namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Verifies that the gateway forwards requests to the services.
/// TR: Gateway'in istekleri servislere ilettiğini doğrular.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class GatewayRoutingTests(AppFixture app)
{
    [Fact]
    public async Task Gateway_CustomersRoute_ForwardsToCustomersService()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = app.CreateGatewayClient();

        using var response = await client.GetAsync("/customers/info", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("customers", await response.Content.ReadAsStringAsync(ct));
    }
}
