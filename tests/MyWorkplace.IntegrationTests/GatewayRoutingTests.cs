namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Starts the real system through the AppHost and verifies that the gateway forwards requests to services.
/// TR: Gerçek sistemi AppHost üzerinden başlatır ve gateway'in istekleri servislere ilettiğini doğrular.
/// </summary>
public sealed class GatewayRoutingTests
{
    /// <summary>
    /// EN: Upper bound for starting the system; generous because the first run builds and starts every service.
    /// TR: Sistemin başlaması için üst sınır; ilk çalıştırmada her servis derlenip başladığı için geniş tutuldu.
    /// </summary>
    private static readonly TimeSpan _startupTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Gateway_CustomersRoute_ForwardsToCustomersService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MyWorkplace_AppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken)
            .WaitAsync(_startupTimeout, cancellationToken);
        await app.StartAsync(cancellationToken)
            .WaitAsync(_startupTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", cancellationToken)
            .WaitAsync(_startupTimeout, cancellationToken);

        using var client = app.CreateHttpClient("gateway");
        using var response = await client.GetAsync("/customers/info", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("customers", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
