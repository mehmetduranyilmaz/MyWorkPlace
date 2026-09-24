namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Verifies that the gateway forwards authorized requests to the services.
/// TR: Gateway'in yetkili istekleri servislere ilettiğini doğrular.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class GatewayRoutingTests(AppFixture app)
{
    [Fact]
    public async Task Gateway_CustomersRouteWithToken_ForwardsToCustomersService()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await IdentityApi.CreateSignedInClientAsync(app, ct);

        using var response = await client.GetAsync("/customers/info", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("customers", await response.Content.ReadAsStringAsync(ct));
    }
}
