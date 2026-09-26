using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
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
                "EN: Replaces SKU, name, base unit and alternative units; the balance is not changed. The base unit can't " +
                "change once the item has stock movements (409). Requires If-Match with the ETag you read: 428 without it, " +
                "412 if the item changed meanwhile. Returns the new ETag. " +
                "TR: SKU, ad, temel birim ve alternatif birimleri değiştirir; bakiye değişmez. Kalemin stok hareketi olduktan " +
                "sonra temel birim değişemez (409). Okuduğunuz ETag ile If-Match gerekir: yoksa 428, kalem bu arada " +
                "değiştiyse 412. Yeni ETag'i döner.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: precondition, lookup, version check, units, frozen base unit, uniqueness, save.
    /// TR: İsteği işler: ön koşul, arama, sürüm kontrolü, birimler, donmuş temel birim, benzersizlik, kaydetme.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="input">EN: Item form. TR: Kalem formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="catalog">EN: Unit catalog. TR: Birim kataloğu.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 400, 404, 409, 412 or 428. TR: 200, 400, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<StockItemResponse>, NotFound, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        StockItemInput input,
        InventoryDbContext db,
        UnitCatalog catalog,
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

        if (await StockItemProblems.UnknownUnitsAsync(input, catalog, cancellationToken) is { } unknownUnits)
        {
            return unknownUnits;
        }

        // EN: Movements are recorded in the base unit; changing it would silently re-read the balance (ADR-019). A movement
        //     arriving after this check changes the row version, so the save below fails with 412 instead.
        // TR: Hareketler temel birimde kaydedilir; onu değiştirmek bakiyeyi sessizce başka birimde okutur (ADR-019). Bu kontrolden
        //     sonra gelen bir hareket satır sürümünü değiştirir; bu yüzden aşağıdaki kaydetme 412 ile başarısız olur.
        if (UnitOfMeasure.NormalizeCode(input.BaseUnit!) != item.BaseUnit
            && await db.StockMovements.AnyAsync(m => m.StockItemId == item.Id, cancellationToken))
        {
            return StockItemProblems.BaseUnitFrozen();
        }

        db.ExpectVersion(item, expectedVersion);
        item.Update(input.Sku!, input.Name!, input.BaseUnit!, input.UnitValues());

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
