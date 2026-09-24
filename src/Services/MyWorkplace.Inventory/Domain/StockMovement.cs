using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: Direction of a stock movement (ADR-020).
/// TR: Bir stok hareketinin yönü (ADR-020).
/// </summary>
public enum StockMovementType
{
    /// <summary>EN: Goods came in. TR: Mal girdi.</summary>
    In,

    /// <summary>EN: Goods went out. TR: Mal çıktı.</summary>
    Out,
}

/// <summary>
/// EN: Why a movement happened. Manual movements arrive with T-030.
/// TR: Bir hareketin neden olduğu. Elle yapılan hareketler T-030 ile gelir.
/// </summary>
public enum StockMovementReason
{
    /// <summary>EN: A placed order (T-016). TR: Verilmiş bir sipariş (T-016).</summary>
    Order,
}

/// <summary>
/// EN: One line of an item's stock history — the answer to "why is the stock 12?" (ADR-020). Append-only: never changed
///     or deleted. Created only by <see cref="StockLedger"/>, together with the balance change it records.
/// TR: Bir kalemin stok geçmişinin bir satırı — "stok neden 12?" sorusunun cevabı (ADR-020). Sadece eklenir: asla değiştirilmez
///     veya silinmez. Sadece <see cref="StockLedger"/> tarafından, kaydettiği bakiye değişikliğiyle birlikte oluşturulur.
/// </summary>
public sealed class StockMovement : TenantOwnedEntity
{
    /// <summary>EN: The stock item. TR: Stok kalemi.</summary>
    public Guid StockItemId { get; init; }

    /// <summary>EN: In or out. TR: Giriş veya çıkış.</summary>
    public StockMovementType Type { get; init; }

    /// <summary>EN: Quantity in the item's base unit, always positive. TR: Kalemin temel biriminde miktar, her zaman pozitif.</summary>
    public decimal Quantity { get; init; }

    /// <summary>EN: The item's balance right after this movement. TR: Bu hareketten hemen sonra kalemin bakiyesi.</summary>
    public decimal BalanceAfter { get; init; }

    /// <summary>EN: Why it happened. TR: Neden olduğu.</summary>
    public StockMovementReason Reason { get; init; }

    /// <summary>EN: The order behind it, if any. TR: Varsa arkasındaki sipariş.</summary>
    public Guid? OrderId { get; init; }

    /// <summary>EN: That order's number. TR: O siparişin numarası.</summary>
    public int? OrderNumber { get; init; }

    /// <summary>
    /// EN: True when this movement took the balance below zero — shown for review, since an order's issue is applied
    ///     even then (ADR-020).
    /// TR: Bu hareket bakiyeyi sıfırın altına indirdiyse true — inceleme için gösterilir; çünkü bir siparişin çıkışı o durumda
    ///     bile uygulanır (ADR-020).
    /// </summary>
    public bool CausedNegativeStock { get; init; }
}
