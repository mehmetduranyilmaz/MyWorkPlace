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
/// <param name="claims">EN: Order stock claims (T-040). TR: Sipariş stok talepleri (T-040).</param>
public sealed partial class OrderPlacedHandler(
    InventoryDbContext db, StockLedger ledger, OrderStockClaims claims, ILogger<OrderPlacedHandler> logger)
    : IEventHandler<OrderPlaced>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        // EN: Claim the order first: if its cancellation was processed already, nothing is issued (T-040).
        // TR: Önce sipariş talep edilir: iptali zaten işlendiyse hiçbir şey çıkılmaz (T-040).
        if (!await claims.TryClaimAsync(
            integrationEvent.OrderId, integrationEvent.TenantId, OrderStockStatus.Issued, cancellationToken))
        {
            LogAlreadyCancelled(integrationEvent.Number);
            return;
        }

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

        // EN: A line matching no item waits for a person, unless the company never stocks that SKU (T-042).
        // TR: Hiçbir kalemle eşleşmeyen bir satır bir insanı bekler; firma o SKU'yu hiç stokta tutmuyorsa beklemez (T-042).
        var unmatched = skus.Except(items.Select(i => i.NormalizedSku)).ToList();
        var ignored = await db.IgnoredSkus
            .Where(s => unmatched.Contains(s.NormalizedSku))
            .Select(s => s.NormalizedSku)
            .ToListAsync(cancellationToken);
        foreach (var sku in unmatched.Except(ignored))
        {
            LogUnmatchedSku(integrationEvent.Number, sku);
            var first = integrationEvent.Lines.First(line => StockItem.NormalizeSku(line.Sku) == sku);
            db.UnmatchedOrderLines.Add(new UnmatchedOrderLine
            {
                TenantId = integrationEvent.TenantId,
                OrderId = integrationEvent.OrderId,
                OrderNumber = integrationEvent.Number,
                Sku = first.Sku.Trim(),
                NormalizedSku = sku,
                Name = first.Name.Trim(),
                Quantity = quantities[sku],
            });
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
    /// EN: An order line whose SKU matches no stock item; it is listed for review (T-042).
    /// TR: SKU'su hiçbir stok kalemiyle eşleşmeyen bir sipariş satırı; inceleme için listelenir (T-042).
    /// </summary>
    /// <param name="orderNumber">EN: Order number. TR: Sipariş numarası.</param>
    /// <param name="sku">EN: The unmatched SKU. TR: Eşleşmeyen SKU.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderNumber}: no stock item with SKU {Sku}; listed for review.")]
    private partial void LogUnmatchedSku(int orderNumber, string sku);

    /// <summary>
    /// EN: The order's cancellation arrived first (events may come in any order); nothing is issued.
    /// TR: Siparişin iptali önce geldi (olaylar herhangi bir sırayla gelebilir); hiçbir şey çıkılmaz.
    /// </summary>
    /// <param name="orderNumber">EN: Order number. TR: Sipariş numarası.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderNumber} was already cancelled; its stock is not issued.")]
    private partial void LogAlreadyCancelled(int orderNumber);
}
