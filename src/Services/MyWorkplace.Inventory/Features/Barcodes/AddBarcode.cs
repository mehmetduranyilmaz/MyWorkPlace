using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Barcodes;

/// <summary>
/// EN: <c>POST /inventory/items/{id}/barcodes</c> — adds a barcode to one of the item's units (ADR-019, T-055).
/// TR: <c>POST /inventory/items/{id}/barcodes</c> — kalemin birimlerinden birine barkod ekler (ADR-019, T-055).
/// </summary>
public static class AddBarcode
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapAddBarcode(this IEndpointRouteBuilder group) =>
        group.MapPost("/{id:guid}/barcodes", HandleAsync)
            .WithName("AddBarcode")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Add a barcode | TR: Barkod ekle")
            .WithDescription(
                "EN: Adds a barcode to the item's base unit or one of its alternative units (400 otherwise). The code " +
                "(letters, digits, '-', '.') is kept as entered, case-sensitive, and must be unique within your company " +
                "(409). Location points to the barcode lookup. " +
                "TR: Kalemin temel birimine veya alternatif birimlerinden birine barkod ekler (değilse 400). Kod (harf, rakam, " +
                "'-', '.') girildiği gibi, büyük/küçük harf duyarlı saklanır ve firmanız içinde benzersiz olmalıdır (409). " +
                "Location barkod sorgusunu gösterir.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: item, unit, uniqueness (checked, and enforced by the database index), save.
    /// TR: İsteği işler: kalem, birim, benzersizlik (kontrol edilir ve veritabanı index'iyle uygulanır), kaydetme.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="input">EN: Barcode form. TR: Barkod formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201, 400, 404 or 409. TR: 201, 400, 404 veya 409.</returns>
    public static async Task<Results<Created<ItemBarcodeResponse>, NotFound, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        BarcodeInput input,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await db.StockItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var unitCode = UnitOfMeasure.NormalizeCode(input.Unit!);
        if (item.FactorOf(unitCode) is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(BarcodeInput.Unit)] = ["The item has no such unit: use its base unit or one of its alternative units."],
            });
        }

        var barcode = Barcode.Create(item.Id, input.Code!, unitCode);
        if (await db.Barcodes.AnyAsync(b => b.Code == barcode.Code, cancellationToken))
        {
            return BarcodeProblems.CodeTaken();
        }

        db.Barcodes.Add(barcode);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // EN: A parallel add of the same code won between the check and the save. TR: Aynı kodun paralel bir eklemesi kontrol ile kaydetme arasında kazandı.
            return BarcodeProblems.CodeTaken();
        }

        return TypedResults.Created(
            $"/inventory/barcodes/{Uri.EscapeDataString(barcode.Code)}", new ItemBarcodeResponse(barcode.Code, barcode.UnitCode));
    }
}
