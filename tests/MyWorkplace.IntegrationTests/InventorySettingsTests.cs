using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.UsersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: <c>/inventory/settings</c> through the gateway (T-032, ADR-018): the first module setting, the negative stock policy.
/// TR: Gateway üzerinden <c>/inventory/settings</c> (T-032, ADR-018): ilk modül ayarı, eksi stok politikası.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class InventorySettingsTests(AppFixture app)
{
    /// <summary>EN: Settings address. TR: Ayarların adresi.</summary>
    private const string Url = "/inventory/settings";

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewCompany_GetsTheDefaults()
    {
        using var client = await CreateProClientAsync(app, Ct);

        using var response = await client.GetAsync(Url, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"0\"", response.Headers.ETag!.ToString());
        Assert.Equal("Block", await ReadPolicyAsync(response));
    }

    [Fact]
    public async Task Update_IsVisibleOnTheNextRead_OnlyForThatCompany()
    {
        using var client = await CreateProClientAsync(app, Ct);
        using var other = await CreateProClientAsync(app, Ct);

        using var update = await PutAsync(client, "Warn", "\"0\"");
        using var read = await client.GetAsync(Url, Ct);
        using var otherRead = await other.GetAsync(Url, Ct);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Warn", await ReadPolicyAsync(read));
        Assert.Equal(update.Headers.ETag, read.Headers.ETag);
        Assert.Equal("Block", await ReadPolicyAsync(otherRead));
    }

    [Fact]
    public async Task Update_WithoutOrWithStaleIfMatch_IsRejected()
    {
        using var client = await CreateProClientAsync(app, Ct);
        using var first = await PutAsync(client, "Allow", "\"0\"");

        using var missing = await PutAsync(client, "Warn", ifMatch: null);
        using var stale = await PutAsync(client, "Warn", "\"0\"");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task TwoFirstSavesAtOnce_OneIsRejected()
    {
        using var client = await CreateProClientAsync(app, Ct);

        var responses = await Task.WhenAll(PutAsync(client, "Allow", "\"0\""), PutAsync(client, "Warn", "\"0\""));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.PreconditionFailed],
            responses.Select(r => r.StatusCode).Order());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Update_UnknownValue_Returns400WithTheField()
    {
        using var client = await CreateProClientAsync(app, Ct);

        using var response = await PutAsync(client, "Maybe", "\"0\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(body.GetProperty("errors").TryGetProperty("negativeStockPolicy", out _));
    }

    [Fact]
    public async Task Viewer_CanRead_Member_CannotChange()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        using var upgrade = await UpgradeAsync(owner, Ct);
        upgrade.EnsureSuccessStatusCode();
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await SignInAsNewUserAsync(app, owner, "Member", Ct);

        using var read = await viewer.GetAsync(Url, Ct);
        using var change = await PutAsync(member, "Allow", "\"0\"");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
    }

    [Fact]
    public async Task BasicPlan_IsRefused()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);

        using var response = await client.GetAsync(Url, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// EN: Sends the settings with an optional If-Match.
    /// TR: Ayarları isteğe bağlı If-Match ile gönderir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="policy">EN: Negative stock policy. TR: Eksi stok politikası.</param>
    /// <param name="ifMatch">EN: ETag or null. TR: ETag veya null.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string policy, string? ifMatch) =>
        client.PutWithIfMatchAsync(Url, new { negativeStockPolicy = policy }, ifMatch, Ct);

    /// <summary>
    /// EN: Reads the policy from a settings response.
    /// TR: Bir ayar cevabından politikayı okur.
    /// </summary>
    /// <param name="response">EN: The response. TR: Cevap.</param>
    /// <returns>EN: The policy name. TR: Politika adı.</returns>
    private static async Task<string?> ReadPolicyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("negativeStockPolicy").GetString();
}
