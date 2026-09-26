using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Movements;

/// <summary>
/// EN: <c>GET /inventory/items/{id}/movements</c> — an item's stock history, newest first: the answer to "why is the stock
///     12?" (ADR-020). Manual and order movements appear together.
/// TR: <c>GET /inventory/items/{id}/movements</c> — bir kalemin stok geçmişi, en yeni önce: "stok neden 12?" sorusunun cevabı (ADR-020).
///     Elle ve sipariş kaynaklı hareketler birlikte görünür.
/// </summary>
public static class ListMovements
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapListMovements(this IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}/movements", HandleAsync)
            .WithName("ListMovements")
            .RequireAuthorization(Permissions.Inventory.Read)
            .WithSummary("EN: List an item's stock movements | TR: Kalemin stok hareketlerini listele")
            .WithDescription(
                "EN: Returns one page of the item's movements, newest first, manual and order ones together. " +
                "page ≥ 1 (default 1), pageSize 1–100 (default 20). Other companies' or deleted items: 404. " +
                "TR: Kalemin hareketlerinden bir sayfa döner; en yeni önce, elle ve sipariş kaynaklı olanlar birlikte. " +
                "page ≥ 1 (varsayılan 1), pageSize 1–100 (varsayılan 20). Başka firmaların veya silinmiş kalemler: 404.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the item must be one of the caller's live items.
    /// TR: İsteği işler; kalem çağıranın canlı kalemlerinden biri olmalıdır.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="page">EN: Page parameters. TR: Sayfa parametreleri.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The page, or 404. TR: Sayfa veya 404.</returns>
    public static async Task<Results<Ok<PagedResult<MovementResponse>>, NotFound>> HandleAsync(
        Guid id,
        [AsParameters] PageQuery page,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.StockItems.AnyAsync(i => i.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var result = await db.StockMovements
            .Where(m => m.StockItemId == id)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .ToPagedResultAsync(MovementResponse.Projection, page, cancellationToken);

        return TypedResults.Ok(result);
    }
}
