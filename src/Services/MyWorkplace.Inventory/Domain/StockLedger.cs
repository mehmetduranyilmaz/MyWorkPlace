using Microsoft.EntityFrameworkCore;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: A manual movement as entered and checked: the quantity in the entered unit and in the base unit.
/// TR: Girildiği ve kontrol edildiği haliyle elle bir hareket: girilen birimde ve temel birimde miktar.
/// </summary>
/// <param name="Type">EN: In or out. TR: Giriş veya çıkış.</param>
/// <param name="EnteredQuantity">EN: Quantity as entered. TR: Girildiği haliyle miktar.</param>
/// <param name="UnitCode">EN: Entered unit. TR: Girilen birim.</param>
/// <param name="Factor">EN: Base units in one entered unit. TR: Girilen birimin bir tanesindeki temel birim sayısı.</param>
/// <param name="BaseQuantity">EN: Quantity in the base unit (&gt; 0). TR: Temel birimde miktar (&gt; 0).</param>
/// <param name="Note">EN: Optional note. TR: İsteğe bağlı not.</param>
public sealed record ManualMovement(
    StockMovementType Type, decimal EnteredQuantity, string UnitCode, decimal Factor, decimal BaseQuantity, string? Note);

/// <summary>
/// EN: How recording a manual movement ended.
/// TR: Elle bir hareketi kaydetmenin nasıl sonuçlandığı.
/// </summary>
public enum ManualMovementOutcome
{
    /// <summary>EN: Applied and recorded. TR: Uygulandı ve kaydedildi.</summary>
    Recorded,

    /// <summary>EN: Refused under <c>Block</c>: not enough stock; nothing changed. TR: <c>Block</c> altında reddedildi: stok yetersiz; hiçbir şey değişmedi.</summary>
    NotEnoughStock,

    /// <summary>EN: The item no longer exists. TR: Kalem artık yok.</summary>
    ItemNotFound,
}

