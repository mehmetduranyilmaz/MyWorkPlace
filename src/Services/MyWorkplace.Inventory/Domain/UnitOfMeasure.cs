using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: A unit a company added to its catalog, e.g. <c>DOZEN</c> (ADR-019). Items refer to units by code, so code and
///     precision are frozen once an item uses the unit — changing them would make stored quantities wrong.
/// TR: Bir firmanın kataloğuna eklediği birim, ör. <c>DOZEN</c> (ADR-019). Kalemler birimlere koduyla bağlanır; bu yüzden bir kalem
///     birimi kullanmaya başlayınca kod ve hassasiyet donar — değiştirmek kayıtlı miktarları yanlış yapar.
/// </summary>
public sealed class UnitOfMeasure : BusinessEntity
{
    /// <summary>EN: Longest unit code. TR: En uzun birim kodu.</summary>
    public const int CodeMaxLength = 10;

    /// <summary>EN: Max length of <see cref="Name"/>. TR: <see cref="Name"/> için en fazla uzunluk.</summary>
    public const int NameMaxLength = 50;

    /// <summary>
    /// EN: Most decimals a unit may allow: balances and movements are stored with 3 decimals, so a finer unit would be
    ///     rounded silently (ADR-019).
    /// TR: Bir birimin izin verebileceği en fazla ondalık: bakiyeler ve hareketler 3 ondalıkla saklanır; daha ince bir birim
    ///     sessizce yuvarlanırdı (ADR-019).
    /// </summary>
    public const int MaxPrecision = 3;

    /// <summary>EN: Allowed characters of a code (checked on input). TR: Bir kodun izin verilen karakterleri (girdide kontrol edilir).</summary>
    public const string CodePattern = "^[A-Za-z0-9_-]+$";

    /// <summary>EN: Upper-case code, unique within the company. TR: Firma içinde benzersiz, büyük harfli kod.</summary>
    [AuditChanges]
    public string Code { get; private set; } = "";

    /// <summary>EN: Display name. TR: Görünen ad.</summary>
    [AuditChanges]
    public string Name { get; private set; } = "";

    /// <summary>EN: Decimal places a quantity may have (0–3). TR: Bir miktarın alabileceği ondalık hane (0–3).</summary>
    [AuditChanges]
    public int Precision { get; private set; }

    /// <summary>
    /// EN: Sets all values (full update, ADR-016). Whether code or precision may change is the caller's check, because
    ///     it needs the database ("is the unit in use?").
    /// TR: Tüm değerleri atar (tam güncelleme, ADR-016). Kod veya hassasiyetin değişip değişemeyeceği çağıranın kontrolüdür;
    ///     çünkü veritabanı gerekir ("birim kullanımda mı?").
    /// </summary>
    /// <param name="code">EN: Code, any case. TR: Kod, herhangi bir harf büyüklüğünde.</param>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="precision">EN: Precision, 0–3. TR: Hassasiyet, 0–3.</param>
    public void Update(string code, string name, int precision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, MaxPrecision);

        Code = NormalizeCode(code);
        Name = name.Trim();
        Precision = precision;
    }

    /// <summary>
    /// EN: This unit as a catalog entry.
    /// TR: Bu birimin katalog kaydı hali.
    /// </summary>
    /// <returns>EN: The definition. TR: Tanım.</returns>
    public UnitDefinition ToDefinition() => new(Code, Name, Precision, IsSystem: false);

    /// <summary>
    /// EN: The single code normalization rule — codes are compared and stored upper-case, so <c>box</c> is <c>BOX</c>.
    /// TR: Tek kod normalizasyon kuralı — kodlar büyük harfle karşılaştırılır ve saklanır; böylece <c>box</c>, <c>BOX</c> olur.
    /// </summary>
    /// <param name="code">EN: Code as entered. TR: Girildiği haliyle kod.</param>
    /// <returns>EN: Trimmed upper-case code. TR: Kırpılmış büyük harfli kod.</returns>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
