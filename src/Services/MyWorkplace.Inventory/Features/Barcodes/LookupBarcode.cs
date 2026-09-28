using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Barcodes;

/// <summary>
/// EN: <c>GET /inventory/barcodes/{code}</c> — what a scanned barcode is: item, unit, factor and stock, in one query.
/// TR: <c>GET /inventory/barcodes/{code}</c> — okutulan bir barkodun ne olduğu: kalem, birim, katsayı ve stok; tek sorguda.
/// </summary>
public static class LookupBarcode
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/barcodes group.
    /// TR: Uç noktayı /inventory/barcodes grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The barcodes group. TR: Barkodlar grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapLookupBarcode(this IEndpointRouteBuilder group) =>
        group.MapGet("/{code}", HandleAsync)
            .WithName("LookupBarcode")
            .RequireAuthorization(Permissions.Inventory.Read)
            .WithSummary("EN: Look up a barcode | TR: Barkod sorgula")
            .WithDescription(
                "EN: Returns the item a barcode belongs to, the scanned unit with its factor, the base unit and the balance. " +
                "The code is matched exactly (case-sensitive). Unknown codes, other companies' and deleted items' codes: 404. " +
                "TR: Bir barkodun ait olduğu kalemi, okutulan birimi katsayısıyla, temel birimi ve bakiyeyi döner. Kod birebir " +
                "eşlenir (büyük/küçük harf duyarlı). Bilinmeyen kodlar, başka firmaların ve silinmiş kalemlerin kodları: 404.");

    /// <summary>
    /// EN: Handles the request; tenant and soft-delete filters apply to the barcode and its item.
    /// TR: İsteği işler; firma ve soft-delete filtreleri barkoda ve kalemine uygulanır.
    /// </summary>
    /// <param name="code">EN: The scanned code. TR: Okutulan kod.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 or 404. TR: 200 veya 404.</returns>
    public static async Task<Results<Ok<BarcodeLookupResponse>, NotFound>> HandleAsync(
        string code,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var normalized = Barcode.NormalizeCode(code);
        var result = await db.Barcodes
            .Where(b => b.Code == normalized)
            .Join(db.StockItems, b => b.StockItemId, i => i.Id, (b, i) => new BarcodeLookupResponse(
                b.Code,
                i.Id,
                i.Sku,
                i.Name,
                b.UnitCode,
                b.UnitCode == i.BaseUnit ? 1m : i.Units.Where(u => u.UnitCode == b.UnitCode).Select(u => u.Factor).First(),
                i.BaseUnit,
                i.Quantity))
            .SingleOrDefaultAsync(cancellationToken);

        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }
}
