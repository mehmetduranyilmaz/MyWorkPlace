namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Second layer of ADR-006: services protect themselves even when the gateway is bypassed (T-008).
///     Each test calls a service <b>directly</b>, as an attacker on the internal network would.
/// TR: ADR-006'nın ikinci katmanı: gateway atlansa bile servisler kendilerini korur (T-008).
///     Her test, iç ağdaki bir saldırganın yapacağı gibi bir servisi <b>doğrudan</b> çağırır.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class DefenseInDepthTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("customers", "/customers/00000000-0000-0000-0000-000000000001")]
    [InlineData("inventory", "/inventory/info")]
    public async Task ServiceCalledDirectly_WithoutToken_Returns401(string service, string path)
    {
        using var client = app.CreateDirectServiceClient(service);

        using var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProService_CalledDirectlyWithBasicToken_Returns403()
    {
        var token = await IdentityApi.GetAccessTokenAsync(app, Ct);
        using var client = app.CreateDirectServiceClient("inventory");
        IdentityApi.Authorize(client, token);

        using var response = await client.GetAsync("/inventory/info", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BasicService_CalledDirectlyWithValidToken_AcceptsTheToken()
    {
        // EN: 404 (not 401) proves the service accepted the token: same issuer, audience and keys as the gateway.
        // TR: 404 (401 değil), servisin token'ı kabul ettiğini kanıtlar: gateway'le aynı üretici, hedef kitle ve anahtarlar.
        var token = await IdentityApi.GetAccessTokenAsync(app, Ct);
        using var client = app.CreateDirectServiceClient("customers");
        IdentityApi.Authorize(client, token);

        using var response = await client.GetAsync($"/customers/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
