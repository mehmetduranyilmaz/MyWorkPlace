using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: Where an unmatched order line is in its review (T-042).
/// TR: Eşleşmeyen bir sipariş satırının incelemede nerede olduğu (T-042).
/// </summary>
public enum UnmatchedLineStatus
{
    /// <summary>EN: Waiting for a person. TR: Bir insanı bekliyor.</summary>
    Open,

    /// <summary>EN: Issued from a chosen item. TR: Seçilen bir kalemden çıkıldı.</summary>
    Resolved,

    /// <summary>EN: Left as it is (e.g. a service). TR: Olduğu gibi bırakıldı (ör. bir hizmet).</summary>
    Dismissed,

    /// <summary>EN: Its order was cancelled. TR: Siparişi iptal edildi.</summary>
    OrderCancelled,
}

/// <summary>
/// EN: An order line whose SKU matched no stock item (ADR-020, T-042) — one per order and SKU. Instead of silently never
///     leaving stock, it waits for a person: resolve it from an item, or dismiss it. Closed entries stay closed, so a
///     quantity is never issued twice.
/// TR: SKU'su hiçbir stok kalemiyle eşleşmeyen bir sipariş satırı (ADR-020, T-042) — sipariş ve SKU başına bir tane. Sessizce stoktan hiç
///     çıkmamak yerine bir insanı bekler: bir kalemden çözülür veya yok sayılır. Kapanan kayıtlar kapalı kalır; böylece bir miktar asla iki kez
///     çıkılmaz.
/// </summary>
public sealed class UnmatchedOrderLine : TenantOwnedEntity
{
    /// <summary>EN: Max length of <see cref="Note"/>. TR: <see cref="Note"/> için en fazla uzunluk.</summary>
    public const int NoteMaxLength = 500;

    /// <summary>EN: The order. TR: Sipariş.</summary>
    public Guid OrderId { get; init; }

    /// <summary>EN: The order's number. TR: Siparişin numarası.</summary>
    public int OrderNumber { get; init; }

    /// <summary>EN: SKU as ordered. TR: Sipariş edildiği haliyle SKU.</summary>
    public string Sku { get; init; } = "";

    /// <summary>EN: Upper-case SKU, as items are matched. TR: Kalemlerin eşlendiği gibi büyük harfli SKU.</summary>
    public string NormalizedSku { get; init; } = "";

    /// <summary>EN: Item name as ordered. TR: Sipariş edildiği haliyle kalem adı.</summary>
    public string Name { get; init; } = "";

    /// <summary>EN: Quantity (in the base unit, as order issues are). TR: Miktar (sipariş çıkışları gibi temel birimde).</summary>
    public decimal Quantity { get; init; }

    /// <summary>EN: Review state. TR: İnceleme durumu.</summary>
    public UnmatchedLineStatus Status { get; private set; } = UnmatchedLineStatus.Open;

    /// <summary>EN: The item it was resolved from. TR: Çözüldüğü kalem.</summary>
    public Guid? ResolvedStockItemId { get; private set; }

    /// <summary>EN: Why it was dismissed. TR: Neden yok sayıldığı.</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// EN: Marks it resolved from an item; the caller issues the stock in the same transaction.
    /// TR: Onu bir kalemden çözülmüş işaretler; çağıran stoğu aynı transaction'da çıkar.
    /// </summary>
    /// <param name="stockItemId">EN: The chosen item. TR: Seçilen kalem.</param>
    public void Resolve(Guid stockItemId)
    {
        EnsureOpen();
        Status = UnmatchedLineStatus.Resolved;
        ResolvedStockItemId = stockItemId;
    }

    /// <summary>
    /// EN: Marks it dismissed; the stock stays as it is.
    /// TR: Onu yok sayılmış işaretler; stok olduğu gibi kalır.
    /// </summary>
    /// <param name="note">EN: Optional note. TR: İsteğe bağlı not.</param>
    public void Dismiss(string? note)
    {
        EnsureOpen();
        Status = UnmatchedLineStatus.Dismissed;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    /// <summary>
    /// EN: Guards both actions: only an open entry can be settled.
    /// TR: İki işlemi de korur: sadece açık bir kayıt çözülebilir.
    /// </summary>
    private void EnsureOpen()
    {
        if (Status != UnmatchedLineStatus.Open)
        {
            throw new InvalidOperationException("Only an open entry can be resolved or dismissed.");
        }
    }
}

/// <summary>
/// EN: A SKU the company never keeps in stock (e.g. <c>SHIPPING</c>): its order lines are not listed as unmatched (T-042).
///     A setting, not history, so removing one really deletes it.
/// TR: Firmanın hiç stokta tutmadığı bir SKU (ör. <c>SHIPPING</c>): sipariş satırları eşleşmeyen olarak listelenmez (T-042). Geçmiş değil bir
///     ayardır; bu yüzden birini silmek onu gerçekten siler.
/// </summary>
public sealed class IgnoredSku : TenantOwnedEntity
{
    /// <summary>EN: SKU as first entered. TR: İlk girildiği haliyle SKU.</summary>
    public string Sku { get; init; } = "";

    /// <summary>EN: Upper-case SKU, unique within the company. TR: Firma içinde benzersiz, büyük harfli SKU.</summary>
    public string NormalizedSku { get; init; } = "";
}
