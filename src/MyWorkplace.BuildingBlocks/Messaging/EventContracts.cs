using MyWorkplace.Contracts.Events;

namespace MyWorkplace.BuildingBlocks.Messaging;

/// <summary>
/// EN: Publishes events together with the business change (transactional outbox, ADR-023). Add the events, then call
///     <see cref="SaveChangesAsync"/> instead of the context's own SaveChanges: both are committed in one transaction and
///     the events are sent afterwards — also if the broker is down right now.
/// TR: Olayları iş değişikliğiyle birlikte yayınlar (transactional outbox, ADR-023). Olayları ekleyin, ardından context'in kendi
///     SaveChanges'i yerine <see cref="SaveChangesAsync"/>'i çağırın: ikisi tek transaction'da kaydedilir ve olaylar sonra
///     gönderilir — mesaj aracı şu an kapalı olsa bile.
/// </summary>
public interface IEventOutbox
{
    /// <summary>
    /// EN: Adds an event to send when the change is saved.
    /// TR: Değişiklik kaydedildiğinde gönderilecek bir olay ekler.
    /// </summary>
    /// <param name="integrationEvent">EN: The event. TR: Olay.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    ValueTask AddAsync(IntegrationEvent integrationEvent);

    /// <summary>
    /// EN: Saves the service's changes and the added events in one transaction, then sends the events.
    /// TR: Servisin değişikliklerini ve eklenen olayları tek transaction'da kaydeder, ardından olayları gönderir.
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// EN: Consumes one event type. A plain class in the service; the messaging library stays out of it (ADR-023).
///     Change the service's DbContext but don't save: the dispatcher saves once, together with the "processed" mark,
///     so a failure leaves nothing half-done and a redelivery is processed again. The handler runs inside the
///     dispatcher's transaction, so direct updates (<c>ExecuteUpdateAsync</c>) commit or roll back with the rest.
/// TR: Tek bir olay tipini dinler. Serviste düz bir sınıftır; mesajlaşma kütüphanesi içine girmez (ADR-023).
///     Servisin DbContext'ini değiştirin ama kaydetmeyin: dağıtıcı "işlendi" işaretiyle birlikte bir kez kaydeder; böylece
///     bir hata yarım iş bırakmaz ve tekrar teslim yeniden işlenir. Handler dağıtıcının transaction'ı içinde çalışır; böylece
///     doğrudan güncellemeler (<c>ExecuteUpdateAsync</c>) geri kalanla birlikte kaydedilir ya da geri alınır.
/// </summary>
/// <typeparam name="TEvent">EN: The event type. TR: Olay tipi.</typeparam>
public interface IEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    /// <summary>
    /// EN: Handles the event; runs as the event tenant's system actor.
    /// TR: Olayı işler; olayın firmasının sistem kullanıcısı olarak çalışır.
    /// </summary>
    /// <param name="integrationEvent">EN: The event. TR: Olay.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// EN: Marks an event id as processed by this service. Kept in the service's database and written in the same
///     transaction as the handler's changes (idempotent consumers, ADR-023).
/// TR: Bir olay kimliğini bu servis tarafından işlenmiş olarak işaretler. Servisin veritabanında tutulur ve handler'ın
///     değişiklikleriyle aynı transaction'da yazılır (idempotent dinleyiciler, ADR-023).
/// </summary>
public sealed class ProcessedEvent
{
    /// <summary>EN: Maximum event type length. TR: En fazla olay tipi uzunluğu.</summary>
    public const int EventTypeMaxLength = 200;

    /// <summary>EN: The event's id (primary key). TR: Olayın kimliği (birincil anahtar).</summary>
    public Guid EventId { get; init; }

    /// <summary>EN: Event type name, for diagnosis. TR: Olay tipi adı, teşhis için.</summary>
    public required string EventType { get; init; }

    /// <summary>EN: The event's tenant. TR: Olayın firması.</summary>
    public Guid TenantId { get; init; }

    /// <summary>EN: When it was processed (UTC). TR: Ne zaman işlendiği (UTC).</summary>
    public DateTimeOffset ProcessedAt { get; init; }
}
