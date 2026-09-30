using MyWorkplace.Abstractions.Events;

namespace MyWorkplace.Contracts.Events;

/// <summary>
/// EN: A placed order was cancelled (ADR-024, T-040). Published by Orders; Inventory returns what it issued for the
///     order. It carries only the order: Inventory returns what it recorded, not what the lines say now.
/// TR: Verilmiş bir sipariş iptal edildi (ADR-024, T-040). Orders yayınlar; Inventory sipariş için çıkardığını geri verir. Sadece siparişi
///     taşır: Inventory satırların şimdi söylediğini değil, kaydettiğini geri verir.
/// </summary>
public sealed record OrderCancelled : IntegrationEvent
{
    /// <summary>EN: The order's id. TR: Siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>EN: The order's per-company number. TR: Siparişin firmaya özel numarası.</summary>
    public required int Number { get; init; }
}
