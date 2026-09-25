using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: <c>POST /inventory/items</c> — creates a stock item with a zero balance.
/// TR: <c>POST /inventory/items</c> — bakiyesi sıfır olan bir stok kalemi oluşturur.
/// </summary>
public static class CreateStockItem
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapCreateStockItem(this IEndpointRouteBuilder group) =>
        group.MapPost("", HandleAsync)
            .WithName("CreateStockItem")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Create a stock item | TR: Stok kalemi oluştur")
            .WithDescription(
                "EN: Creates a stock item with a zero balance. The SKU must be unique within your company. Base unit and " +
                "alternative units (units: [{ unit, factor }], 1 unit = factor base units) come from the unit catalog. " +
                "Requires the Pro plan. " +
                "TR: Bakiyesi sıfır olan bir stok kalemi oluşturur. SKU firmanız içinde benzersiz olmalıdır. Temel birim ve " +
                "alternatif birimler (units: [{ unit, factor }], 1 birim = factor temel birim) birim kataloğundan gelir. " +
                "Pro plan gerektirir.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the input has already been validated.
    /// TR: İsteği işler; girdi zaten doğrulanmıştır.
    /// </summary>
    /// <param name="input">EN: Item form. TR: Kalem formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="catalog">EN: Unit catalog. TR: Birim kataloğu.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201; 400 for an unknown unit; 409 for a duplicate SKU. TR: 201; bilinmeyen birimde 400; tekrar eden SKU'da 409.</returns>
    public static async Task<Results<Created<StockItemResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        StockItemInput input,
        InventoryDbContext db,
        UnitCatalog catalog,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (await StockItemProblems.UnknownUnitsAsync(input, catalog, cancellationToken) is { } unknownUnits)
        {
            return unknownUnits;
        }

        var item = new StockItem();
        item.Update(input.Sku!, input.Name!, input.BaseUnit!, input.UnitValues());

        if (await db.StockItems.AnyAsync(i => i.NormalizedSku == item.NormalizedSku, cancellationToken))
        {
            return StockItemProblems.SkuTaken();
        }

        db.StockItems.Add(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return StockItemProblems.SkuTaken();
        }

        http.Response.SetETag(db.GetVersion(item));
        return TypedResults.Created($"/inventory/items/{item.Id}", StockItemResponse.From(item));
    }
}
