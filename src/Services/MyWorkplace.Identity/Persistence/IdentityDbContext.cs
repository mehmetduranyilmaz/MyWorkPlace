using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Domain;

namespace MyWorkplace.Identity.Persistence;

/// <summary>
/// EN: Database of the Identity service (identity-db). Shared conventions come from <see cref="ServiceDbContext"/>.
/// TR: Identity servisinin veritabanı (identity-db). Ortak kurallar <see cref="ServiceDbContext"/>'ten gelir.
/// </summary>
/// <param name="options">EN: Context options. TR: Context seçenekleri.</param>
/// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    /// <summary>EN: Companies. TR: Firmalar.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>EN: Users. TR: Kullanıcılar.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>EN: Token signing keys. TR: Token imzalama anahtarları.</summary>
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength);
            // EN: Stored as text ("Basic") so the database stays readable and reordering the enum can't corrupt data.
            // TR: Metin ("Basic") olarak saklanır; veritabanı okunabilir kalır ve enum sırası değişse de veri bozulmaz.
            tenant.Property(t => t.Plan).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
            user.Property(u => u.NormalizedEmail).HasMaxLength(User.EmailMaxLength);
            // EN: The real guarantee of uniqueness — also against two sign-ups racing each other. Only live users count,
            //     so the email of a removed user can be used again.
            // TR: Benzersizliğin asıl garantisi — aynı anda yarışan iki kayda karşı da. Sadece canlı kullanıcılar sayılır;
            //     böylece kaldırılmış bir kullanıcının e-postası tekrar kullanılabilir.
            user.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("is_deleted = false");
            user.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SigningKey>(key =>
        {
            key.Property(k => k.KeyId).HasMaxLength(64);
            key.Property(k => k.Algorithm).HasMaxLength(16);
            key.HasIndex(k => k.KeyId).IsUnique();
        });
    }
}
