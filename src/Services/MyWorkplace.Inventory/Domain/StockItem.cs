using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: A stock-keeping item of a company, identified by its SKU (ADR-019). The balance is kept in the base unit and
///     changes only through stock movements (T-030, ADR-020) — never through this entity's update.
/// TR: Bir firmanın, SKU'suyla tanımlanan stok kalemi (ADR-019). Bakiye temel birimde tutulur ve sadece stok
///     hareketleriyle değişir (T-030, ADR-020) — asla bu entity'nin güncellemesiyle değil.
/// </summary>
public sealed class StockItem : BusinessEntity
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

    /// <summary>EN: Most alternative units one item may have. TR: Bir kalemin en fazla alternatif birim sayısı.</summary>
    public const int MaxUnits = 20;

    /// <summary>EN: Decimals a conversion factor may have. TR: Bir çevrim katsayısının alabileceği ondalık sayısı.</summary>
    public const int FactorDecimals = 6;

    /// <summary>EN: Largest conversion factor. TR: En büyük çevrim katsayısı.</summary>
    public const decimal MaxFactor = 1_000_000m;

    /// <summary>EN: The alternative units. TR: Alternatif birimler.</summary>
    private readonly List<StockItemUnit> _units = [];

    /// <summary>
    /// EN: Unit code the balance is kept in (ADR-019). Frozen once the item has movements — checked by the endpoint,
    ///     because it needs the database.
    /// TR: Bakiyenin tutulduğu birim kodu (ADR-019). Kalemin hareketi olunca donar — veritabanı gerektiği için uç nokta kontrol eder.
    /// </summary>
    [AuditChanges]
    public string BaseUnit { get; private set; } = SystemUnits.Piece;

    /// <summary>
    /// EN: Other units the item is counted in, each with its factor to the base unit (1 <c>BOX</c> = 24 <c>PCS</c>).
    /// TR: Kalemin sayıldığı diğer birimler; her biri temel birime çevrim katsayısıyla (1 <c>BOX</c> = 24 <c>PCS</c>).
    /// </summary>
    public IReadOnlyList<StockItemUnit> Units => _units;

    /// <summary>
    /// EN: Balance in the base unit. Starts at 0; only stock movements change it (ADR-020).
    /// TR: Temel birimdeki bakiye. 0'dan başlar; sadece stok hareketleri değiştirir (ADR-020).
    /// </summary>
    public decimal Quantity { get; private set; }

    /// <summary>
    /// EN: True while the item still holds stock; such an item can't be deleted.
    /// TR: Kalemde hâlâ stok varken true; böyle bir kalem silinemez.
    /// </summary>
    public bool HasStock => Quantity != 0;

    /// <summary>
    /// EN: Sets the master data, alternative units included (full update, ADR-016). The balance is deliberately not part
    ///     of it. Unit codes are normalized; whether they exist in the catalog is the caller's check (it needs the database).
    /// TR: Alternatif birimler dahil ana verileri atar (tam güncelleme, ADR-016). Bakiye bilerek bunun parçası değildir. Birim
    ///     kodları normalleştirilir; katalogda olup olmadıkları çağıranın kontrolüdür (veritabanı gerekir).
    /// </summary>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="baseUnit">EN: Base unit code. TR: Temel birim kodu.</param>
    /// <param name="units">EN: Alternative units. TR: Alternatif birimler.</param>
    public void Update(string sku, string name, string baseUnit, IReadOnlyList<StockItemUnitValues> units)
    {
        var normalizedBase = UnitOfMeasure.NormalizeCode(baseUnit);
        var normalizedUnits = units.Select(u => u with { Unit = UnitOfMeasure.NormalizeCode(u.Unit) }).ToList();
        if (normalizedUnits.Count > MaxUnits
            || normalizedUnits.Any(u => u.Unit == normalizedBase || !IsValidFactor(u.Factor))
            || normalizedUnits.DistinctBy(u => u.Unit).Count() != normalizedUnits.Count)
        {
            throw new ArgumentException(
                $"At most {MaxUnits} distinct alternative units, none the base unit, each with a valid factor.", nameof(units));
        }

        Sku = sku.Trim();
        NormalizedSku = NormalizeSku(sku);
        Name = name.Trim();
        BaseUnit = normalizedBase;

        _units.Clear();
        _units.AddRange(normalizedUnits.Select(StockItemUnit.Create));
    }

    /// <summary>
    /// EN: How many base units one of <paramref name="unitCode"/> is for this item: 1 for the base unit, the factor for an
    ///     alternative unit, null for a unit the item doesn't have.
    /// TR: Bu kalem için bir <paramref name="unitCode"/>'un kaç temel birim olduğu: temel birim için 1, alternatif birim için katsayı,
    ///     kalemde olmayan bir birim için null.
    /// </summary>
    /// <param name="unitCode">EN: Unit code, any case. TR: Birim kodu, harf büyüklüğü fark etmez.</param>
    /// <returns>EN: The factor or null. TR: Katsayı veya null.</returns>
    public decimal? FactorOf(string unitCode)
    {
        var code = UnitOfMeasure.NormalizeCode(unitCode);
        return code == BaseUnit ? 1m : _units.FirstOrDefault(u => u.UnitCode == code)?.Factor;
    }

    /// <summary>
    /// EN: Whether a conversion factor is usable: above zero, at most <see cref="MaxFactor"/> and
    ///     <see cref="FactorDecimals"/> decimals (more would be rounded silently by the column).
    /// TR: Bir çevrim katsayısının kullanılabilir olup olmadığı: sıfırdan büyük, en fazla <see cref="MaxFactor"/> ve
    ///     <see cref="FactorDecimals"/> ondalık (fazlası sütunda sessizce yuvarlanırdı).
    /// </summary>
    /// <param name="factor">EN: The factor. TR: Katsayı.</param>
    /// <returns>EN: True if valid. TR: Geçerliyse true.</returns>
    public static bool IsValidFactor(decimal factor) =>
        factor is > 0 and <= MaxFactor && decimal.Round(factor, FactorDecimals) == factor;

    /// <summary>
    /// EN: The single SKU normalization rule, used when saving and when checking uniqueness.
    /// TR: Kaydederken ve benzersizliği kontrol ederken kullanılan tek SKU normalizasyon kuralı.
    /// </summary>
    /// <param name="sku">EN: SKU as entered. TR: Girildiği haliyle SKU.</param>
    /// <returns>EN: Trimmed upper-case SKU. TR: Kırpılmış büyük harfli SKU.</returns>
    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();
}
