using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Products.Domain;

namespace MyWorkplace.Products.Features;

/// <summary>
/// EN: Product form for create and (full) update. Validated before the handler runs; failures become a 400 with field errors.
/// TR: Oluşturma ve (tam) güncelleme için ürün formu. Handler'dan önce doğrulanır; hatalar alan bazlı 400'e dönüşür.
/// </summary>
public sealed record ProductInput : IValidatableObject
{
    /// <summary>EN: Stock keeping unit, unique within the company. TR: Firma içinde benzersiz stok kodu.</summary>
    [Required]
    [MaxLength(Product.SkuMaxLength)]
    public string? Sku { get; init; }

    /// <summary>EN: Product name. TR: Ürün adı.</summary>
    [Required]
    [MaxLength(Product.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>EN: Sales price: zero or more, at most 2 decimals. TR: Satış fiyatı: sıfır veya fazlası, en fazla 2 ondalık.</summary>
    [Required]
    [Range(0, 1_000_000_000)]
    public decimal? Price { get; init; }

    /// <summary>EN: Description (optional). TR: Açıklama (isteğe bağlı).</summary>
    [MaxLength(Product.DescriptionMaxLength)]
    public string? Description { get; init; }

    /// <summary>
    /// EN: The price's decimals, checked with the entity's own rule so the two can't disagree.
    /// TR: Fiyatın ondalıkları; ikisi çelişmesin diye entity'nin kendi kuralıyla kontrol edilir.
    /// </summary>
    /// <param name="validationContext">EN: Validation context. TR: Doğrulama bağlamı.</param>
    /// <returns>EN: Errors. TR: Hatalar.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Price is { } price && !Product.IsValidPrice(price))
        {
            yield return new ValidationResult($"At most {Product.PriceDecimals} decimals.", [nameof(Price)]);
        }
    }
}

/// <summary>
/// EN: What the API returns for a product. Never the entity itself; the version travels in the ETag header.
/// TR: API'nin bir ürün için döndürdüğü. Asla entity'nin kendisi değil; sürüm ETag başlığında taşınır.
/// </summary>
/// <param name="Id">EN: Product id. TR: Ürün kimliği.</param>
/// <param name="Sku">EN: SKU. TR: SKU.</param>
/// <param name="Name">EN: Name. TR: Ad.</param>
/// <param name="Price">EN: Price. TR: Fiyat.</param>
/// <param name="Description">EN: Description. TR: Açıklama.</param>
/// <param name="CreatedAt">EN: Creation time. TR: Oluşturulma zamanı.</param>
/// <param name="UpdatedAt">EN: Last update time. TR: Son güncelleme zamanı.</param>
public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>
    /// EN: The one mapping from entity to API shape (ADR-021).
    /// TR: Entity'den API biçimine tek eşleme (ADR-021).
    /// </summary>
    public static readonly Expression<Func<Product, ProductResponse>> Projection = p =>
        new ProductResponse(p.Id, p.Sku, p.Name, p.Price, p.Description, p.CreatedAt, p.UpdatedAt);

    /// <summary>EN: <see cref="Projection"/>, compiled once. TR: Bir kez derlenmiş <see cref="Projection"/>.</summary>
    private static readonly Func<Product, ProductResponse> _map = Projection.Compile();

    /// <summary>
    /// EN: Maps an entity already in memory (after create or update).
    /// TR: Bellekteki bir entity'yi eşler (oluşturma veya güncellemeden sonra).
    /// </summary>
    /// <param name="product">EN: The product. TR: Ürün.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static ProductResponse From(Product product) => _map(product);
}

/// <summary>
/// EN: Shared answers of the product endpoints.
/// TR: Ürün uç noktalarının ortak cevapları.
/// </summary>
internal static class ProductProblems
{
    /// <summary>
    /// EN: 409 for a SKU already used by another live product of the same company.
    /// TR: Aynı firmanın başka bir canlı ürününün kullandığı SKU için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult SkuTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "SKU already used by another product.",
            detail: "Product SKUs must be unique within a company.");
}
