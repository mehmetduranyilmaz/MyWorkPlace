using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: Returns the stock of a cancelled order (T-040, ADR-024): exactly what Inventory issued for it — its own movements,
///     not the order's lines, which may no longer match. If the cancellation comes before the order was issued, the order
///     is remembered as cancelled and the late <c>OrderPlaced</c> issues nothing.
/// TR: İptal edilen bir siparişin stoğunu geri verir (T-040, ADR-024): Inventory'nin onun için çıkardığını birebir — siparişin artık
///     eşleşmeyebilecek satırlarını değil, kendi hareketlerini. İptal, sipariş çıkılmadan önce gelirse sipariş iptal edilmiş olarak hatırlanır
///     ve geç gelen <c>OrderPlaced</c> hiçbir şey çıkmaz.
/// </summary>
/// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
/// <param name="claims">EN: Order stock claims. TR: Sipariş stok talepleri.</param>
/// <param name="ledger">EN: Changes balances. TR: Bakiyeleri değiştirir.</param>
/// <param name="logger">EN: Logger. TR: Günlükçü.</param>
public sealed partial class OrderCancelledHandler(
    InventoryDbContext db, OrderStockClaims claims, StockLedger ledger, ILogger<OrderCancelledHandler> logger)
    : IEventHandler<OrderCancelled>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        // EN: Its lines still waiting for review are closed: nothing to issue for a cancelled order (T-042). This changes
        //     their row version, so a resolve running at the same moment fails instead of issuing.
        // TR: İncelemeyi hâlâ bekleyen satırları kapatılır: iptal edilmiş bir sipariş için çıkılacak bir şey yok (T-042). Bu, satır sürümlerini
        //     değiştirir; böylece aynı anda çalışan bir çözme çıkış yapmak yerine başarısız olur.
        await db.UnmatchedOrderLines
            .Where(l => l.OrderId == integrationEvent.OrderId && l.Status == UnmatchedLineStatus.Open)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.Status, UnmatchedLineStatus.OrderCancelled), cancellationToken);

        if (await claims.TryClaimAsync(
            integrationEvent.OrderId, integrationEvent.TenantId, OrderStockStatus.Cancelled, cancellationToken))
        {
            // EN: Nothing was issued yet: remembered, so the late OrderPlaced issues nothing.
            // TR: Henüz hiçbir şey çıkılmadı: hatırlanır; böylece geç gelen OrderPlaced hiçbir şey çıkmaz.
            LogCancelledBeforeIssued(integrationEvent.Number);
            return;
        }

        if (!await claims.TryCancelIssuedAsync(integrationEvent.OrderId, cancellationToken))
        {
            return; // EN: Already cancelled. TR: Zaten iptal edilmiş.
        }

        // EN: Same order (by item id) as issuing, so a cancellation and an order can't deadlock.
        // TR: Çıkıştaki sırayla aynı (kalem kimliğine göre); böylece bir iptal ile bir sipariş birbirini kilitleyemez.
        var issues = await db.StockMovements
            .Where(m => m.OrderId == integrationEvent.OrderId && m.Reason == StockMovementReason.Order)
            .OrderBy(m => m.StockItemId)
            .ToListAsync(cancellationToken);

        foreach (var issue in issues)
        {
            if (await ledger.ReturnForOrderAsync(issue, cancellationToken) is null)
            {
                LogItemGone(integrationEvent.Number, issue.StockItemId);
            }
        }
    }

    /// <summary>
    /// EN: The cancellation came first; the order's stock will not be issued.
    /// TR: İptal önce geldi; siparişin stoğu çıkılmayacak.
    /// </summary>
    /// <param name="orderNumber">EN: Order number. TR: Sipariş numarası.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderNumber} cancelled before its stock was issued; nothing to return.")]
    private partial void LogCancelledBeforeIssued(int orderNumber);

    /// <summary>
    /// EN: An item the order took from was deleted meanwhile; its return is skipped.
    /// TR: Siparişin aldığı bir kalem bu arada silindi; onun geri verişi atlanır.
    /// </summary>
    /// <param name="orderNumber">EN: Order number. TR: Sipariş numarası.</param>
    /// <param name="stockItemId">EN: The deleted item. TR: Silinmiş kalem.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderNumber} cancelled: stock item {StockItemId} no longer exists; its return is skipped.")]
    private partial void LogItemGone(int orderNumber, Guid stockItemId);
}
