using Microsoft.EntityFrameworkCore;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: The only code that changes a stock balance (ADR-020): one atomic <c>UPDATE … SET quantity = quantity - @q</c> plus
///     the movement that records it. The update locks the item's row until the transaction ends, so two orders issuing
///     the same item at once are applied one after the other and none is lost.
/// TR: Bir stok bakiyesini değiştiren tek kod (ADR-020): tek bir atomik <c>UPDATE … SET quantity = quantity - @q</c> ve onu kaydeden
///     hareket. Güncelleme, transaction bitene kadar kalemin satırını kilitler; böylece aynı kalemden aynı anda çıkış yapan iki
///     sipariş art arda uygulanır ve hiçbiri kaybolmaz.
/// </summary>
/// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
public sealed class StockLedger(InventoryDbContext db)
{
    /// <summary>
    /// EN: Issues stock for a placed order — always applied, even below zero (ADR-020). Must run inside a transaction
    ///     that also saves the movement; the event dispatcher provides one.
    /// TR: Verilmiş bir sipariş için stok çıkışı yapar — sıfırın altına düşse bile her zaman uygulanır (ADR-020). Hareketi de
    ///     kaydeden bir transaction içinde çalışmalıdır; olay dağıtıcısı bunu sağlar.
    /// </summary>
    /// <param name="stockItemId">EN: The item. TR: Kalem.</param>
    /// <param name="quantity">EN: Quantity in the base unit (&gt; 0). TR: Temel birimde miktar (&gt; 0).</param>
    /// <param name="orderId">EN: The order. TR: Sipariş.</param>
    /// <param name="orderNumber">EN: The order's number. TR: Siparişin numarası.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The recorded movement. TR: Kaydedilen hareket.</returns>
    public async Task<StockMovement> IssueForOrderAsync(
        Guid stockItemId,
        decimal quantity,
        Guid orderId,
        int orderNumber,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A stock change must run inside a transaction, so the balance and its movement commit together.");
        }

        // EN: Goes through the tenant and soft-delete filters like any query, so it can only touch the caller's items.
        // TR: Her sorgu gibi firma ve soft-delete filtrelerinden geçer; böylece sadece çağıranın kalemlerine dokunabilir.
        var items = db.StockItems.Where(i => i.Id == stockItemId);
        await items.ExecuteUpdateAsync(set => set.SetProperty(i => i.Quantity, i => i.Quantity - quantity), cancellationToken);
        var balance = await items.Select(i => i.Quantity).SingleAsync(cancellationToken);

        var movement = new StockMovement
        {
            StockItemId = stockItemId,
            Type = StockMovementType.Out,
            Quantity = quantity,
            BalanceAfter = balance,
            Reason = StockMovementReason.Order,
            OrderId = orderId,
            OrderNumber = orderNumber,
            CausedNegativeStock = balance < 0,
        };
        db.StockMovements.Add(movement);
        return movement;
    }
}
