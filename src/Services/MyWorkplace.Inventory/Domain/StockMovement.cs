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
/// EN: Why a movement happened.
/// TR: Bir hareketin neden olduğu.
/// </summary>
public enum StockMovementReason
{
    /// <summary>EN: A placed order (T-016). TR: Verilmiş bir sipariş (T-016).</summary>
    Order,

    /// <summary>EN: Recorded by hand (T-030). TR: Elle kaydedilmiş (T-030).</summary>
    Manual,
}

/// <summary>
/// EN: One line of an item's stock history — the answer to "why is the stock 12?" (ADR-020). Append-only: never changed
///     or deleted. Created only by <see cref="StockLedger"/>, together with the balance change it records.
/// TR: Bir kalemin stok geçmişinin bir satırı — "stok neden 12?" sorusunun cevabı (ADR-020). Sadece eklenir: asla değiştirilmez
///     veya silinmez. Sadece <see cref="StockLedger"/> tarafından, kaydettiği bakiye değişikliğiyle birlikte oluşturulur.
/// </summary>
public sealed class StockMovement : TenantOwnedEntity
{
    /// <summary>EN: Max length of <see cref="Note"/>. TR: <see cref="Note"/> için en fazla uzunluk.</summary>
    public const int NoteMaxLength = 500;

    /// <summary>EN: The stock item. TR: Stok kalemi.</summary>
    public Guid StockItemId { get; init; }

    /// <summary>EN: In or out. TR: Giriş veya çıkış.</summary>
    public StockMovementType Type { get; init; }

    /// <summary>EN: Quantity as entered, in <see cref="UnitCode"/>. TR: <see cref="UnitCode"/> cinsinden, girildiği haliyle miktar.</summary>
    public decimal EnteredQuantity { get; init; }

    /// <summary>EN: Unit the quantity was entered in. TR: Miktarın girildiği birim.</summary>
    public string UnitCode { get; init; } = "";

    /// <summary>EN: Base units in one entered unit (1 for the base unit). TR: Girilen birimin bir tanesindeki temel birim sayısı (temel birim için 1).</summary>
    public decimal Factor { get; init; } = 1;

    /// <summary>
    /// EN: Quantity in the item's base unit (entered quantity × factor), always positive — what the balance changed by.
    /// TR: Kalemin temel biriminde miktar (girilen miktar × katsayı), her zaman pozitif — bakiyenin değiştiği miktar.
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>EN: Optional note of a manual movement. TR: Elle yapılan bir hareketin isteğe bağlı notu.</summary>
    public string? Note { get; init; }

    /// <summary>EN: The item's balance right after this movement. TR: Bu hareketten hemen sonra kalemin bakiyesi.</summary>
    public decimal BalanceAfter { get; init; }

    /// <summary>EN: Why it happened. TR: Neden olduğu.</summary>
    public StockMovementReason Reason { get; init; }

    /// <summary>EN: The order behind it, if any. TR: Varsa arkasındaki sipariş.</summary>
    public Guid? OrderId { get; init; }

    /// <summary>EN: That order's number. TR: O siparişin numarası.</summary>
    public int? OrderNumber { get; init; }

    /// <summary>
    /// EN: True when this movement is an issue that took the balance below zero — shown for review, since order issues
    ///     and manual ones under <c>Allow</c> / <c>Warn</c> are applied even then (ADR-020). A receipt never sets it.
    /// TR: Bu hareket bakiyeyi sıfırın altına indiren bir çıkışsa true — inceleme için gösterilir; çünkü sipariş çıkışları ve
    ///     <c>Allow</c> / <c>Warn</c> altındaki elle çıkışlar o durumda bile uygulanır (ADR-020). Bir giriş bunu asla işaretlemez.
    /// </summary>
    public bool CausedNegativeStock { get; init; }
}
