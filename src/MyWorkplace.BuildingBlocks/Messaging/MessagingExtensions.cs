using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace MyWorkplace.BuildingBlocks.Messaging;

/// <summary>
/// EN: Joins a service to cross-service events in one call (ADR-023). Wolverine is used only here: services see
///     <see cref="IEventOutbox"/> and <see cref="IEventHandler{TEvent}"/>, so the library can be replaced without touching them.
/// TR: Bir servisi tek çağrıda servisler arası olaylara bağlar (ADR-023). Wolverine sadece burada kullanılır: servisler
///     <see cref="IEventOutbox"/> ve <see cref="IEventHandler{TEvent}"/> görür; böylece kütüphane onlara dokunmadan değiştirilebilir.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>EN: Aspire connection name of the broker. TR: Mesaj aracının Aspire bağlantı adı.</summary>
    public const string BrokerConnectionName = "messaging";

    /// <summary>
    /// EN: Database schema of the library's own outbox / inbox tables, created by the library, outside EF migrations.
    /// TR: Kütüphanenin kendi outbox / inbox tablolarının şeması; EF migration'larının dışında, kütüphane tarafından oluşturulur.
    /// </summary>
    public const string MessageStoreSchema = "wolverine";

    /// <summary>
    /// EN: Adds messaging: RabbitMQ, the outbox and inbox in the service's database, and every
    ///     <see cref="IEventHandler{TEvent}"/> of the service's assembly (the one declaring <typeparamref name="TContext"/>).
    ///     Each service gets its own queue per event, so every interested service receives every event.
    /// TR: Mesajlaşmayı ekler: RabbitMQ, servisin veritabanında outbox ve inbox ve servis derlemesindeki (<typeparamref name="TContext"/>'i
    ///     tanımlayan) her <see cref="IEventHandler{TEvent}"/>. Her servis her olay için kendi kuyruğunu alır; böylece ilgilenen her
    ///     servis her olayı alır.
    /// </summary>
    /// <typeparam name="TContext">EN: The service's DbContext. TR: Servisin DbContext'i.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <param name="databaseConnectionName">EN: The service's database connection name. TR: Servisin veritabanı bağlantı adı.</param>
    /// <returns>EN: The same builder. TR: Aynı builder.</returns>
    public static IHostApplicationBuilder AddServiceMessaging<TContext>(
        this IHostApplicationBuilder builder,
        string databaseConnectionName)
        where TContext : ServiceDbContext
    {
        var database = RequiredConnectionString(builder, databaseConnectionName);
        var broker = RequiredConnectionString(builder, BrokerConnectionName);
        var serviceName = builder.Environment.ApplicationName.ToLowerInvariant();
        var handlers = FindHandlers(typeof(TContext).Assembly);

        foreach (var (eventType, handlerType) in handlers)
        {
            builder.Services.AddScoped(typeof(IEventHandler<>).MakeGenericType(eventType), handlerType);
        }

        builder.Services.AddScoped<IEventOutbox, WolverineEventOutbox<TContext>>();
        builder.UseWolverine(options =>
        {
            options.ServiceName = serviceName;

            // EN: Only our dispatchers are handlers; nothing in the service is picked up by naming conventions.
            // TR: Sadece bizim dağıtıcılarımız handler'dır; servisteki hiçbir şey isim kurallarıyla yakalanmaz.
            options.Discovery.DisableConventionalDiscovery();
            foreach (var (eventType, _) in handlers)
            {
                options.Discovery.IncludeType(typeof(EventDispatcher<>).MakeGenericType(eventType));
            }

            options.PersistMessagesWithPostgresql(database, MessageStoreSchema);
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Policies.UseDurableInboxOnAllListeners();

            // EN: Events always travel through the broker, even when this service consumes its own events; otherwise
            //     they would be handed over in memory and every other interested service would miss them.
            // TR: Olaylar, servis kendi olaylarını dinlese bile her zaman mesaj aracından geçer; aksi halde bellek içinde
            //     teslim edilir ve ilgilenen diğer servisler onları kaçırırdı.
            options.Policies.DisableConventionalLocalRouting();

            // EN: One exchange per event type; one queue per service and event type bound to it.
            // TR: Her olay tipi için bir exchange; ona bağlı, her servis ve olay tipi için bir kuyruk.
            options.UseRabbitMq(new Uri(broker))
                .AutoProvision()
                .UseConventionalRouting(routing => routing
                    .IncludeTypes(type => type.IsAssignableTo(typeof(IntegrationEvent)))
                    .QueueNameForListener(type => $"{serviceName}.{type.Name}"));
        });

        return builder;
    }

    /// <summary>
    /// EN: Every concrete <see cref="IEventHandler{TEvent}"/> in <paramref name="assembly"/>, with its event type.
    /// TR: <paramref name="assembly"/> içindeki her somut <see cref="IEventHandler{TEvent}"/>, olay tipiyle birlikte.
    /// </summary>
    /// <param name="assembly">EN: The service's assembly. TR: Servisin derlemesi.</param>
    /// <returns>EN: Event and handler types. TR: Olay ve handler tipleri.</returns>
    private static List<(Type EventType, Type HandlerType)> FindHandlers(Assembly assembly) =>
    [
        .. assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                .Select(i => (i.GetGenericArguments()[0], type))),
    ];

    /// <summary>
    /// EN: Reads a connection string or explains which AppHost reference is missing.
    /// TR: Bir bağlantı cümlesini okur ya da AppHost'ta hangi referansın eksik olduğunu açıklar.
    /// </summary>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <param name="name">EN: Connection name. TR: Bağlantı adı.</param>
    /// <returns>EN: The connection string. TR: Bağlantı cümlesi.</returns>
    private static string RequiredConnectionString(IHostApplicationBuilder builder, string name) =>
        builder.Configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException(
            $"Connection string '{name}' is missing. Is it referenced in the AppHost?");
}

