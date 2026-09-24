using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: Stock item form for create and (full) update. The balance is not part of it: only movements change it (ADR-020).
/// TR: Oluşturma ve (tam) güncelleme için stok kalemi formu. Bakiye bunun parçası değildir: sadece hareketler değiştirir (ADR-020).
/// </summary>
public sealed record StockItemInput
{
    /// <summary>EN: SKU, unique within the company. TR: Firma içinde benzersiz SKU.</summary>
    [Required]
    [MaxLength(StockItem.SkuMaxLength)]
    public string? Sku { get; init; }

    /// <summary>EN: Item name. TR: Kalem adı.</summary>
    [Required]
    [MaxLength(StockItem.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>EN: Base unit code. TR: Temel birim kodu.</summary>
    [Required]
    [AllowedValues(
        DefaultUnits.Piece, DefaultUnits.Kilogram, DefaultUnits.Litre,
        DefaultUnits.Metre, DefaultUnits.Box, DefaultUnits.Pack)]
    public string? BaseUnit { get; init; }
}

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
public sealed record StockItemResponse(
    Guid Id,
    string Sku,
    string Name,
    string BaseUnit,
    decimal Quantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>
    /// EN: Maps a stock item to its API shape.
    /// TR: Bir stok kalemini API biçimine çevirir.
    /// </summary>
    /// <param name="item">EN: The item. TR: Kalem.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static StockItemResponse From(StockItem item) =>
        new(item.Id, item.Sku, item.Name, item.BaseUnit, item.Quantity, item.CreatedAt, item.UpdatedAt);
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
}
