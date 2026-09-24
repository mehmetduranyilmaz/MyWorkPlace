using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using Npgsql;

namespace MyWorkplace.BuildingBlocks.Messaging;

/// <summary>
/// EN: The one message handler the library sees per event type (ADR-023). It gives every event the same treatment before
///     the service's <see cref="IEventHandler{TEvent}"/> runs: a fresh scope acting for the event's tenant, a skip if the
///     event was already processed, and one save for the handler's changes plus the "processed" mark.
/// TR: Kütüphanenin her olay tipi için gördüğü tek mesaj handler'ı (ADR-023). Servisin <see cref="IEventHandler{TEvent}"/>'ı
///     çalışmadan önce her olaya aynı muameleyi yapar: olayın firması adına çalışan yeni bir kapsam, olay zaten işlendiyse
///     atlama ve handler'ın değişiklikleri ile "işlendi" işareti için tek bir kaydetme.
/// </summary>
/// <typeparam name="TEvent">EN: The event type. TR: Olay tipi.</typeparam>
/// <param name="scopes">EN: Creates one scope per message. TR: Her mesaj için bir kapsam oluşturur.</param>
/// <param name="time">EN: Clock. TR: Saat.</param>
public sealed class EventDispatcher<TEvent>(IServiceScopeFactory scopes, TimeProvider time)
    where TEvent : IntegrationEvent
{
    /// <summary>EN: Name of the processed-events primary key. TR: İşlenmiş olaylar birincil anahtarının adı.</summary>
    private const string ProcessedEventKey = "pk_processed_events";

    /// <summary>
    /// EN: Called by the messaging library for every delivered <typeparamref name="TEvent"/>.
    /// TR: Mesajlaşma kütüphanesi tarafından teslim edilen her <typeparamref name="TEvent"/> için çağrılır.
    /// </summary>
    /// <param name="message">EN: The event. TR: Olay.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public async Task Handle(TEvent message, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        // EN: Before anything touches the database: the tenant filter must see the event's tenant.
        // TR: Veritabanına dokunan herhangi bir şeyden önce: firma filtresi olayın firmasını görmelidir.
        services.GetRequiredService<MessageActor>().ActFor(message.TenantId);
        var db = services.GetRequiredService<ServiceDbContext>();
        var handler = services.GetRequiredService<IEventHandler<TEvent>>();

        // EN: One explicit transaction around the whole unit: the handler may also change rows directly (e.g. an atomic
        //     stock update), and those must commit or roll back together with its tracked changes and the "processed"
        //     mark. Inside the execution strategy, so a transient failure re-runs the unit from a clean state.
        // TR: Tüm birimin etrafında tek bir açık transaction: handler satırları doğrudan da değiştirebilir (ör. atomik bir stok
        //     güncellemesi) ve bunlar, takip edilen değişiklikleri ve "işlendi" işaretiyle birlikte kaydedilmeli ya da geri
        //     alınmalıdır. Execution strategy içindedir; geçici bir hata birimi temiz bir durumdan yeniden çalıştırır.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                if (await db.ProcessedEvents.AnyAsync(e => e.EventId == message.EventId, ct))
                {
                    return;
                }

                await handler.HandleAsync(message, ct);
                db.ProcessedEvents.Add(new ProcessedEvent
                {
                    EventId = message.EventId,
                    EventType = typeof(TEvent).Name,
                    TenantId = message.TenantId,
                    ProcessedAt = time.GetUtcNow(),
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: ProcessedEventKey })
                {
                    // EN: The same event was processed in parallel and committed first; ours rolls back. Done.
                    // TR: Aynı olay paralel işlendi ve önce kaydedildi; bizimki geri alınır. Tamam.
                }
            },
            cancellationToken);
    }
}
