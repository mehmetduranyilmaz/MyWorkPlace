using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: <c>GET /inventory/items</c> — one page of the caller's stock items, optionally searched (ADR-016).
/// TR: <c>GET /inventory/items</c> — çağıranın stok kalemlerinden bir sayfa, isteğe bağlı aramayla (ADR-016).
/// </summary>
public static class ListStockItems
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapListStockItems(this IEndpointRouteBuilder group) =>
        group.MapGet("", HandleAsync)
            .WithName("ListStockItems")
            .WithSummary("EN: List stock items | TR: Stok kalemlerini listele")
            .WithDescription(
                "EN: Returns one page of your stock items, sorted by SKU. search matches SKU or name, case-insensitive. " +
                "page ≥ 1 (default 1), pageSize 1–100 (default 20). " +
                "TR: Stok kalemlerinizden SKU'ya göre sıralı bir sayfa döner. search; SKU veya adda büyük/küçük harf " +
                "duyarsız eşleşir. page ≥ 1 (varsayılan 1), pageSize 1–100 (varsayılan 20).")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; tenant and soft-delete filters apply to the list and the count.
    /// TR: İsteği işler; firma ve soft-delete filtreleri listeye ve sayıma uygulanır.
    /// </summary>
    /// <param name="page">EN: Page and search parameters. TR: Sayfa ve arama parametreleri.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    public static async Task<Ok<PagedResult<StockItemResponse>>> HandleAsync(
        [AsParameters] PageQuery page,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var items = db.StockItems.AsQueryable();

        if (page.SearchText is { } text)
        {
            var pattern = SearchPattern.Contains(text);
            items = items.Where(i =>
                EF.Functions.ILike(i.Sku, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(i.Name, pattern, SearchPattern.EscapeCharacter));
        }

        var result = await items
            .OrderBy(i => i.Sku)
            .ThenBy(i => i.Id)
            .ToPagedResultAsync(StockItemResponse.Projection, page, cancellationToken);

        return TypedResults.Ok(result);
    }
}
