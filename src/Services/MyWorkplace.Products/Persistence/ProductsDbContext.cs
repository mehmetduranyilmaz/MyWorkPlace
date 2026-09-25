using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Products.Domain;

namespace MyWorkplace.Products.Persistence;

/// <summary>
/// EN: Database of the Products service (products-db). Shared conventions come from <see cref="ServiceDbContext"/>.
/// TR: Products servisinin veritabanı (products-db). Ortak kurallar <see cref="ServiceDbContext"/>'ten gelir.
/// </summary>
/// <param name="options">EN: Context options. TR: Context seçenekleri.</param>
/// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
public sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    /// <summary>EN: Products of the current tenant. TR: Aktif firmanın ürünleri.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Product>(product =>
        {
            product.Property(p => p.Sku).HasMaxLength(Product.SkuMaxLength);
            product.Property(p => p.NormalizedSku).HasMaxLength(Product.SkuMaxLength);
            product.Property(p => p.Name).HasMaxLength(Product.NameMaxLength);
            product.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
            product.Property(p => p.Price).HasPrecision(18, Product.PriceDecimals);

            // EN: SKU unique per tenant among live products (ADR-016); the database enforces it against races too.
            // TR: SKU firma içinde canlı ürünler arasında benzersiz (ADR-016); veritabanı bunu yarışlara karşı da uygular.
            product.HasIndex(p => new { p.TenantId, p.NormalizedSku })
                .IsUnique()
                .HasFilter("is_deleted = false");

            // EN: Lists are filtered by tenant and sorted by name (ADR-016).
            // TR: Listeler firmaya göre filtrelenir ve ada göre sıralanır (ADR-016).
            product.HasIndex(p => new { p.TenantId, p.Name });
        });
}
