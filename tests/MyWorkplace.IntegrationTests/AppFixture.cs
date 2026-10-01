using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using MyWorkplace.IntegrationTests;

[assembly: AssemblyFixture(typeof(AppFixture))]

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Starts the whole system (PostgreSQL, services, gateway) once for all integration tests.
///     Tests create their own data with unique emails, so they can share the running system.
/// TR: Tüm sistemi (PostgreSQL, servisler, gateway) bütün entegrasyon testleri için bir kez başlatır.
///     Testler kendi verilerini benzersiz e-postalarla oluşturur; bu yüzden çalışan sistemi paylaşabilir.
/// </summary>
public class AppFixture : IAsyncLifetime
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

    /// <summary>
    /// EN: HTTP client pointed directly at a service, <b>bypassing the gateway</b> — plays an attacker on the internal network.
    /// TR: Doğrudan bir servise yönelik, <b>gateway'i atlayan</b> HTTP istemcisi — iç ağdaki bir saldırganı canlandırır.
    /// </summary>
    /// <param name="resourceName">EN: Aspire resource name, e.g. "inventory". TR: Aspire kaynak adı, ör. "inventory".</param>
    /// <returns>EN: A new client. TR: Yeni bir istemci.</returns>
    public HttpClient CreateDirectServiceClient(string resourceName) => App.CreateHttpClient(resourceName);

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

        // EN: Tests run without the Aspire dashboard, so nothing listens on the OpenTelemetry endpoint the services are
        //     given. On shutdown their exporter then waits for it — ~8 s on Windows, over DCP's 12 s stop window on Linux,
        //     where DCP kills the process and loses track of it (state "Unknown", T-044). No dashboard, no endpoint: the
        //     services export nothing and stop in under a second. Development and production keep their telemetry.
        // TR: Testler Aspire paneli olmadan çalışır; bu yüzden servislere verilen OpenTelemetry adresini kimse dinlemez. Kapanışta exporter
        //     onu bekler — Windows'ta ~8 sn, Linux'ta DCP'nin 12 sn'lik durdurma süresinin üstünde; orada DCP süreci öldürür ve izini kaybeder
        //     (durum "Unknown", T-044). Panel yoksa adres de yok: servisler hiçbir şey göndermez ve bir saniyenin altında durur. Geliştirme ve
        //     production telemetrilerini korur.
        foreach (var project in appHost.Resources.OfType<ProjectResource>())
        {
            project.Annotations.Add(new EnvironmentCallbackAnnotation(
                context => context.EnvironmentVariables.Remove("OTEL_EXPORTER_OTLP_ENDPOINT")));
        }

        App = await appHost.BuildAsync(timeout.Token);
        await App.StartAsync(timeout.Token);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// EN: A second, private copy of the whole system for tests that break it on purpose (e.g. stop a service). Aspire
///     picks random ports and container names for test runs, so both copies run side by side without interfering.
/// TR: Sistemi bilerek bozan testler (ör. bir servisi durduran) için tüm sistemin ikinci, özel bir kopyası. Aspire test
///     çalıştırmalarında rastgele port ve konteyner adları seçer; böylece iki kopya birbirini etkilemeden yan yana çalışır.
/// </summary>
public sealed class IsolatedAppFixture : AppFixture;

/// <summary>
/// EN: Tests that take a service down run in this collection: on their own, after all parallel tests have finished.
///     A second system starting and a service restarting compete for the CI machine; timing-sensitive tests running
///     at the same moment could then miss their deadlines (T-043).
/// TR: Bir servisi kapatan testler bu koleksiyonda çalışır: tek başlarına, tüm paralel testler bittikten sonra. İkinci bir sistemin
///     başlaması ve bir servisin yeniden başlaması CI makinesi için yarışır; aynı anda çalışan zamana duyarlı testler süre sınırlarını
///     kaçırabilirdi (T-043).
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class ServiceOutageCollection;
