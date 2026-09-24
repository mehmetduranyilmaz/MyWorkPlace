using System.Net.Http.Json;

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

        using var response = await client.PostAsJsonAsync("/customers", new { name = "Routed Ltd" }, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
