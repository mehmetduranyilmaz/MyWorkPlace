using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: A barcode of one of an item's units, unique within the company (ADR-019, T-055). A record of its own, not part of
///     the item: uniqueness is a rule across items, so the database enforces it with a unique index on (tenant, code).
///     Free text — internal codes (Code128) are case-sensitive and carry no EAN check digit — kept as entered.
/// TR: Bir kalemin birimlerinden birinin barkodu; firma içinde benzersiz (ADR-019, T-055). Kalemin parçası değil, kendi kaydı: benzersizlik
///     kalemler arası bir kuraldır; bu yüzden veritabanı onu (firma, kod) üzerindeki benzersiz bir index'le uygular. Serbest metin — iç kodlar
///     (Code128) büyük/küçük harf duyarlıdır ve EAN kontrol hanesi taşımaz — girildiği gibi saklanır.
/// </summary>
public sealed class Barcode : BusinessEntity
{
    /// <summary>EN: Longest code. TR: En uzun kod.</summary>
    public const int CodeMaxLength = 50;

    /// <summary>EN: Allowed characters (checked on input). TR: İzin verilen karakterler (girdide kontrol edilir).</summary>
    public const string CodePattern = @"^\s*[A-Za-z0-9.\-]+\s*$";

    /// <summary>EN: The item. TR: Kalem.</summary>
    public Guid StockItemId { get; private init; }

    /// <summary>EN: The code, trimmed, case kept. TR: Kod; kırpılmış, harf büyüklüğü korunmuş.</summary>
    public string Code { get; private init; } = "";

    /// <summary>EN: The item unit this barcode stands for. TR: Bu barkodun temsil ettiği kalem birimi.</summary>
    public string UnitCode { get; private init; } = "";

    /// <summary>
    /// EN: Creates a barcode; whether the unit belongs to the item and the code is free is the caller's check (database).
    /// TR: Bir barkod oluşturur; birimin kaleme ait olup olmadığı ve kodun boş olup olmadığı çağıranın kontrolüdür (veritabanı).
    /// </summary>
    /// <param name="stockItemId">EN: The item. TR: Kalem.</param>
    /// <param name="code">EN: Code as entered. TR: Girildiği haliyle kod.</param>
    /// <param name="unitCode">EN: Normalized unit code. TR: Normalleştirilmiş birim kodu.</param>
    /// <returns>EN: The barcode. TR: Barkod.</returns>
    public static Barcode Create(Guid stockItemId, string code, string unitCode) =>
        new() { StockItemId = stockItemId, Code = NormalizeCode(code), UnitCode = unitCode };

    /// <summary>
    /// EN: The single code rule: trimmed, case kept — "abc" and "ABC" are different codes.
    /// TR: Tek kod kuralı: kırpılmış, harf büyüklüğü korunmuş — "abc" ve "ABC" farklı kodlardır.
    /// </summary>
    /// <param name="code">EN: Code as entered. TR: Girildiği haliyle kod.</param>
    /// <returns>EN: The stored form. TR: Saklanan biçim.</returns>
    public static string NormalizeCode(string code) => code.Trim();
}
