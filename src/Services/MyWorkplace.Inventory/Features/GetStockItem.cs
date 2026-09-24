using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: <c>GET /inventory/items/{id}</c> — reads one stock item of the caller's company.
/// TR: <c>GET /inventory/items/{id}</c> — çağıranın firmasının bir stok kalemini okur.
/// </summary>
public static class GetStockItem
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapGetStockItem(this IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetStockItem")
            .WithSummary("EN: Get a stock item | TR: Stok kalemini getir")
            .WithDescription(
                "EN: Returns the item with its balance and its version in ETag; send that ETag in If-Match when " +
                "updating. Other companies' items are reported as not found (404). " +
                "TR: Kalemi bakiyesiyle ve sürümünü ETag'de döner; güncellerken bu ETag'i If-Match ile gönderin. " +
                "Başka firmaların kalemleri bulunamadı (404) olarak döner.");

    /// <summary>
    /// EN: Handles the request with an untracked query that projects the row version for the ETag.
    /// TR: İsteği, ETag için satır sürümünü de yansıtan takipsiz bir sorguyla işler.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 or 404. TR: 200 veya 404.</returns>
    public static async Task<Results<Ok<StockItemResponse>, NotFound>> HandleAsync(
        Guid id,
        InventoryDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var row = await db.StockItems.SingleWithVersionAsync(id, StockItemResponse.Projection, cancellationToken);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.SetETag(row.Version);
        return TypedResults.Ok(row.Value);
    }
}
