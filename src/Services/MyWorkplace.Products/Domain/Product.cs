using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Products.Domain;

/// <summary>
/// EN: A product in a company's catalog: what it sells and for how much. Tenant-owned, audited and soft-deletable
///     (ADR-011, ADR-016). Stock lives in Inventory; the two are linked later through events (T-050).
/// TR: Bir firmanın kataloğundaki ürün: ne sattığı ve kaça sattığı. Firmaya ait, denetlenen ve soft-delete edilebilen (ADR-011,
///     ADR-016). Stok Inventory'dedir; ikisi sonra olaylarla bağlanır (T-050).
/// </summary>
public sealed class Product : BusinessEntity
{
    /// <summary>EN: Max length of <see cref="Sku"/>. TR: <see cref="Sku"/> için en fazla uzunluk.</summary>
    public const int SkuMaxLength = 50;

    /// <summary>EN: Max length of <see cref="Name"/>. TR: <see cref="Name"/> için en fazla uzunluk.</summary>
    public const int NameMaxLength = 200;

    /// <summary>EN: Max length of <see cref="Description"/>. TR: <see cref="Description"/> için en fazla uzunluk.</summary>
    public const int DescriptionMaxLength = 2000;

    /// <summary>EN: Decimals a price may have (the column's scale). TR: Bir fiyatın taşıyabileceği ondalık (sütunun ölçeği).</summary>
    public const int PriceDecimals = 2;

    /// <summary>EN: Stock keeping unit as entered. TR: Girildiği haliyle stok kodu.</summary>
    [AuditChanges]
    public string Sku { get; private set; } = "";

    /// <summary>
    /// EN: Upper-case SKU for the per-tenant uniqueness check; kept in sync with <see cref="Sku"/> by <see cref="Update"/>.
    /// TR: Firma içi benzersizlik kontrolü için büyük harfli SKU; <see cref="Update"/> ile <see cref="Sku"/>'yla uyumlu tutulur.
    /// </summary>
    public string NormalizedSku { get; private set; } = "";

    /// <summary>EN: Product name. TR: Ürün adı.</summary>
    [AuditChanges]
    public string Name { get; private set; } = "";

    /// <summary>EN: Sales price; audited, a price change is worth knowing. TR: Satış fiyatı; denetlenir, fiyat değişikliği bilinmeye değer.</summary>
    [AuditChanges]
    public decimal Price { get; private set; }

    /// <summary>EN: Free-text description (optional). TR: Serbest açıklama (isteğe bağlı).</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// EN: Sets every editable field at once (full update, ADR-016) and enforces the price rules.
    /// TR: Düzenlenebilir tüm alanları bir kerede atar (tam güncelleme, ADR-016) ve fiyat kurallarını uygular.
    /// </summary>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="price">EN: Price. TR: Fiyat.</param>
    /// <param name="description">EN: Description or null. TR: Açıklama veya null.</param>
    public void Update(string sku, string name, decimal price, string? description)
    {
        if (!IsValidPrice(price))
        {
            throw new ArgumentOutOfRangeException(
                nameof(price), price, $"A price is zero or more, with at most {PriceDecimals} decimals.");
        }

        Sku = sku.Trim();
        NormalizedSku = NormalizeSku(sku);
        Name = name.Trim();
        Price = price;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>
    /// EN: The price rule in one place: not negative, and no more decimals than the column holds — more would be rounded
    ///     silently by the database (a mistake the guide lists).
    /// TR: Fiyat kuralı tek yerde: eksi değil ve sütunun tuttuğundan fazla ondalık yok — fazlası veritabanında sessizce yuvarlanırdı
    ///     (rehberin listelediği bir hata).
    /// </summary>
    /// <param name="price">EN: Price. TR: Fiyat.</param>
    /// <returns>EN: True if valid. TR: Geçerliyse true.</returns>
    public static bool IsValidPrice(decimal price) => price >= 0 && decimal.Round(price, PriceDecimals) == price;

    /// <summary>
    /// EN: The single SKU normalization rule, used when saving and when checking uniqueness.
    /// TR: Kaydederken ve benzersizliği kontrol ederken kullanılan tek SKU normalizasyon kuralı.
    /// </summary>
    /// <param name="sku">EN: SKU as entered. TR: Girildiği haliyle SKU.</param>
    /// <returns>EN: Trimmed upper-case SKU. TR: Kırpılmış büyük harfli SKU.</returns>
    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();
}
