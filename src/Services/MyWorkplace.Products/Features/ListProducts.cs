using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Products.Persistence;

namespace MyWorkplace.Products.Features;

/// <summary>
/// EN: <c>GET /products</c> — one page of the caller's products, optionally searched (ADR-016).
/// TR: <c>GET /products</c> — çağıranın ürünlerinden bir sayfa, isteğe bağlı aramayla (ADR-016).
/// </summary>
public static class ListProducts
{
    /// <summary>
    /// EN: Maps the endpoint on the /products group.
    /// TR: Uç noktayı /products grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /products group. TR: /products grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapListProducts(this IEndpointRouteBuilder group) =>
        group.MapGet("", HandleAsync)
            .WithName("ListProducts")
            .RequireAuthorization(Permissions.Products.Read)
            .WithSummary("EN: List products | TR: Ürünleri listele")
            .WithDescription(
                "EN: Returns one page of your products, sorted by name. page ≥ 1 (default 1), pageSize 1–100 " +
                "(default 20). search matches name or SKU, case-insensitive; % and _ are matched literally. " +
                "TR: Ürünlerinizden ada göre sıralı bir sayfa döner. page ≥ 1 (varsayılan 1), pageSize 1–100 " +
                "(varsayılan 20). search; ad veya SKU'da büyük/küçük harf duyarsız eşleşir; % ve _ harfiyen aranır.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the tenant and soft-delete filters apply to the list and to the count.
    /// TR: İsteği işler; firma ve soft-delete filtreleri hem listeye hem sayıma uygulanır.
    /// </summary>
    /// <param name="page">EN: Page and search parameters. TR: Sayfa ve arama parametreleri.</param>
    /// <param name="db">EN: Products database. TR: Products veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    public static async Task<Ok<PagedResult<ProductResponse>>> HandleAsync(
        [AsParameters] PageQuery page,
        ProductsDbContext db,
        CancellationToken cancellationToken)
    {
        var products = db.Products.AsQueryable();

        if (page.SearchText is { } text)
        {
            var pattern = SearchPattern.Contains(text);
            products = products.Where(p =>
                EF.Functions.ILike(p.Name, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(p.Sku, pattern, SearchPattern.EscapeCharacter));
        }

        var result = await products
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToPagedResultAsync(ProductResponse.Projection, page, cancellationToken);

        return TypedResults.Ok(result);
    }
}
