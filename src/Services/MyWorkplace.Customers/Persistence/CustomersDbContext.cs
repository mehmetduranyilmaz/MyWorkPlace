using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Customers.Domain;

namespace MyWorkplace.Customers.Persistence;

/// <summary>
/// EN: Database of the Customers service (customers-db). Shared conventions come from <see cref="ServiceDbContext"/>.
/// TR: Customers servisinin veritabanı (customers-db). Ortak kurallar <see cref="ServiceDbContext"/>'ten gelir.
/// </summary>
/// <param name="options">EN: Context options. TR: Context seçenekleri.</param>
/// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    /// <summary>EN: Customers of the current tenant. TR: Aktif firmanın müşterileri.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Customer>(customer =>
        {
            customer.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength);
            customer.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength);
            customer.Property(c => c.NormalizedEmail).HasMaxLength(Customer.EmailMaxLength);
            customer.Property(c => c.Phone).HasMaxLength(Customer.PhoneMaxLength);
            customer.Property(c => c.TaxNumber).HasMaxLength(Customer.TaxNumberMaxLength);
            customer.Property(c => c.Notes).HasMaxLength(Customer.NotesMaxLength);

            // EN: Email unique per tenant, but only among live customers that have one (ADR-016): a deleted customer's
            //     email can be reused. The database enforces it, so racing requests can't break it.
            // TR: E-posta firma içinde benzersiz, ama sadece e-postası olan canlı müşteriler arasında (ADR-016): silinmiş
            //     bir müşterinin e-postası tekrar kullanılabilir. Kuralı veritabanı uygular; yarışan istekler bozamaz.
            customer.HasIndex(c => new { c.TenantId, c.NormalizedEmail })
                .IsUnique()
                .HasFilter("normalized_email IS NOT NULL AND is_deleted = false");

            // EN: Lists are filtered by tenant and sorted by name (T-028).
            // TR: Listeler firmaya göre filtrelenir ve ada göre sıralanır (T-028).
            customer.HasIndex(c => new { c.TenantId, c.Name });
        });
}
