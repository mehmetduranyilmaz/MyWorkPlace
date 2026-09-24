using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: <c>PUT /inventory/items/{id}</c> — full update of the master data, protected with If-Match (ADR-017).
///     The balance is untouched: only movements change it.
/// TR: <c>PUT /inventory/items/{id}</c> — ana verilerin tam güncellemesi, If-Match ile korunur (ADR-017).
///     Bakiyeye dokunulmaz: sadece hareketler değiştirir.
/// </summary>
public static class UpdateStockItem
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapUpdateStockItem(this IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}", HandleAsync)
            .WithName("UpdateStockItem")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Update a stock item | TR: Stok kalemini güncelle")
            .WithDescription(
                "EN: Replaces SKU, name and base unit; the balance is not changed. Requires If-Match with the ETag you " +
                "read: 428 without it, 412 if the item changed meanwhile. Returns the new ETag. " +
                "TR: SKU, ad ve temel birimi değiştirir; bakiye değişmez. Okuduğunuz ETag ile If-Match gerekir: yoksa " +
                "428, kalem bu arada değiştiyse 412. Yeni ETag'i döner.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: precondition, lookup, version check, uniqueness, save.
    /// TR: İsteği işler: ön koşul, arama, sürüm kontrolü, benzersizlik, kaydetme.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="input">EN: Item form. TR: Kalem formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404, 409, 412 or 428. TR: 200, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<StockItemResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        StockItemInput input,
        InventoryDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        var item = await db.StockItems.FindForUpdateAsync(id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (db.GetVersion(item) != expectedVersion)
        {
            return ETags.PreconditionFailed();
        }

        db.ExpectVersion(item, expectedVersion);
        item.Update(input.Sku!, input.Name!, input.BaseUnit!);

        if (await db.StockItems.AnyAsync(i => i.NormalizedSku == item.NormalizedSku && i.Id != item.Id, cancellationToken))
        {
            return StockItemProblems.SkuTaken();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ETags.PreconditionFailed();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return StockItemProblems.SkuTaken();
        }

        http.Response.SetETag(db.GetVersion(item));
        return TypedResults.Ok(StockItemResponse.From(item));
    }
}
