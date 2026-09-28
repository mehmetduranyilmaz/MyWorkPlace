using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Features.Barcodes;

/// <summary>
/// EN: Form for adding a barcode to one of an item's units (ADR-019, T-055).
/// TR: Bir kalemin birimlerinden birine barkod eklemek için form (ADR-019, T-055).
/// </summary>
public sealed record BarcodeInput
{
    /// <summary>EN: The code: letters, digits, '-' or '.'; case kept. TR: Kod: harf, rakam, '-' veya '.'; harf büyüklüğü korunur.</summary>
    [Required]
    [MaxLength(Barcode.CodeMaxLength)]
    [RegularExpression(Barcode.CodePattern)]
    public string? Code { get; init; }

    /// <summary>EN: The item's base unit or one of its alternative units. TR: Kalemin temel birimi veya alternatif birimlerinden biri.</summary>
    [Required]
    [MaxLength(UnitOfMeasure.CodeMaxLength)]
    [RegularExpression(UnitOfMeasure.CodePattern)]
    public string? Unit { get; init; }
}

/// <summary>
/// EN: What scanning a barcode tells: the item, the scanned unit and the stock — everything a till or a count needs.
/// TR: Bir barkodu okutmanın söyledikleri: kalem, okutulan birim ve stok — bir kasanın veya sayımın ihtiyacı olan her şey.
/// </summary>
/// <param name="Code">EN: The code. TR: Kod.</param>
/// <param name="ItemId">EN: Item id. TR: Kalem kimliği.</param>
/// <param name="Sku">EN: SKU. TR: SKU.</param>
/// <param name="Name">EN: Item name. TR: Kalem adı.</param>
/// <param name="Unit">EN: The scanned unit. TR: Okutulan birim.</param>
/// <param name="Factor">EN: Base units in one scanned unit. TR: Okutulan birimin bir tanesindeki temel birim sayısı.</param>
/// <param name="BaseUnit">EN: The item's base unit. TR: Kalemin temel birimi.</param>
/// <param name="Quantity">EN: Balance in the base unit. TR: Temel birimde bakiye.</param>
public sealed record BarcodeLookupResponse(
    string Code, Guid ItemId, string Sku, string Name, string Unit, decimal Factor, string BaseUnit, decimal Quantity);

/// <summary>
/// EN: Shared answers of the barcode endpoints.
/// TR: Barkod uç noktalarının ortak cevapları.
/// </summary>
internal static class BarcodeProblems
{
    /// <summary>
    /// EN: 409 for a code another live item of the company already uses.
    /// TR: Firmanın başka bir canlı kaleminin zaten kullandığı bir kod için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult CodeTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Barcode already used.",
            detail: "A barcode must be unique within a company.");
}
