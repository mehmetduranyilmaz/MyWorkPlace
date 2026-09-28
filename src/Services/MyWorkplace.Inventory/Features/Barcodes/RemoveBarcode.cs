using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Barcodes;

/// <summary>
/// EN: <c>DELETE /inventory/items/{id}/barcodes/{code}</c> — removes one of the item's barcodes; the code is free again.
/// TR: <c>DELETE /inventory/items/{id}/barcodes/{code}</c> — kalemin barkodlarından birini siler; kod yeniden serbest kalır.
/// </summary>
public static class RemoveBarcode
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapRemoveBarcode(this IEndpointRouteBuilder group) =>
        group.MapDelete("/{id:guid}/barcodes/{code}", HandleAsync)
            .WithName("RemoveBarcode")
            .RequireAuthorization(Permissions.Inventory.Delete)
            .WithSummary("EN: Remove a barcode | TR: Barkodu sil")
            .WithDescription(
                "EN: Removes a barcode of the item (exact code, case-sensitive); 404 if the item has no such barcode. " +
                "TR: Kalemin bir barkodunu siler (birebir kod, büyük/küçük harf duyarlı); kalemde böyle bir barkod yoksa 404.");

    /// <summary>
    /// EN: Handles the request.
    /// TR: İsteği işler.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="code">EN: The code. TR: Kod.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204 or 404. TR: 204 veya 404.</returns>
    public static async Task<Results<NoContent, NotFound>> HandleAsync(
        Guid id,
        string code,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var normalized = Barcode.NormalizeCode(code);
        var barcode = await db.Barcodes.AsTracking()
            .FirstOrDefaultAsync(b => b.StockItemId == id && b.Code == normalized, cancellationToken);
        if (barcode is null)
        {
            return TypedResults.NotFound();
        }

        db.Barcodes.Remove(barcode);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
