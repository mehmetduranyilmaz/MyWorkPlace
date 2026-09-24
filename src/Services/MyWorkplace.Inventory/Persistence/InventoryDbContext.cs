using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Persistence;

/// <summary>
/// EN: Database of the Inventory service (inventory-db). Shared conventions come from <see cref="ServiceDbContext"/>.
/// TR: Inventory servisinin veritabanı (inventory-db). Ortak kurallar <see cref="ServiceDbContext"/>'ten gelir.
/// </summary>
/// <param name="options">EN: Context options. TR: Context seçenekleri.</param>
/// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    /// <summary>EN: Stock items of the current tenant. TR: Aktif firmanın stok kalemleri.</summary>
    public DbSet<StockItem> StockItems => Set<StockItem>();

    /// <summary>EN: Stock history of the current tenant. TR: Aktif firmanın stok geçmişi.</summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockMovement>(movement =>
        {
            // EN: Stored as text so the history stays readable. TR: Geçmiş okunur kalsın diye metin olarak saklanır.
            movement.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
            movement.Property(m => m.Reason).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.Quantity).HasPrecision(18, 3);
            movement.Property(m => m.BalanceAfter).HasPrecision(18, 3);
            movement.HasOne<StockItem>().WithMany().HasForeignKey(m => m.StockItemId).OnDelete(DeleteBehavior.Restrict);

            // EN: An item's history, newest first (T-030). TR: Bir kalemin geçmişi, en yeni önce (T-030).
            movement.HasIndex(m => new { m.TenantId, m.StockItemId, m.CreatedAt });
        });

        modelBuilder.Entity<StockItem>(item =>
        {
            item.Property(i => i.Sku).HasMaxLength(StockItem.SkuMaxLength);
            item.Property(i => i.NormalizedSku).HasMaxLength(StockItem.SkuMaxLength);
            item.Property(i => i.Name).HasMaxLength(StockItem.NameMaxLength);
            item.Property(i => i.BaseUnit).HasMaxLength(DefaultUnits.CodeMaxLength);
            // EN: decimal, not double: 0.1 + 0.2 must be exactly 0.3 in a stock balance.
            // TR: double değil decimal: bir stok bakiyesinde 0,1 + 0,2 tam olarak 0,3 olmalı.
            item.Property(i => i.Quantity).HasPrecision(18, 3);

            // EN: SKU unique per tenant among live items (ADR-016); the database enforces it against races too.
            // TR: SKU firma içinde canlı kalemler arasında benzersiz (ADR-016); veritabanı bunu yarışlara karşı da uygular.
            item.HasIndex(i => new { i.TenantId, i.NormalizedSku })
                .IsUnique()
                .HasFilter("is_deleted = false");

            // EN: Lists are filtered by tenant and sorted by SKU (ADR-016).
            // TR: Listeler firmaya göre filtrelenir ve SKU'ya göre sıralanır (ADR-016).
            item.HasIndex(i => new { i.TenantId, i.Sku });
        });
    }
}
