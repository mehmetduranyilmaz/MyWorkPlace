using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Test event: "create a note with this title".
/// TR: Test olayı: "bu başlıkla bir not oluştur".
/// </summary>
public sealed record NoteRequested : IntegrationEvent
{
    /// <summary>EN: Title of the note to create. TR: Oluşturulacak notun başlığı.</summary>
    public required string Title { get; init; }
}

/// <summary>
/// EN: What each handled event could see, keyed by event id.
/// TR: İşlenen her olayın neleri görebildiği; olay kimliğine göre.
/// </summary>
public sealed class TenantProbe
{
    /// <summary>EN: Event id → tenant ids of the visible notes. TR: Olay kimliği → görünen notların firma kimlikleri.</summary>
    public ConcurrentDictionary<Guid, Guid[]> VisibleTenants { get; } = new();
}

/// <summary>
/// EN: A consumer as a service would write it: changes the context, doesn't save.
/// TR: Bir servisin yazacağı şekliyle bir dinleyici: context'i değiştirir, kaydetmez.
/// </summary>
/// <param name="db">EN: The service's database. TR: Servisin veritabanı.</param>
/// <param name="probe">EN: Records what was visible. TR: Neyin göründüğünü kaydeder.</param>
public sealed class NoteRequestedHandler(TestDbContext db, TenantProbe probe) : IEventHandler<NoteRequested>
{
    /// <inheritdoc />
    public async Task HandleAsync(NoteRequested integrationEvent, CancellationToken cancellationToken)
    {
        probe.VisibleTenants[integrationEvent.EventId] =
            await db.Notes.Select(n => n.TenantId).Distinct().ToArrayAsync(cancellationToken);
        db.Notes.Add(new TestNote { Title = integrationEvent.Title });
    }
}

/// <summary>
/// EN: A real host with messaging: its own PostgreSQL and RabbitMQ containers (the broker gets paused by a test, so it
///     isn't shared), the test context registered like a service's, and <c>AddServiceMessaging</c>.
/// TR: Mesajlaşmalı gerçek bir host: kendi PostgreSQL ve RabbitMQ konteynerleri (mesaj aracı bir testte duraklatıldığı için
///     paylaşılmaz), bir servisinki gibi kaydedilmiş test context'i ve <c>AddServiceMessaging</c>.
/// </summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    /// <summary>EN: Database container. TR: Veritabanı konteyneri.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage.Reference).Build();

    /// <summary>EN: The host, once started. TR: Başlatıldıktan sonra host.</summary>
    private IHost? _host;

    /// <summary>EN: Broker container. TR: Mesaj aracı konteyneri.</summary>
    public RabbitMqContainer Broker { get; } = new RabbitMqBuilder(RabbitMqImage.Reference).Build();

    /// <summary>EN: The running host's services. TR: Çalışan host'un servisleri.</summary>
    public IServiceProvider Services => _host!.Services;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), Broker.StartAsync());
        var database = _postgres.GetConnectionString();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "buildingblocks-tests",
            EnvironmentName = Environments.Development,
        });
        builder.Configuration["ConnectionStrings:test-db"] = database;
        builder.Configuration["ConnectionStrings:" + MessagingExtensions.BrokerConnectionName] = Broker.GetConnectionString();

        builder.Services.AddBuildingBlocksPersistence();
        builder.Services.AddDbContext<TestDbContext>((services, options) => options
            .UseServiceConventions(
                database,
                services.GetRequiredService<AuditingInterceptor>(),
                services.GetRequiredService<ChangeHistoryInterceptor>())
            // EN: The retrying strategy Aspire adds in the services, so the outbox is tested against it.
            // TR: Aspire'ın servislerde eklediği yeniden deneme stratejisi; böylece outbox ona karşı test edilir.
            .UseNpgsql(database, npgsql => npgsql.EnableRetryOnFailure()));
        builder.Services.AddScoped<ServiceDbContext>(services => services.GetRequiredService<TestDbContext>());
        builder.Services.AddSingleton<TenantProbe>();
        builder.AddServiceMessaging<TestDbContext>("test-db");

        _host = builder.Build();
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        await _host.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Broker.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
