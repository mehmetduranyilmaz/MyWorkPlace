using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Client helpers for the user-management endpoints (T-037).
/// TR: Kullanıcı yönetimi uç noktaları için istemci yardımcıları (T-037).
/// </summary>
internal static class UsersApi
{
    /// <summary>
    /// EN: Registers a new company and returns a client signed in as its Owner, with the Owner's id.
    /// TR: Yeni bir firma kaydeder; Sahip'i olarak giriş yapmış bir istemciyi ve Sahip'in kimliğini döner.
    /// </summary>
    /// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Owner client and id. TR: Sahip istemcisi ve kimliği.</returns>
    public static async Task<(HttpClient Owner, Guid OwnerId)> CreateCompanyAsync(AppFixture app, CancellationToken ct)
    {
        using var anonymous = app.CreateGatewayClient();
        var email = UniqueEmail();
        var (_, ownerId) = await RegisterNewTenantAsync(anonymous, email, ct);
        return (await SignInAsync(app, email, ct), ownerId);
    }

    /// <summary>
    /// EN: Posts a new user.
    /// TR: Yeni bir kullanıcı gönderir.
    /// </summary>
    /// <param name="client">EN: Client of a user manager. TR: Kullanıcı yöneticisinin istemcisi.</param>
    /// <param name="body">EN: User fields. TR: Kullanıcı alanları.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, object body, CancellationToken ct) =>
        client.PostAsJsonAsync("/identity/users", body, ct);

    /// <summary>
    /// EN: Adds a user with one role and returns its id and email; fails the test if that fails.
    /// TR: Tek rollü bir kullanıcı ekler, kimliğini ve e-postasını döner; başarısızsa testi düşürür.
    /// </summary>
    /// <param name="client">EN: Client of a user manager. TR: Kullanıcı yöneticisinin istemcisi.</param>
    /// <param name="role">EN: Role name. TR: Rol adı.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Id and email. TR: Kimlik ve e-posta.</returns>
    public static async Task<(Guid Id, string Email)> AddUserAsync(HttpClient client, string role, CancellationToken ct)
    {
        var email = UniqueEmail();
        using var response = await CreateAsync(client, new { email, password = ValidPassword, roles = new[] { role } }, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid(), email);
    }

    /// <summary>
    /// EN: Adds a user with one role and returns a client signed in as that user.
    /// TR: Tek rollü bir kullanıcı ekler ve o kullanıcı olarak giriş yapmış bir istemci döner.
    /// </summary>
    /// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
    /// <param name="owner">EN: Owner client of the company. TR: Firmanın Sahip istemcisi.</param>
    /// <param name="role">EN: Role name. TR: Rol adı.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The signed-in client. TR: Giriş yapmış istemci.</returns>
    public static async Task<HttpClient> SignInAsNewUserAsync(
        AppFixture app, HttpClient owner, string role, CancellationToken ct)
    {
        var (_, email) = await AddUserAsync(owner, role, ct);
        return await SignInAsync(app, email, ct);
    }

    /// <summary>
    /// EN: Signs in and returns a gateway client that sends the token.
    /// TR: Giriş yapar ve token'ı gönderen bir gateway istemcisi döner.
    /// </summary>
    /// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
    /// <param name="email">EN: Email. TR: E-posta.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The signed-in client. TR: Giriş yapmış istemci.</returns>
    public static async Task<HttpClient> SignInAsync(AppFixture app, string email, CancellationToken ct)
    {
        var client = app.CreateGatewayClient();
        using var login = await LoginAsync(client, email, ValidPassword, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Authorize(client, (await login.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accessToken").GetString()!);
        return client;
    }

    /// <summary>
    /// EN: Reads a user's current ETag.
    /// TR: Bir kullanıcının güncel ETag'ini okur.
    /// </summary>
    /// <param name="client">EN: Client of a user manager. TR: Kullanıcı yöneticisinin istemcisi.</param>
    /// <param name="id">EN: User id. TR: Kullanıcı kimliği.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The ETag. TR: ETag.</returns>
    public static async Task<string> GetETagAsync(HttpClient client, Guid id, CancellationToken ct)
    {
        using var response = await client.GetAsync($"/identity/users/{id}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.ETag!.ToString();
    }

    /// <summary>
    /// EN: Replaces a user's roles and extra permissions.
    /// TR: Bir kullanıcının rollerini ve ek izinlerini değiştirir.
    /// </summary>
    /// <param name="client">EN: Client of a user manager. TR: Kullanıcı yöneticisinin istemcisi.</param>
    /// <param name="id">EN: User id. TR: Kullanıcı kimliği.</param>
    /// <param name="roles">EN: New roles. TR: Yeni roller.</param>
    /// <param name="extraPermissions">EN: New extra permissions. TR: Yeni ek izinler.</param>
    /// <param name="ifMatch">EN: ETag to send, or null. TR: Gönderilecek ETag veya null.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> UpdateAccessAsync(
        HttpClient client, Guid id, string[] roles, string[] extraPermissions, string? ifMatch, CancellationToken ct) =>
        client.PutWithIfMatchAsync($"/identity/users/{id}/access", new { roles, extraPermissions }, ifMatch, ct);
}
