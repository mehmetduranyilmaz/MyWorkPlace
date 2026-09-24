using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using static MyWorkplace.IntegrationTests.IdentityApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Basic → Pro through the gateway (T-011): a fresh token opens Pro routes immediately, the old one does not.
/// TR: Gateway üzerinden Basic → Pro (T-011): taze token Pro rotaları hemen açar, eskisi açmaz.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class PlanUpgradeTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upgrade_ReturnsProToken_ThatOpensProRoutes_WhileTheOldTokenDoesNot()
    {
        var oldToken = await GetAccessTokenAsync(app, Ct);
        using var client = app.CreateGatewayClient();
        Authorize(client, oldToken);

        using var upgrade = await UpgradeAsync(client, Ct);

        Assert.Equal(HttpStatusCode.OK, upgrade.StatusCode);
        var body = await upgrade.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("pro", body.GetProperty("plan").GetString());
        Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());
        var newToken = body.GetProperty("accessToken").GetString()!;
        Assert.Equal("pro", new JsonWebTokenHandler().ReadJsonWebToken(newToken).GetClaim("plan").Value);

        using var withNew = await GetInventoryAsync(newToken);
        using var withOld = await GetInventoryAsync(oldToken);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        // EN: ADR-006: the plan lives in the token, so the old token keeps "basic" until it expires.
        // TR: ADR-006: plan token'ın içindedir; eski token süresi dolana kadar "basic" kalır.
        Assert.Equal(HttpStatusCode.Forbidden, withOld.StatusCode);
    }

    [Fact]
    public async Task Upgrade_Twice_IsIdempotent_AndRecordedOnceInHistory()
    {
        using var client = app.CreateGatewayClient();
        var email = UniqueEmail();
        var (tenantId, userId) = await RegisterNewTenantAsync(client, email, Ct);
        using var login = await LoginAsync(client, email, ValidPassword, Ct);
        Authorize(client, (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);

        using var first = await UpgradeAsync(client, Ct);
        using var second = await UpgradeAsync(client, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var connectionString = await app.App.GetConnectionStringAsync("identity-db", Ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "select old_value, new_value, changed_by from audit_log where entity_id = @id and property = 'Plan'", connection);
        command.Parameters.AddWithValue("id", tenantId);
        await using var reader = await command.ExecuteReaderAsync(Ct);

        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal(("Basic", "Pro", userId), (reader.GetString(0), reader.GetString(1), reader.GetGuid(2)));
        Assert.False(await reader.ReadAsync(Ct), "A repeated upgrade must not add a second history row.");
    }

    [Fact]
    public async Task Upgrade_WithoutToken_Returns401()
    {
        using var client = app.CreateGatewayClient();

        using var response = await UpgradeAsync(client, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// EN: Calls a Pro route through the gateway with the given token.
    /// TR: Verilen token ile gateway üzerinden bir Pro rotayı çağırır.
    /// </summary>
    /// <param name="token">EN: Access token. TR: Erişim token'ı.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private async Task<HttpResponseMessage> GetInventoryAsync(string token)
    {
        using var client = app.CreateGatewayClient();
        Authorize(client, token);
        return await client.GetAsync("/inventory/items", Ct);
    }
}
