using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Orders.Domain;

namespace MyWorkplace.Orders.Persistence;

/// <summary>
/// EN: Database of the Orders service (orders-db). Shared conventions come from <see cref="ServiceDbContext"/>.
/// TR: Orders servisinin veritabanı (orders-db). Ortak kurallar <see cref="ServiceDbContext"/>'ten gelir.
/// </summary>
/// <param name="options">EN: Context options. TR: Context seçenekleri.</param>
/// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    /// <summary>EN: Orders of the current tenant. TR: Aktif firmanın siparişleri.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>EN: Order number counters. TR: Sipariş numarası sayaçları.</summary>
    public DbSet<OrderNumberSequence> OrderNumberSequences => Set<OrderNumberSequence>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            // EN: Stored as text ("Draft") so the database stays readable. TR: Veritabanı okunur kalsın diye metin ("Draft") olarak saklanır.
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.CustomerName).HasMaxLength(Order.CustomerNameMaxLength);
            order.Property(o => o.Total).HasPrecision(18, 2);

            // EN: Lines are part of the order (owned): saved, loaded and deleted with it, in their own table so they can
            //     be queried later (reporting, T-018).
            // TR: Satırlar siparişin parçasıdır (owned): onunla kaydedilir, yüklenir ve silinir; ileride sorgulanabilsinler
            //     diye kendi tablolarındadır (raporlama, T-018).
            order.OwnsMany(o => o.Lines, line =>
            {
                line.ToTable("order_lines");
                line.WithOwner().HasForeignKey("OrderId");
                line.HasKey(l => l.Id);
                line.Property(l => l.Id).ValueGeneratedNever();
                line.Property(l => l.Sku).HasMaxLength(OrderLine.SkuMaxLength);
                line.Property(l => l.Name).HasMaxLength(OrderLine.NameMaxLength);
                line.Property(l => l.Quantity).HasPrecision(18, 3);
                line.Property(l => l.UnitPrice).HasPrecision(18, 2);
                line.Property(l => l.LineTotal).HasPrecision(18, 2);
            });

            // EN: A number is unique within a company; drafts have none. TR: Numara firma içinde benzersizdir; taslakların numarası yoktur.
            order.HasIndex(o => new { o.TenantId, o.Number }).IsUnique().HasFilter("number IS NOT NULL");

            // EN: Lists show the newest orders first (T-028). TR: Listeler en yeni siparişleri önce gösterir (T-028).
            order.HasIndex(o => new { o.TenantId, o.CreatedAt });
        });

        modelBuilder.Entity<OrderNumberSequence>(sequence =>
        {
            sequence.ToTable("order_number_sequences");
            sequence.HasIndex(s => s.TenantId).IsUnique();
        });
    }
}
