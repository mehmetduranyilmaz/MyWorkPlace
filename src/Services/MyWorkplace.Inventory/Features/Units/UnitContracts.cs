using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Features.Units;

/// <summary>
/// EN: Form for adding or (fully) updating one of the company's own units (ADR-019).
/// TR: Firmanın kendi birimlerinden birini eklemek veya (tam) güncellemek için form (ADR-019).
/// </summary>
public sealed record UnitInput
{
    /// <summary>EN: Code: letters, digits, '-' or '_'; stored upper-case. TR: Kod: harf, rakam, '-' veya '_'; büyük harfle saklanır.</summary>
    [Required]
    [MaxLength(UnitOfMeasure.CodeMaxLength)]
    [RegularExpression(UnitOfMeasure.CodePattern)]
    public string? Code { get; init; }

    /// <summary>EN: Display name. TR: Görünen ad.</summary>
    [Required]
    [MaxLength(UnitOfMeasure.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>EN: Decimal places a quantity may have, 0–3. TR: Bir miktarın alabileceği ondalık hane, 0–3.</summary>
    [Required]
    [Range(0, UnitOfMeasure.MaxPrecision)]
    public int? Precision { get; init; }
}

/// <summary>
/// EN: A unit of the catalog as the API returns it. Own units carry their version in the ETag of single reads.
/// TR: API'nin döndürdüğü haliyle katalogdaki bir birim. Kendi birimlerin sürümü tekil okumalarda ETag'de taşınır.
/// </summary>
/// <param name="Code">EN: Code. TR: Kod.</param>
/// <param name="Name">EN: Name. TR: Ad.</param>
/// <param name="Precision">EN: Decimal places. TR: Ondalık hane.</param>
/// <param name="IsSystem">EN: True for a built-in unit, which can't be changed. TR: Değiştirilemeyen yerleşik birim için true.</param>
public sealed record UnitResponse(string Code, string Name, int Precision, bool IsSystem)
{
    /// <summary>
    /// EN: The one mapping from a catalog entry to the API shape.
    /// TR: Katalog kaydından API biçimine tek eşleme.
    /// </summary>
    /// <param name="unit">EN: The unit. TR: Birim.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static UnitResponse From(UnitDefinition unit) => new(unit.Code, unit.Name, unit.Precision, unit.IsSystem);
}

/// <summary>
/// EN: Shared answers of the unit endpoints.
/// TR: Birim uç noktalarının ortak cevapları.
/// </summary>
internal static class UnitProblems
{
    /// <summary>
    /// EN: 409 for a code already used by a system unit or another unit of the company.
    /// TR: Bir sistem biriminin veya firmanın başka bir biriminin kullandığı kod için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult CodeTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Unit code already used.",
            detail: "A unit code must be unique within a company, system units included.");

    /// <summary>
    /// EN: 409 for changing or deleting a system unit.
    /// TR: Bir sistem birimini değiştirmek veya silmek için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult SystemUnit() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "System units can't be changed.",
            detail: "System units are the same for every company; add your own unit instead.");

    /// <summary>
    /// EN: 409 for changing the code or precision of, or deleting, a unit that stock items use.
    /// TR: Stok kalemlerinin kullandığı bir birimin kodunu veya hassasiyetini değiştirmek ya da onu silmek için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult InUse() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The unit is used by stock items.",
            detail: "Its code and precision can't change and it can't be deleted while stock items use it.");
}