/// <summary>
/// EN: <see cref="IEventOutbox"/> on Wolverine's EF Core outbox. Wolverine opens its transaction as soon as an event is
///     published, and the retrying execution strategy (Aspire) refuses a transaction it didn't start. So events are
///     collected here and handed to Wolverine only inside the strategy. If a transient failure makes the strategy retry,
///     an event may be stored twice — harmless: it keeps its id, and consumers skip what they already processed.
/// TR: Wolverine'in EF Core outbox'ı üzerinde <see cref="IEventOutbox"/>. Wolverine bir olay yayınlanır yayınlanmaz transaction'ını
///     açar ve yeniden deneyen execution strategy (Aspire) kendi başlatmadığı transaction'ı reddeder. Bu yüzden olaylar burada
///     toplanır ve Wolverine'e ancak stratejinin içinde verilir. Geçici bir hata stratejiyi yeniden denemeye iterse bir olay iki kez
///     saklanabilir — zararsızdır: kimliğini korur ve dinleyiciler zaten işledikleri şeyi atlar.
/// </summary>
/// <typeparam name="TContext">EN: The service's DbContext. TR: Servisin DbContext'i.</typeparam>
/// <param name="outbox">EN: Wolverine's outbox bound to the scoped context. TR: Kapsamdaki context'e bağlı Wolverine outbox'ı.</param>
internal sealed class WolverineEventOutbox<TContext>(IDbContextOutbox<TContext> outbox) : IEventOutbox
    where TContext : ServiceDbContext
{
    /// <summary>EN: Events waiting for the save. TR: Kaydetmeyi bekleyen olaylar.</summary>
    private readonly List<IntegrationEvent> _pending = [];

    /// <inheritdoc />
    public ValueTask AddAsync(IntegrationEvent integrationEvent)
    {
        _pending.Add(integrationEvent);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await outbox.DbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                foreach (var integrationEvent in _pending)
                {
                    await outbox.PublishAsync(integrationEvent);
                }

                await outbox.SaveChangesAndFlushMessagesAsync(ct);
            },
            cancellationToken);
        _pending.Clear();
    }
}
