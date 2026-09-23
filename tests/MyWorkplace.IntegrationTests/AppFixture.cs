using Aspire.Hosting;
using MyWorkplace.IntegrationTests;

[assembly: AssemblyFixture(typeof(AppFixture))]

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Starts the whole system (PostgreSQL, services, gateway) once for all integration tests.
///     Tests create their own data with unique emails, so they can share the running system.
/// TR: Tüm sistemi (PostgreSQL, servisler, gateway) bütün entegrasyon testleri için bir kez başlatır.
///     Testler kendi verilerini benzersiz e-postalarla oluşturur; bu yüzden çalışan sistemi paylaşabilir.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    /// <summary>
    /// EN: Generous: the first run may pull the PostgreSQL image and builds every service.
    /// TR: Geniş tutuldu: ilk çalıştırma PostgreSQL imajını indirebilir ve her servisi derler.
    /// </summary>
    private static readonly TimeSpan _startupTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// EN: The running system.
    /// TR: Çalışan sistem.
    /// </summary>
    public DistributedApplication App { get; private set; } = null!;

    /// <summary>
    /// EN: HTTP client pointed at the gateway — the only public entry point.
    /// TR: Gateway'e yönelik HTTP istemcisi — tek açık giriş noktası.
    /// </summary>
    /// <returns>EN: A new client. TR: Yeni bir istemci.</returns>
    public HttpClient CreateGatewayClient() => App.CreateHttpClient("gateway");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(_startupTimeout);

        // EN: Fresh database per run and no PgWeb (see AppHost).
        // TR: Her çalıştırmada temiz veritabanı ve PgWeb yok (bkz. AppHost).
        // EN: No retrying HTTP handler on purpose: a retried POST could register twice and hide real behavior.
        // TR: Bilerek yeniden deneyen HTTP handler yok: tekrarlanan bir POST iki kez kayıt yapıp gerçek davranışı gizleyebilir.
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MyWorkplace_AppHost>(["Storage:Ephemeral=true"], timeout.Token);

        App = await appHost.BuildAsync(timeout.Token);
        await App.StartAsync(timeout.Token);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => App.DisposeAsync();
}
