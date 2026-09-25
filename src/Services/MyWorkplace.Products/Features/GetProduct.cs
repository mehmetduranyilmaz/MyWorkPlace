using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Products.Persistence;

namespace MyWorkplace.Products.Features;

/// <summary>
/// EN: <c>GET /products/{id}</c> — reads one product of the caller's company.
/// TR: <c>GET /products/{id}</c> — çağıranın firmasının bir ürünsini okur.
/// </summary>
public static class GetProduct
{
    /// <summary>
    /// EN: Maps the endpoint on the /products group.
    /// TR: Uç noktayı /products grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /products group. TR: /products grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapGetProduct(this IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetProduct")
            .RequireAuthorization(Permissions.Products.Read)
            .WithSummary("EN: Get a product | TR: Ürünü getir")
            .WithDescription(
                "EN: Returns the product and its version in ETag; send that ETag in If-Match when updating. " +
                "Products of other companies are reported as not found (404). " +
                "TR: Ürünü ve sürümünü ETag'de döner; güncellerken bu ETag'i If-Match ile gönderin. " +
                "Başka firmaların ürünleri bulunamadı (404) olarak döner.");

    /// <summary>
    /// EN: Handles the request with an untracked query that projects the row version for the ETag.
    /// TR: İsteği, ETag için satır sürümünü de yansıtan takipsiz bir sorguyla işler.
    /// </summary>
    /// <param name="id">EN: Product id. TR: Ürün kimliği.</param>
    /// <param name="db">EN: Products database. TR: Products veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 or 404. TR: 200 veya 404.</returns>
    public static async Task<Results<Ok<ProductResponse>, NotFound>> HandleAsync(
        Guid id,
        ProductsDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // EN: The tenant and soft-delete filters apply, so another company's or a deleted product is simply not found.
        // TR: Firma ve soft-delete filtreleri uygulanır; başka firmanın veya silinmiş bir ürün kısaca bulunamaz.
        var row = await db.Products.SingleWithVersionAsync(id, ProductResponse.Projection, cancellationToken);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.SetETag(row.Version);
        return TypedResults.Ok(row.Value);
    }
}
