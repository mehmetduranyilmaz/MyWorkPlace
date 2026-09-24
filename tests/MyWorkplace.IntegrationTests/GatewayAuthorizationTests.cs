using System.Net.Http.Headers;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The gateway as gatekeeper (T-007): no token → 401, bad token → 401, Basic on a Pro route → 403.
/// TR: Kapı görevlisi olarak gateway (T-007): token yok → 401, bozuk token → 401, Pro rotada Basic → 403.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class GatewayAuthorizationTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProtectedRoute_WithoutToken_Returns401ProblemDetails()
    {
        using var client = app.CreateGatewayClient();

        using var response = await client.GetAsync($"/customers/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProtectedRoute_WithTamperedToken_Returns401()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;

        // EN: Change one character in the middle of the signature (not the last one: its low bits are only padding).
        //     The payload is intact, but the signature no longer matches.
        // TR: İmzanın ortasındaki bir karakteri değiştir (sonuncuyu değil: onun alt bitleri sadece dolgudur).
        //     İçerik aynı, ama imza artık tutmuyor.
        var index = token.LastIndexOf('.') + 10;
        var tampered = token[..index] + (token[index] == 'A' ? 'B' : 'A') + token[(index + 1)..];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        using var response = await client.GetAsync($"/customers/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProRoute_WithBasicPlanToken_Returns403ProblemDetails()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);

        using var response = await client.GetAsync("/inventory/info", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PublicRoutes_WithoutToken_StayOpen()
    {
        using var client = app.CreateGatewayClient();

        using var jwks = await client.GetAsync("/identity/.well-known/jwks.json", Ct);
        using var login = await IdentityApi.LoginAsync(client, IdentityApi.UniqueEmail(), IdentityApi.ValidPassword, Ct);

        Assert.Equal(HttpStatusCode.OK, jwks.StatusCode);
        // EN: 401 from Identity (unknown user) proves the gateway let the anonymous request through.
        // TR: Identity'den gelen 401 (bilinmeyen kullanıcı), gateway'in anonim isteği geçirdiğini kanıtlar.
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("Invalid email or password", await login.Content.ReadAsStringAsync(Ct));
    }
}
