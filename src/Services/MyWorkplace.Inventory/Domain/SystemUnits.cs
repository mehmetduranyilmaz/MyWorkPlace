namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: A unit as the catalog shows it: a system unit or one of the company's own (ADR-019).
/// TR: Kataloğun gösterdiği haliyle bir birim: sistem birimi veya firmanın kendi birimlerinden biri (ADR-019).
/// </summary>
/// <param name="Code">EN: Upper-case code, e.g. <c>KG</c>. TR: Büyük harfli kod, ör. <c>KG</c>.</param>
/// <param name="Name">EN: Display name. TR: Görünen ad.</param>
/// <param name="Precision">EN: Decimal places a quantity may have. TR: Bir miktarın alabileceği ondalık hane sayısı.</param>
/// <param name="IsSystem">EN: True for a built-in unit. TR: Yerleşik birim için true.</param>
public sealed record UnitDefinition(string Code, string Name, int Precision, bool IsSystem)
{
    /// <summary>
    /// EN: Whether a quantity fits this unit's precision: <c>PCS</c> (0) refuses 2.5, <c>KG</c> (3) accepts it. Applied to
    ///     manual movements (T-030), never to order issues, which are always applied (ADR-020).
    /// TR: Bir miktarın bu birimin hassasiyetine uyup uymadığı: <c>PCS</c> (0) 2,5'i reddeder, <c>KG</c> (3) kabul eder. Elle
    ///     yapılan hareketlere uygulanır (T-030); her zaman uygulanan sipariş çıkışlarına asla (ADR-020).
    /// </summary>
    /// <param name="quantity">EN: The quantity. TR: Miktar.</param>
    /// <returns>EN: True if it has at most <see cref="Precision"/> decimals. TR: En fazla <see cref="Precision"/> ondalığı varsa true.</returns>
    public bool Fits(decimal quantity) => decimal.Round(quantity, Precision) == quantity;
}

/// <summary>
/// EN: Units every company has, defined in code (ADR-019): nothing is seeded per company, and they can't be changed or
///     deleted. A company's own units live in the database (<see cref="UnitOfMeasure"/>).
/// TR: Her firmada bulunan, kodda tanımlı birimler (ADR-019): firma başına hiçbir şey tohumlanmaz; değiştirilemez ve silinemezler.
///     Firmanın kendi birimleri veritabanında durur (<see cref="UnitOfMeasure"/>).
/// </summary>
public static class SystemUnits
{
    /// <summary>EN: Piece. TR: Adet.</summary>
    public const string Piece = "PCS";

    /// <summary>EN: Kilogram. TR: Kilogram.</summary>
    public const string Kilogram = "KG";

    /// <summary>EN: Litre. TR: Litre.</summary>
    public const string Litre = "L";

    /// <summary>EN: Metre. TR: Metre.</summary>
    public const string Metre = "M";

    /// <summary>EN: Box. TR: Koli.</summary>
    public const string Box = "BOX";

    /// <summary>EN: Pack. TR: Paket.</summary>
    public const string Pack = "PACK";

    /// <summary>EN: The system units, in display order. TR: Görüntüleme sırasıyla sistem birimleri.</summary>
    public static readonly IReadOnlyList<UnitDefinition> All =
    [
        new(Piece, "Piece", 0, IsSystem: true),
        new(Kilogram, "Kilogram", 3, IsSystem: true),
        new(Litre, "Litre", 3, IsSystem: true),
        new(Metre, "Metre", 3, IsSystem: true),
        new(Box, "Box", 0, IsSystem: true),
        new(Pack, "Pack", 0, IsSystem: true),
    ];

    /// <summary>
    /// EN: The system unit with this code, or null.
    /// TR: Bu koddaki sistem birimi veya null.
    /// </summary>
    /// <param name="code">EN: Unit code, any case. TR: Birim kodu, herhangi bir harf büyüklüğünde.</param>
    /// <returns>EN: The unit or null. TR: Birim veya null.</returns>
    public static UnitDefinition? Find(string code)
    {
        var normalized = UnitOfMeasure.NormalizeCode(code);
        return All.FirstOrDefault(u => u.Code == normalized);
    }
}
