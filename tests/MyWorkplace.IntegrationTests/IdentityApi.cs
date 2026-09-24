using System.Net.Http.Json;
using System.Text.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Small client helpers for the Identity endpoints, shared by the integration tests.
/// TR: Entegrasyon testlerinin paylaştığı, Identity uç noktaları için küçük istemci yardımcıları.
/// </summary>
internal static class IdentityApi
{
    /// <summary>EN: A password that satisfies the rules. TR: Kurallara uyan bir parola.</summary>
    public const string ValidPassword = "Correct-Horse-9";

    /// <summary>
    /// EN: An email no other test uses.
    /// TR: Başka hiçbir testin kullanmadığı bir e-posta.
    /// </summary>
    /// <returns>EN: The email. TR: E-posta.</returns>
    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// EN: Posts a sign-up request.
    /// TR: Bir kayıt isteği gönderir.
    /// </summary>
    /// <param name="client">EN: Gateway client. TR: Gateway istemcisi.</param>
    /// <param name="companyName">EN: Company name. TR: Firma adı.</param>
    /// <param name="email">EN: Email. TR: E-posta.</param>
    /// <param name="password">EN: Password. TR: Parola.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string? companyName, string email, string password, CancellationToken ct) =>
        client.PostAsJsonAsync("/identity/register", new { companyName, email, password }, ct);

    /// <summary>
    /// EN: Posts a sign-in request.
    /// TR: Bir giriş isteği gönderir.
    /// </summary>
    /// <param name="client">EN: Gateway client. TR: Gateway istemcisi.</param>
    /// <param name="email">EN: Email. TR: E-posta.</param>
    /// <param name="password">EN: Password. TR: Parola.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string email, string password, CancellationToken ct) =>
        client.PostAsJsonAsync("/identity/login", new { email, password }, ct);

    /// <summary>
    /// EN: Registers a new (Basic) company, signs in and returns a client that sends its access token.
    /// TR: Yeni bir (Basic) firma kaydeder, giriş yapar ve erişim token'ını gönderen bir istemci döner.
    /// </summary>
    /// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: An authenticated gateway client. TR: Kimliği doğrulanmış gateway istemcisi.</returns>
    public static async Task<HttpClient> CreateSignedInClientAsync(AppFixture app, CancellationToken ct)
    {
        var client = app.CreateGatewayClient();
        Authorize(client, await GetAccessTokenAsync(app, ct));
        return client;
    }

    /// <summary>
    /// EN: Registers a new (Basic) company, signs in through the gateway and returns the access token.
    /// TR: Yeni bir (Basic) firma kaydeder, gateway üzerinden giriş yapar ve erişim token'ını döner.
    /// </summary>
    /// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The access token. TR: Erişim token'ı.</returns>
    public static async Task<string> GetAccessTokenAsync(AppFixture app, CancellationToken ct)
    {
        using var client = app.CreateGatewayClient();
        var email = UniqueEmail();
        await RegisterNewTenantAsync(client, email, ct);
        using var login = await LoginAsync(client, email, ValidPassword, ct);
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>
    /// EN: Makes <paramref name="client"/> send <paramref name="token"/> as a Bearer token.
    /// TR: <paramref name="client"/>'ın <paramref name="token"/>'ı Bearer token olarak göndermesini sağlar.
    /// </summary>
    /// <param name="client">EN: Any client. TR: Herhangi bir istemci.</param>
    /// <param name="token">EN: Access token. TR: Erişim token'ı.</param>
    public static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    /// <summary>
    /// EN: Registers a new company and returns its ids.
    /// TR: Yeni bir firma kaydeder ve kimliklerini döner.
    /// </summary>
    /// <param name="client">EN: Gateway client. TR: Gateway istemcisi.</param>
    /// <param name="email">EN: Email of the first user. TR: İlk kullanıcının e-postası.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Tenant and user ids. TR: Firma ve kullanıcı kimlikleri.</returns>
    public static async Task<(Guid TenantId, Guid UserId)> RegisterNewTenantAsync(
        HttpClient client, string email, CancellationToken ct)
    {
        using var response = await RegisterAsync(client, "Test Co", email, ValidPassword, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (body.GetProperty("tenantId").GetGuid(), body.GetProperty("userId").GetGuid());
    }
}
