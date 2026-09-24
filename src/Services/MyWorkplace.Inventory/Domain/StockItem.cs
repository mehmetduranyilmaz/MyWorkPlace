using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: A stock-keeping item of a company, identified by its SKU (ADR-019). The balance is kept in the base unit and
///     changes only through stock movements (T-030, ADR-020) — never through this entity's update.
/// TR: Bir firmanın, SKU'suyla tanımlanan stok kalemi (ADR-019). Bakiye temel birimde tutulur ve sadece stok
///     hareketleriyle değişir (T-030, ADR-020) — asla bu entity'nin güncellemesiyle değil.
/// </summary>
public sealed class StockItem : Entity, ITenantOwned, IAuditable, ISoftDeletable
{
    /// <summary>EN: Max length of <see cref="Sku"/>. TR: <see cref="Sku"/> için en fazla uzunluk.</summary>
    public const int SkuMaxLength = 50;

    /// <summary>EN: Max length of <see cref="Name"/>. TR: <see cref="Name"/> için en fazla uzunluk.</summary>
    public const int NameMaxLength = 200;

    /// <summary>EN: Stock-keeping unit code, as entered. TR: Girildiği haliyle stok kodu (SKU).</summary>
    [AuditChanges]
    public string Sku { get; private set; } = "";

    /// <summary>
    /// EN: Upper-case SKU for the per-tenant uniqueness check; kept in sync by <see cref="Update"/>.
    /// TR: Firma içi benzersizlik kontrolü için büyük harfli SKU; <see cref="Update"/> ile uyumlu tutulur.
    /// </summary>
    public string NormalizedSku { get; private set; } = "";

    /// <summary>EN: Item name. TR: Kalem adı.</summary>
    [AuditChanges]
    public string Name { get; private set; } = "";

    /// <summary>EN: Unit the balance is kept in (<see cref="DefaultUnits"/>). TR: Bakiyenin tutulduğu birim (<see cref="DefaultUnits"/>).</summary>
    [AuditChanges]
    public string BaseUnit { get; private set; } = DefaultUnits.Piece;

    /// <summary>
    /// EN: Balance in the base unit. Starts at 0; only stock movements change it (ADR-020).
    /// TR: Temel birimdeki bakiye. 0'dan başlar; sadece stok hareketleri değiştirir (ADR-020).
    /// </summary>
    public decimal Quantity { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// EN: True while the item still holds stock; such an item can't be deleted.
    /// TR: Kalemde hâlâ stok varken true; böyle bir kalem silinemez.
    /// </summary>
    public bool HasStock => Quantity != 0;

    /// <summary>
    /// EN: Sets the master data (full update, ADR-016). The balance is deliberately not part of it.
    /// TR: Ana verileri atar (tam güncelleme, ADR-016). Bakiye bilerek bunun parçası değildir.
    /// </summary>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="baseUnit">EN: Base unit code. TR: Temel birim kodu.</param>
    public void Update(string sku, string name, string baseUnit)
    {
        Sku = sku.Trim();
        NormalizedSku = NormalizeSku(sku);
        Name = name.Trim();
        BaseUnit = baseUnit;
    }

    /// <summary>
    /// EN: The single SKU normalization rule, used when saving and when checking uniqueness.
    /// TR: Kaydederken ve benzersizliği kontrol ederken kullanılan tek SKU normalizasyon kuralı.
    /// </summary>
    /// <param name="sku">EN: SKU as entered. TR: Girildiği haliyle SKU.</param>
    /// <returns>EN: Trimmed upper-case SKU. TR: Kırpılmış büyük harfli SKU.</returns>
    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();
}
