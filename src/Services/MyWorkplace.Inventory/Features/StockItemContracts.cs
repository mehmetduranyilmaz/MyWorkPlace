using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: Stock item form for create and (full) update. The balance is not part of it: only movements change it (ADR-020).
/// TR: Oluşturma ve (tam) güncelleme için stok kalemi formu. Bakiye bunun parçası değildir: sadece hareketler değiştirir (ADR-020).
/// </summary>
public sealed record StockItemInput : IValidatableObject
{
    /// <summary>EN: SKU, unique within the company. TR: Firma içinde benzersiz SKU.</summary>
    [Required]
    [MaxLength(StockItem.SkuMaxLength)]
    public string? Sku { get; init; }

    /// <summary>EN: Item name. TR: Kalem adı.</summary>
    [Required]
    [MaxLength(StockItem.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>EN: Base unit code from the catalog, any case. TR: Katalogdan temel birim kodu, harf büyüklüğü fark etmez.</summary>
    [Required]
    [MaxLength(UnitOfMeasure.CodeMaxLength)]
    [RegularExpression(UnitOfMeasure.CodePattern)]
    public string? BaseUnit { get; init; }

    /// <summary>
    /// EN: Alternative units (optional, at most 20). A <c>List</c>, not an array, so the validation source generator
    ///     checks its elements (T-014).
    /// TR: Alternatif birimler (isteğe bağlı, en fazla 20). Dizi değil <c>List</c>; böylece doğrulama kaynak üreteci öğelerini
    ///     kontrol eder (T-014).
    /// </summary>
    [MaxLength(StockItem.MaxUnits)]
    public List<StockItemUnitInput>? Units { get; init; }

    /// <summary>
    /// EN: Cross-field rules: an alternative unit is not the base unit and appears once; factors fit their column.
    /// TR: Alanlar arası kurallar: alternatif birim temel birim değildir ve bir kez geçer; katsayılar sütunlarına sığar.
    /// </summary>
    /// <param name="validationContext">EN: Validation context. TR: Doğrulama bağlamı.</param>
    /// <returns>EN: Errors. TR: Hatalar.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var baseUnit = BaseUnit is null ? null : UnitOfMeasure.NormalizeCode(BaseUnit);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (unit, index) in (Units ?? []).Select((unit, index) => (unit, index)))
        {
            if (unit.Unit is { } code)
            {
                var normalized = UnitOfMeasure.NormalizeCode(code);
                if (normalized == baseUnit)
                {
                    yield return new ValidationResult("The base unit can't be an alternative unit.", [$"{nameof(Units)}[{index}].{nameof(unit.Unit)}"]);
                }
                else if (!seen.Add(normalized))
                {
                    yield return new ValidationResult("Each unit appears once.", [$"{nameof(Units)}[{index}].{nameof(unit.Unit)}"]);
                }
            }

            if (unit.Factor is { } factor && !StockItem.IsValidFactor(factor))
            {
                yield return new ValidationResult(
                    $"Above 0, at most {StockItem.MaxFactor} and {StockItem.FactorDecimals} decimals.",
                    [$"{nameof(Units)}[{index}].{nameof(unit.Factor)}"]);
            }
        }
    }

    /// <summary>
    /// EN: The alternative units as domain values.
    /// TR: Domain değerleri olarak alternatif birimler.
    /// </summary>
    /// <returns>EN: Unit values. TR: Birim değerleri.</returns>
    public IReadOnlyList<StockItemUnitValues> UnitValues() =>
        [.. (Units ?? []).Select(u => new StockItemUnitValues(u.Unit!, u.Factor!.Value))];

    /// <summary>
    /// EN: Every unit code the form refers to, base unit first.
    /// TR: Formun başvurduğu tüm birim kodları, önce temel birim.
    /// </summary>
    /// <returns>EN: The codes. TR: Kodlar.</returns>
    public IEnumerable<string> UnitCodes() => [BaseUnit!, .. (Units ?? []).Select(u => u.Unit!)];
}

/// <summary>
/// EN: One alternative unit of the item form: 1 <see cref="Unit"/> = <see cref="Factor"/> base units.
/// TR: Kalem formunun bir alternatif birimi: 1 <see cref="Unit"/> = <see cref="Factor"/> temel birim.
/// </summary>
public sealed record StockItemUnitInput
{
    /// <summary>EN: Unit code from the catalog. TR: Katalogdan birim kodu.</summary>
    [Required]
    [MaxLength(UnitOfMeasure.CodeMaxLength)]
    [RegularExpression(UnitOfMeasure.CodePattern)]
    public string? Unit { get; init; }