/// <summary>
/// EN: The only code that changes a stock balance (ADR-020): one atomic <c>UPDATE … SET quantity = quantity ± @q</c> plus
///     the movement that records it. The update locks the item's row until the transaction ends, so concurrent movements
///     of the same item are applied one after the other and none is lost. Under <c>Block</c> the same statement also
///     checks the balance (<c>… AND quantity &gt;= @q</c>), so parallel issues can never take it below zero together.
/// TR: Bir stok bakiyesini değiştiren tek kod (ADR-020): tek bir atomik <c>UPDATE … SET quantity = quantity ± @q</c> ve onu kaydeden
///     hareket. Güncelleme, transaction bitene kadar kalemin satırını kilitler; böylece aynı kalemin eşzamanlı hareketleri art arda
///     uygulanır ve hiçbiri kaybolmaz. <c>Block</c> altında aynı ifade bakiyeyi de kontrol eder (<c>… AND quantity &gt;= @q</c>);
///     böylece paralel çıkışlar onu birlikte asla sıfırın altına indiremez.
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

        var (_, balance, baseUnit) = await ChangeBalanceAsync(stockItemId, -quantity, onlyIfEnough: false, cancellationToken);

        return Record(new StockMovement
        {
            StockItemId = stockItemId,
            Type = StockMovementType.Out,
            EnteredQuantity = quantity,
            UnitCode = baseUnit,
            Factor = 1,
            Quantity = quantity,
            BalanceAfter = balance,
            Reason = StockMovementReason.Order,
            OrderId = orderId,
            OrderNumber = orderNumber,
            CausedNegativeStock = IsNegativeIssue(StockMovementType.Out, balance),
        });
    }

    /// <summary>
    /// EN: Records a manual movement under the company's negative stock policy (ADR-020): <c>Block</c> refuses an issue
    ///     larger than the balance, <c>Allow</c> and <c>Warn</c> apply it and flag it. Must run inside a transaction that
    ///     also saves the movement.
    /// TR: Elle bir hareketi firmanın eksi stok politikasıyla kaydeder (ADR-020): <c>Block</c> bakiyeden büyük bir çıkışı reddeder,
    ///     <c>Allow</c> ve <c>Warn</c> onu uygular ve işaretler. Hareketi de kaydeden bir transaction içinde çalışmalıdır.
    /// </summary>
    /// <param name="stockItemId">EN: The item. TR: Kalem.</param>
    /// <param name="movement">EN: The checked movement. TR: Kontrol edilmiş hareket.</param>
    /// <param name="policy">EN: The company's negative stock policy. TR: Firmanın eksi stok politikası.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The outcome and, if recorded, the movement. TR: Sonuç ve kaydedildiyse hareket.</returns>
    public async Task<(ManualMovementOutcome Outcome, StockMovement? Movement)> RecordManualAsync(
        Guid stockItemId,
        ManualMovement movement,
        NegativeStockPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(movement.BaseQuantity);

        var isOut = movement.Type == StockMovementType.Out;
        var (changed, balance, _) = await ChangeBalanceAsync(
            stockItemId,
            isOut ? -movement.BaseQuantity : movement.BaseQuantity,
            onlyIfEnough: isOut && policy == NegativeStockPolicy.Block,
            cancellationToken);

        if (!changed)
        {
            return await db.StockItems.AnyAsync(i => i.Id == stockItemId, cancellationToken)
                ? (ManualMovementOutcome.NotEnoughStock, null)
                : (ManualMovementOutcome.ItemNotFound, null);
        }

        return (ManualMovementOutcome.Recorded, Record(new StockMovement
        {
            StockItemId = stockItemId,
            Type = movement.Type,
            EnteredQuantity = movement.EnteredQuantity,
            UnitCode = movement.UnitCode,
            Factor = movement.Factor,
            Quantity = movement.BaseQuantity,
            BalanceAfter = balance,
            Reason = StockMovementReason.Manual,
            Note = movement.Note,
            CausedNegativeStock = IsNegativeIssue(movement.Type, balance),
        }));
    }

    /// <summary>
    /// EN: Changes the balance with one atomic statement and reads the result. It goes through the tenant and soft-delete
    ///     filters like any query, so it can only touch the caller's live items.
    /// TR: Bakiyeyi tek bir atomik ifadeyle değiştirir ve sonucu okur. Her sorgu gibi firma ve soft-delete filtrelerinden geçer;
    ///     böylece sadece çağıranın canlı kalemlerine dokunabilir.
    /// </summary>
    /// <param name="stockItemId">EN: The item. TR: Kalem.</param>
    /// <param name="delta">EN: Signed change in the base unit. TR: Temel birimde işaretli değişiklik.</param>
    /// <param name="onlyIfEnough">EN: Apply only if the balance stays ≥ 0 (<c>Block</c>). TR: Sadece bakiye ≥ 0 kalırsa uygula (<c>Block</c>).</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Whether it changed, the balance after and the base unit. TR: Değişip değişmediği, sonraki bakiye ve temel birim.</returns>
    private async Task<(bool Changed, decimal Balance, string BaseUnit)> ChangeBalanceAsync(
        Guid stockItemId, decimal delta, bool onlyIfEnough, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A stock change must run inside a transaction, so the balance and its movement commit together.");
        }

        var items = db.StockItems.Where(i => i.Id == stockItemId);
        var target = onlyIfEnough ? items.Where(i => i.Quantity + delta >= 0) : items;
        if (await target.ExecuteUpdateAsync(set => set.SetProperty(i => i.Quantity, i => i.Quantity + delta), cancellationToken) == 0)
        {
            return (false, 0, "");
        }

        var after = await items.Select(i => new { i.Quantity, i.BaseUnit }).SingleAsync(cancellationToken);
        return (true, after.Quantity, after.BaseUnit);
    }

    /// <summary>
    /// EN: Adds the movement that explains a balance change.
    /// TR: Bir bakiye değişikliğini açıklayan hareketi ekler.
    /// </summary>
    /// <param name="movement">EN: The movement. TR: Hareket.</param>
    /// <returns>EN: The same movement. TR: Aynı hareket.</returns>
    private StockMovement Record(StockMovement movement)
    {
        db.StockMovements.Add(movement);
        return movement;
    }

    /// <summary>
    /// EN: The review flag: an issue that ended below zero. A receipt never sets it, even while the balance is negative.
    /// TR: İnceleme işareti: sıfırın altında biten bir çıkış. Bir giriş, bakiye eksideyken bile bunu asla işaretlemez.
    /// </summary>
    /// <param name="type">EN: Movement type. TR: Hareket türü.</param>
    /// <param name="balanceAfter">EN: Balance after it. TR: Sonraki bakiye.</param>
    /// <returns>EN: True to flag. TR: İşaretlenecekse true.</returns>
    public static bool IsNegativeIssue(StockMovementType type, decimal balanceAfter) =>
        type == StockMovementType.Out && balanceAfter < 0;
}
