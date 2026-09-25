using MyWorkplace.Abstractions.Events;

namespace MyWorkplace.Contracts.Events;

/// <summary>
/// EN: An order was placed (ADR-024). Published by Orders; Inventory decreases stock from it (T-016).
/// TR: Bir sipariş verildi (ADR-024). Orders yayınlar; Inventory buna göre stok düşer (T-016).
/// </summary>
public sealed record OrderPlaced : IntegrationEvent
{
    /// <summary>EN: The order's id. TR: Siparişin kimliği.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>EN: The order's per-company number. TR: Siparişin firmaya özel numarası.</summary>
    public required int Number { get; init; }

    /// <summary>EN: What was ordered. TR: Neyin sipariş edildiği.</summary>
    public required IReadOnlyList<OrderPlacedLine> Lines { get; init; }
}

/// <summary>
/// EN: One line of a placed order; the SKU is what Inventory matches its stock items by.
/// TR: Verilmiş bir siparişin bir satırı; Inventory stok kalemlerini SKU ile eşler.
/// </summary>
/// <param name="Sku">EN: Stock keeping unit. TR: Stok kodu.</param>
/// <param name="Name">EN: Item name as ordered. TR: Sipariş edildiği haliyle kalem adı.</param>
/// <param name="Quantity">EN: Quantity. TR: Miktar.</param>
public sealed record OrderPlacedLine(string Sku, string Name, decimal Quantity);
