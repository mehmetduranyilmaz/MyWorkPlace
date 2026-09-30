using MyWorkplace.Abstractions.Events;

namespace MyWorkplace.Contracts.Events;

/// <summary>
/// EN: A customer was created (T-039). Published by Customers; Orders keeps a replica from it. <c>ChangedAt</c> orders the
///     changes of one customer, because events may arrive in any order.
/// TR: Bir müşteri oluşturuldu (T-039). Customers yayınlar; Orders buna göre bir kopya tutar. <c>ChangedAt</c> bir müşterinin
///     değişikliklerini sıralar; çünkü olaylar herhangi bir sırayla gelebilir.
/// </summary>
public sealed record CustomerCreated : IntegrationEvent
{
    /// <summary>EN: The customer. TR: Müşteri.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>EN: Its name. TR: Adı.</summary>
    public required string Name { get; init; }

    /// <summary>EN: When the change happened (UTC). TR: Değişikliğin ne zaman olduğu (UTC).</summary>
    public required DateTimeOffset ChangedAt { get; init; }
}

/// <summary>
/// EN: A customer's data changed (T-039).
/// TR: Bir müşterinin verisi değişti (T-039).
/// </summary>
public sealed record CustomerUpdated : IntegrationEvent
{
    /// <summary>EN: The customer. TR: Müşteri.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>EN: Its name now. TR: Şimdiki adı.</summary>
    public required string Name { get; init; }

    /// <summary>EN: When the change happened (UTC). TR: Değişikliğin ne zaman olduğu (UTC).</summary>
    public required DateTimeOffset ChangedAt { get; init; }
}

/// <summary>
/// EN: A customer was deleted (T-039); orders placed before keep their snapshot.
/// TR: Bir müşteri silindi (T-039); daha önce verilmiş siparişler kopyalarını tutar.
/// </summary>
public sealed record CustomerDeleted : IntegrationEvent
{
    /// <summary>EN: The customer. TR: Müşteri.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>EN: When the change happened (UTC). TR: Değişikliğin ne zaman olduğu (UTC).</summary>
    public required DateTimeOffset ChangedAt { get; init; }
}
