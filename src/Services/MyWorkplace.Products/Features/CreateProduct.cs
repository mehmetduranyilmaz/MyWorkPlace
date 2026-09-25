using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Products.Domain;
using MyWorkplace.Products.Persistence;

namespace MyWorkplace.Products.Features;

/// <summary>
/// EN: <c>POST /products</c> — creates a product for the caller's company.
/// TR: <c>POST /products</c> — çağıranın firması için bir ürün oluşturur.
/// </summary>
public static class CreateProduct
{
    /// <summary>
    /// EN: Maps the endpoint on the /products group.
    /// TR: Uç noktayı /products grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /products group. TR: /products grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapCreateProduct(this IEndpointRouteBuilder group) =>
        group.MapPost("", HandleAsync)
            .WithName("CreateProduct")
            .RequireAuthorization(Permissions.Products.Write)
            .WithSummary("EN: Create a product | TR: Ürün oluştur")
            .WithDescription(
                "EN: Creates a product for your company. Returns its address in Location and its version in ETag. " +
                "The SKU must be unique within your company (409). " +
                "TR: Firmanız için bir ürün oluşturur. Adresini Location'da, sürümünü ETag'de döner. " +
                "SKU firmanız içinde benzersiz olmalıdır (409).")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the input has already been validated.
    /// TR: İsteği işler; girdi zaten doğrulanmıştır.
    /// </summary>
    /// <param name="input">EN: Product form. TR: Ürün formu.</param>
    /// <param name="db">EN: Products database. TR: Products veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201, or 409 for a duplicate SKU. TR: 201; tekrar eden SKU'da 409.</returns>
    public static async Task<Results<Created<ProductResponse>, ProblemHttpResult>> HandleAsync(
        ProductInput input,
        ProductsDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var product = new Product();
        product.Update(input.Sku!, input.Name!, input.Price!.Value, input.Description);

        // EN: Fast, friendly answer; the filtered unique index is the real guarantee (see the catch below).
        // TR: Hızlı ve anlaşılır cevap; asıl garanti koşullu benzersiz indekstir (aşağıdaki catch'e bakın).
        if (await db.Products.AnyAsync(p => p.NormalizedSku == product.NormalizedSku, cancellationToken))
        {
            return ProductProblems.SkuTaken();
        }

        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return ProductProblems.SkuTaken();
        }

        http.Response.SetETag(db.GetVersion(product));
        return TypedResults.Created($"/products/{product.Id}", ProductResponse.From(product));
    }
}