    /// <summary>EN: Base units in one of this unit. TR: Bu birimin bir tanesindeki temel birim sayısı.</summary>
    [Required]
    public decimal? Factor { get; init; }
}

/// <summary>
/// EN: An alternative unit as the API returns it.
/// TR: API'nin döndürdüğü haliyle bir alternatif birim.
/// </summary>
/// <param name="Unit">EN: Unit code. TR: Birim kodu.</param>
/// <param name="Factor">EN: Base units in one of it. TR: Bir tanesindeki temel birim sayısı.</param>
public sealed record StockItemUnitResponse(string Unit, decimal Factor);

/// <summary>
/// EN: What the API returns for a stock item; the version travels in the ETag header.
/// TR: API'nin bir stok kalemi için döndürdüğü; sürüm ETag başlığında taşınır.
/// </summary>
/// <param name="Id">EN: Item id. TR: Kalem kimliği.</param>
/// <param name="Sku">EN: SKU. TR: SKU.</param>
/// <param name="Name">EN: Name. TR: Ad.</param>
/// <param name="BaseUnit">EN: Base unit. TR: Temel birim.</param>
/// <param name="Quantity">EN: Balance in the base unit (read-only). TR: Temel birimdeki bakiye (salt okunur).</param>
/// <param name="CreatedAt">EN: Creation time. TR: Oluşturulma zamanı.</param>
/// <param name="UpdatedAt">EN: Last update time. TR: Son güncelleme zamanı.</param>
/// <param name="Units">EN: Alternative units, by code. TR: Koda göre alternatif birimler.</param>
public sealed record StockItemResponse(
    Guid Id,
    string Sku,
    string Name,
    string BaseUnit,
    decimal Quantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<StockItemUnitResponse> Units)
{
    /// <summary>
    /// EN: The one mapping from entity to API shape (ADR-021), used by lists, single reads and <see cref="From"/>.
    /// TR: Entity'den API biçimine tek eşleme (ADR-021); listeler, tekil okumalar ve <see cref="From"/> kullanır.
    /// </summary>
    public static readonly Expression<Func<StockItem, StockItemResponse>> Projection = i =>
        new StockItemResponse(
            i.Id, i.Sku, i.Name, i.BaseUnit, i.Quantity, i.CreatedAt, i.UpdatedAt,
            i.Units.OrderBy(u => u.UnitCode).Select(u => new StockItemUnitResponse(u.UnitCode, u.Factor)).ToList());

    /// <summary>EN: <see cref="Projection"/>, compiled once. TR: Bir kez derlenmiş <see cref="Projection"/>.</summary>
    private static readonly Func<StockItem, StockItemResponse> _map = Projection.Compile();

    /// <summary>
    /// EN: Maps an entity already in memory (after create or update).
    /// TR: Bellekteki bir entity'yi eşler (oluşturma veya güncellemeden sonra).
    /// </summary>
    /// <param name="item">EN: The item. TR: Kalem.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static StockItemResponse From(StockItem item) => _map(item);
}

/// <summary>
/// EN: Shared answers of the stock item endpoints.
/// TR: Stok kalemi uç noktalarının ortak cevapları.
/// </summary>
internal static class StockItemProblems
{
    /// <summary>
    /// EN: 409 for a SKU already used by another live item of the same company.
    /// TR: Aynı firmanın başka bir canlı kaleminin kullandığı SKU için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult SkuTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "SKU already used by another stock item.",
            detail: "SKUs must be unique within a company.");

    /// <summary>
    /// EN: 409 for deleting an item that still holds stock.
    /// TR: Hâlâ stoğu olan bir kalemi silmek için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult StillHasStock() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The stock item still has stock.",
            detail: "Bring its quantity to zero with stock movements before deleting it.");

    /// <summary>
    /// EN: 409 for changing the base unit of an item that has movements: its balance would silently be read in another unit.
    /// TR: Hareketi olan bir kalemin temel birimini değiştirmek için 409: bakiyesi sessizce başka bir birimde okunurdu.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult BaseUnitFrozen() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The base unit can't change.",
            detail: "The item has stock movements recorded in its base unit; create a new item for another base unit.");

    /// <summary>
    /// EN: 400 for unit codes that are not in the company's catalog, keyed like the form's fields; null if all exist.
    /// TR: Firmanın kataloğunda olmayan birim kodları için, formun alanlarına göre anahtarlanmış 400; hepsi varsa null.
    /// </summary>
    /// <param name="input">EN: The item form. TR: Kalem formu.</param>
    /// <param name="catalog">EN: Unit catalog. TR: Birim kataloğu.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The 400 or null. TR: 400 veya null.</returns>
    public static async Task<ValidationProblem?> UnknownUnitsAsync(
        StockItemInput input, UnitCatalog catalog, CancellationToken cancellationToken)
    {
        var unknown = await catalog.FindUnknownAsync(input.UnitCodes(), cancellationToken);
        if (unknown.Count == 0)
        {
            return null;
        }

        const string message = "Unknown unit: add it to the unit catalog first.";
        var errors = new Dictionary<string, string[]>();
        if (unknown.Contains(UnitOfMeasure.NormalizeCode(input.BaseUnit!)))
        {
            errors[nameof(StockItemInput.BaseUnit)] = [message];
        }

        foreach (var (unit, index) in (input.Units ?? []).Select((unit, index) => (unit, index)))
        {
            if (unknown.Contains(UnitOfMeasure.NormalizeCode(unit.Unit!)))
            {
                errors[$"{nameof(StockItemInput.Units)}[{index}].{nameof(StockItemUnitInput.Unit)}"] = [message];
            }
        }

        return TypedResults.ValidationProblem(errors);
    }
}
