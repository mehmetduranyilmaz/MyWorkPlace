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

    /// <summary>
    /// EN: The current tenant's own units; system units live in code (<see cref="SystemUnits"/>).
    /// TR: Aktif firmanın kendi birimleri; sistem birimleri koddadır (<see cref="SystemUnits"/>).
    /// </summary>
    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();

    /// <summary>EN: Barcodes of the current tenant's items. TR: Aktif firmanın kalemlerinin barkodları.</summary>
    public DbSet<Barcode> Barcodes => Set<Barcode>();

    /// <summary>
    /// EN: What was done about each order's stock (T-040). Keyed by the order id, which is unique system-wide, so not
    ///     tenant-filtered; handlers always ask by order id.
    /// TR: Her siparişin stoğu için ne yapıldığı (T-040). Anahtar tüm sistemde benzersiz olan sipariş kimliğidir; bu yüzden firma filtresi
    ///     yoktur; handler'lar her zaman sipariş kimliğiyle sorar.
    /// </summary>
    public DbSet<OrderStock> OrderStocks => Set<OrderStock>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderStock>(order =>
        {
            order.ToTable("order_stock");
            order.HasKey(o => o.OrderId);
            order.Property(o => o.OrderId).ValueGeneratedNever();

            // EN: Stored as text so it stays readable. TR: Okunur kalsın diye metin olarak saklanır.
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Barcode>(barcode =>
        {
            barcode.Property(b => b.Code).HasMaxLength(Barcode.CodeMaxLength);
            barcode.Property(b => b.UnitCode).HasMaxLength(UnitOfMeasure.CodeMaxLength);
            barcode.HasOne<StockItem>().WithMany(i => i.Barcodes).HasForeignKey(b => b.StockItemId).OnDelete(DeleteBehavior.Restrict);

            // EN: A code is unique per tenant among live barcodes: the database settles two parallel adds (ADR-019).
            // TR: Bir kod firma içinde canlı barkodlar arasında benzersizdir: iki paralel eklemeyi veritabanı çözer (ADR-019).
            barcode.HasIndex(b => new { b.TenantId, b.Code })
                .IsUnique()
                .HasFilter("is_deleted = false");

            // EN: An item's barcodes. TR: Bir kalemin barkodları.
            barcode.HasIndex(b => new { b.TenantId, b.StockItemId });
        });

        modelBuilder.Entity<UnitOfMeasure>(unit =>
        {
            unit.ToTable("units");
            unit.Property(u => u.Code).HasMaxLength(UnitOfMeasure.CodeMaxLength);
            unit.Property(u => u.Name).HasMaxLength(UnitOfMeasure.NameMaxLength);

            // EN: Code unique per tenant among live units; the database enforces it against races too.
            // TR: Kod firma içinde canlı birimler arasında benzersiz; veritabanı bunu yarışlara karşı da uygular.
            unit.HasIndex(u => new { u.TenantId, u.Code })
                .IsUnique()
                .HasFilter("is_deleted = false");
        });

        modelBuilder.Entity<StockMovement>(movement =>
        {
            // EN: Stored as text so the history stays readable. TR: Geçmiş okunur kalsın diye metin olarak saklanır.
            movement.Property(m => m.Type).HasConversion<string>().HasMaxLength(10);
            movement.Property(m => m.Reason).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.Quantity).HasPrecision(18, 3);
            movement.Property(m => m.EnteredQuantity).HasPrecision(18, 3);
            movement.Property(m => m.UnitCode).HasMaxLength(UnitOfMeasure.CodeMaxLength);
            movement.Property(m => m.Factor).HasPrecision(18, StockItem.FactorDecimals);
            movement.Property(m => m.Note).HasMaxLength(StockMovement.NoteMaxLength);
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
            item.Property(i => i.BaseUnit).HasMaxLength(UnitOfMeasure.CodeMaxLength);

            // EN: Alternative units are part of the item (owned): saved, loaded and deleted with it (ADR-019).
            // TR: Alternatif birimler kalemin parçasıdır (owned): onunla kaydedilir, yüklenir ve silinir (ADR-019).
            item.OwnsMany(i => i.Units, unit =>
            {
                unit.ToTable("stock_item_units");
                unit.WithOwner().HasForeignKey("StockItemId");
                unit.HasKey(u => u.Id);
                unit.Property(u => u.Id).ValueGeneratedNever();
                unit.Property(u => u.UnitCode).HasMaxLength(UnitOfMeasure.CodeMaxLength);
                unit.Property(u => u.Factor).HasPrecision(18, StockItem.FactorDecimals);

                // EN: "Is this unit in use?" looks items up by unit code. TR: "Bu birim kullanımda mı?" kalemleri birim koduna göre arar.
                unit.HasIndex(u => u.UnitCode);
            });

            // EN: "Is this unit in use?" also checks base units. TR: "Bu birim kullanımda mı?" temel birimleri de kontrol eder.
            item.HasIndex(i => new { i.TenantId, i.BaseUnit });
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
