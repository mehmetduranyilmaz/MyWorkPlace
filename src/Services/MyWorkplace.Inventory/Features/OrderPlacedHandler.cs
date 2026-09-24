using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: Decreases stock when an order is placed (T-016, ADR-020). Runs as the order's company (ADR-023), so it can only
///     see and change that company's items; a Basic company has none, so its orders change nothing.
/// TR: Bir sipariş verildiğinde stoğu düşer (T-016, ADR-020). Siparişin firması adına çalışır (ADR-023); bu yüzden sadece o firmanın
///     kalemlerini görebilir ve değiştirebilir; Basic bir firmanın kalemi yoktur, dolayısıyla siparişleri hiçbir şeyi değiştirmez.
/// </summary>
/// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
/// <param name="ledger">EN: Changes balances. TR: Bakiyeleri değiştirir.</param>
/// <param name="logger">EN: Logger. TR: Günlükçü.</param>
public sealed partial class OrderPlacedHandler(InventoryDbContext db, StockLedger ledger, ILogger<OrderPlacedHandler> logger)
    : IEventHandler<OrderPlaced>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        // EN: Lines of the same SKU are issued as one movement.
        // TR: Aynı SKU'lu satırlar tek hareket olarak çıkılır.
        var quantities = integrationEvent.Lines
            .GroupBy(line => StockItem.NormalizeSku(line.Sku))
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var skus = quantities.Keys.ToList();
        var items = await db.StockItems
            .Where(i => skus.Contains(i.NormalizedSku))
            .Select(i => new { i.Id, i.NormalizedSku })
            .ToListAsync(cancellationToken);

        foreach (var sku in skus.Except(items.Select(i => i.NormalizedSku)))
        {
            LogUnmatchedSku(integrationEvent.Number, sku);
        }

        // EN: Always in the same order (by id): two orders locking the same items can then never wait for each other
        //     in a circle (deadlock).
        // TR: Her zaman aynı sırayla (kimliğe göre): aynı kalemleri kilitleyen iki sipariş böylece birbirini asla döngüsel olarak
        //     bekleyemez (deadlock).
        foreach (var item in items.OrderBy(i => i.Id))
        {
            await ledger.IssueForOrderAsync(
                item.Id, quantities[item.NormalizedSku], integrationEvent.OrderId, integrationEvent.Number, cancellationToken);
        }
    }

    /// <summary>
    /// EN: An order line whose SKU matches no stock item — e.g. a service. Skipped; a review list follows (T-042).
    /// TR: SKU'su hiçbir stok kalemiyle eşleşmeyen bir sipariş satırı — ör. bir hizmet. Atlanır; bir inceleme listesi sonra gelir (T-042).
    /// </summary>
    /// <param name="orderNumber">EN: Order number. TR: Sipariş numarası.</param>
    /// <param name="sku">EN: The unmatched SKU. TR: Eşleşmeyen SKU.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderNumber}: no stock item with SKU {Sku}; line skipped.")]
    private partial void LogUnmatchedSku(int orderNumber, string sku);
}
