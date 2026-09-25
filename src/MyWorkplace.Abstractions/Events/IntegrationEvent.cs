namespace MyWorkplace.Abstractions.Events;

/// <summary>
/// EN: Base of every event one service publishes for others (ADR-007, ADR-023). The product's events derive from it in
///     the product's contracts, so services depend on the contract, never on each other's code. Once published, an event
///     only gains fields — none is removed or renamed.
/// TR: Bir servisin diğerleri için yayınladığı her olayın temeli (ADR-007, ADR-023). Ürünün olayları, ürünün sözleşmelerinde bundan türer;
///     böylece servisler birbirinin koduna değil sözleşmeye bağlıdır. Yayınlanmış bir olaya sadece alan eklenir — alan silinmez, adı değişmez.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>
    /// EN: Unique id; a consumer that sees the same id twice processes it once (at-least-once delivery).
    /// TR: Benzersiz kimlik; aynı kimliği iki kez gören dinleyici onu bir kez işler (en az bir kez teslim).
    /// </summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// EN: The company the event belongs to; the consumer runs as that company's system actor.
    /// TR: Olayın ait olduğu firma; dinleyici o firmanın sistem kullanıcısı olarak çalışır.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>EN: When it happened (UTC). TR: Ne zaman olduğu (UTC).</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
