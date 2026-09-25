using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Products.Persistence;

namespace MyWorkplace.Products.Features;

/// <summary>
/// EN: <c>DELETE /products/{id}</c> — soft delete: the row is kept (history, audit) but hidden everywhere.
/// TR: <c>DELETE /products/{id}</c> — soft delete: satır saklanır (geçmiş, denetim) ama her yerde gizlenir.
/// </summary>
public static class DeleteProduct
{
    /// <summary>
    /// EN: Maps the endpoint on the /products group.
    /// TR: Uç noktayı /products grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /products group. TR: /products grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapDeleteProduct(this IEndpointRouteBuilder group) =>
        group.MapDelete("/{id:guid}", HandleAsync)
            .WithName("DeleteProduct")
            .RequireAuthorization(Permissions.Products.Delete)
            .WithSummary("EN: Delete a product | TR: Ürünü sil")
            .WithDescription(
                "EN: Deletes the product. The record is kept for history but no longer appears anywhere, and its " +
                "SKU can be used again. If-Match is not required (ADR-017). " +
                "TR: Ürünü siler. Kayıt geçmiş için saklanır ama artık hiçbir yerde görünmez ve SKU'su tekrar " +
                "kullanılabilir. If-Match gerekmez (ADR-017).");

    /// <summary>
    /// EN: Handles the request; the soft-delete interceptor turns the removal into an update.
    /// TR: İsteği işler; soft-delete interceptor'ı silme işlemini güncellemeye çevirir.
    /// </summary>
    /// <param name="id">EN: Product id. TR: Ürün kimliği.</param>
    /// <param name="db">EN: Products database. TR: Products veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204 or 404. TR: 204 veya 404.</returns>
    public static async Task<Results<NoContent, NotFound>> HandleAsync(
        Guid id,
        ProductsDbContext db,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FindForUpdateAsync(id, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
