using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Products.Persistence;

namespace MyWorkplace.Products.Features;

/// <summary>
/// EN: <c>PUT /products/{id}</c> — full update, protected against lost updates with If-Match (ADR-017).
/// TR: <c>PUT /products/{id}</c> — tam güncelleme; If-Match ile kayıp güncellemelere karşı korunur (ADR-017).
/// </summary>
public static class UpdateProduct
{
    /// <summary>
    /// EN: Maps the endpoint on the /products group.
    /// TR: Uç noktayı /products grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /products group. TR: /products grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapUpdateProduct(this IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}", HandleAsync)
            .WithName("UpdateProduct")
            .RequireAuthorization(Permissions.Products.Write)
            .WithSummary("EN: Update a product | TR: Ürünü güncelle")
            .WithDescription(
                "EN: Replaces all fields. Requires If-Match with the ETag you read: 428 without it, 412 if someone " +
                "changed the product in the meantime (reload and retry). Returns the new ETag. " +
                "TR: Tüm alanları değiştirir. Okuduğunuz ETag ile If-Match gerekir: yoksa 428, siz düzenlerken biri " +
                "ürünü değiştirdiyse 412 (yeniden yükleyip tekrar deneyin). Yeni ETag'i döner.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: precondition, lookup, version check, uniqueness, save.
    /// TR: İsteği işler: ön koşul, arama, sürüm kontrolü, benzersizlik, kaydetme.
    /// </summary>
    /// <param name="id">EN: Product id. TR: Ürün kimliği.</param>
    /// <param name="input">EN: Product form. TR: Ürün formu.</param>
    /// <param name="db">EN: Products database. TR: Products veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404, 409, 412 or 428. TR: 200, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<ProductResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        ProductInput input,
        ProductsDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        var product = await db.Products.FindForUpdateAsync(id, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        // EN: Cheap early answer; ExpectVersion below also covers a change that lands between this check and the save.
        // TR: Ucuz erken cevap; aşağıdaki ExpectVersion, bu kontrol ile kaydetme arasına düşen bir değişikliği de kapsar.
        if (db.GetVersion(product) != expectedVersion)
        {
            return ETags.PreconditionFailed();
        }

        db.ExpectVersion(product, expectedVersion);
        product.Update(input.Sku!, input.Name!, input.Price!.Value, input.Description);

        if (await db.Products.AnyAsync(
                p => p.NormalizedSku == product.NormalizedSku && p.Id != product.Id, cancellationToken))
        {
            return ProductProblems.SkuTaken();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ETags.PreconditionFailed();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return ProductProblems.SkuTaken();
        }

        http.Response.SetETag(db.GetVersion(product));
        return TypedResults.Ok(ProductResponse.From(product));
    }
}
