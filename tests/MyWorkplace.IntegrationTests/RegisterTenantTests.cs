using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Sign-up through the gateway against the real system and database (T-006).
/// TR: Gerçek sistem ve veritabanı üzerinde, gateway üzerinden kayıt (T-006).
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class RegisterTenantTests(AppFixture app)
{
    /// <summary>EN: A password that satisfies the rules. TR: Kurallara uyan bir parola.</summary>
    private const string ValidPassword = "Correct-Horse-9";

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_ValidRequest_Returns201WithIds()
    {
        using var client = app.CreateGatewayClient();

        using var response = await RegisterAsync(client, "Acme Ltd", UniqueEmail(), ValidPassword);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.NotEqual(Guid.Empty, body.GetProperty("tenantId").GetGuid());
        Assert.NotEqual(Guid.Empty, body.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Register_NewTenant_IsOnBasicPlan()
    {
        using var client = app.CreateGatewayClient();
        using var response = await RegisterAsync(client, "Basic Co", UniqueEmail(), ValidPassword);
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tenantId").GetGuid();

        var plan = await QueryScalarAsync<string>("select plan from tenants where id = @id", tenantId);

        Assert.Equal("Basic", plan);
    }

    [Theory]
    [InlineData(null, "valid", ValidPassword, "companyName")]
    [InlineData("Acme", "not-an-email", ValidPassword, "email")]
    [InlineData("Acme", "valid", "short", "password")]
    public async Task Register_InvalidField_Returns400WithFieldError(
        string? companyName, string email, string password, string invalidField)
    {
        using var client = app.CreateGatewayClient();

        using var response = await RegisterAsync(
            client, companyName, email == "valid" ? UniqueEmail() : email, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(
            errors.EnumerateObject(),
            error => string.Equals(error.Name, invalidField, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Register_EmailAlreadyRegisteredInAnotherCase_Returns409()
    {
        using var client = app.CreateGatewayClient();
        var email = UniqueEmail();
        using var first = await RegisterAsync(client, "First", email, ValidPassword);

        using var second = await RegisterAsync(client, "Second", email.ToUpperInvariant(), ValidPassword);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Register_StoresOnlyPasswordHash()
    {
        using var client = app.CreateGatewayClient();
        using var response = await RegisterAsync(client, "Hash Co", UniqueEmail(), ValidPassword);
        var userId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("userId").GetGuid();

        var stored = await QueryScalarAsync<string>("select password_hash from users where id = @id", userId);

        Assert.NotEqual(ValidPassword, stored);
        Assert.DoesNotContain(ValidPassword, stored);
        var verification = new PasswordHasher<object>().VerifyHashedPassword(new object(), stored, ValidPassword);
        Assert.Equal(PasswordVerificationResult.Success, verification);
    }

    /// <summary>
    /// EN: Posts a sign-up request through the gateway.
    /// TR: Gateway üzerinden bir kayıt isteği gönderir.
    /// </summary>
    /// <param name="client">EN: Gateway client. TR: Gateway istemcisi.</param>
    /// <param name="companyName">EN: Company name. TR: Firma adı.</param>
    /// <param name="email">EN: Email. TR: E-posta.</param>
    /// <param name="password">EN: Password. TR: Parola.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string? companyName, string email, string password) =>
        client.PostAsJsonAsync("/identity/register", new { companyName, email, password }, Ct);

    /// <summary>
    /// EN: An email no other test uses.
    /// TR: Başka hiçbir testin kullanmadığı bir e-posta.
    /// </summary>
    /// <returns>EN: The email. TR: E-posta.</returns>
    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// EN: Reads one value directly from identity-db, bypassing the API — to check what is really stored.
    /// TR: API'yi atlayarak identity-db'den doğrudan tek bir değer okur — gerçekte ne saklandığını görmek için.
    /// </summary>
    /// <typeparam name="T">EN: Value type. TR: Değer tipi.</typeparam>
    /// <param name="sql">EN: Query with an @id parameter. TR: @id parametreli sorgu.</param>
    /// <param name="id">EN: Row id. TR: Satır kimliği.</param>
    /// <returns>EN: The value. TR: Değer.</returns>
    private async Task<T> QueryScalarAsync<T>(string sql, Guid id)
    {
        var connectionString = await app.App.GetConnectionStringAsync("identity-db", Ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
