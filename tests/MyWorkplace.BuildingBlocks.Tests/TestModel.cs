using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Domain;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Test entity implementing every building-block interface; only <see cref="Title"/> is audited.
/// TR: Tüm building-block arayüzlerini uygulayan test entity'si; sadece <see cref="Title"/> denetlenir.
/// </summary>
public sealed class TestNote : Entity, ITenantOwned, IAuditable, ISoftDeletable
{
    /// <summary>EN: Audited property. TR: Denetlenen alan.</summary>
    [AuditChanges]
    public string Title { get; set; } = "";

    /// <summary>EN: Unaudited property. TR: Denetlenmeyen alan.</summary>
    public string Body { get; set; } = "";

    /// <summary>EN: Audited collection (like a user's roles). TR: Denetlenen koleksiyon (bir kullanıcının rolleri gibi).</summary>
    [AuditChanges]
    public string[] Tags { get; set; } = [];

    /// <summary>EN: Owned parts (like an order's lines). TR: Sahip olunan parçalar (bir siparişin satırları gibi).</summary>
    public List<TestNoteItem> Items { get; set; } = [];

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// EN: An owned part of a <see cref="TestNote"/>.
/// TR: Bir <see cref="TestNote"/>'un sahip olunan parçası.
/// </summary>
public sealed class TestNoteItem
{
    /// <summary>EN: Part id. TR: Parça kimliği.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>EN: Text. TR: Metin.</summary>
    public string Text { get; set; } = "";
}

/// <summary>
/// EN: Minimal service context used to exercise the shared conventions.
/// TR: Ortak kuralları denemek için kullanılan en küçük servis context'i.
/// </summary>
/// <param name="options">EN: Context options. TR: Context seçenekleri.</param>
/// <param name="currentUser">EN: Acting user. TR: İşlemi yapan kullanıcı.</param>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    /// <summary>EN: Test notes. TR: Test notları.</summary>
    public DbSet<TestNote> Notes => Set<TestNote>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<TestNote>(note =>
        {
            note.Property(n => n.Title).HasMaxLength(200);
            note.Property(n => n.Body).HasMaxLength(2000);
            note.OwnsMany(n => n.Items, item =>
            {
                item.ToTable("test_note_items");
                item.HasKey(i => i.Id);
                item.Property(i => i.Id).ValueGeneratedNever();
            });
        });
}

/// <summary>
/// EN: Current user with fixed values; anonymous when created without arguments.
/// TR: Sabit değerli aktif kullanıcı; argümansız oluşturulursa anonimdir.
/// </summary>
/// <param name="TenantId">EN: Tenant, or null. TR: Firma veya null.</param>
/// <param name="UserId">EN: User, or null. TR: Kullanıcı veya null.</param>
/// <param name="Plan">EN: Plan, or null. TR: Plan veya null.</param>
public sealed record TestUser(Guid? TenantId = null, Guid? UserId = null, string? Plan = null) : ICurrentUser
{
    /// <summary>
    /// EN: A signed-in user of a brand-new tenant.
    /// TR: Yepyeni bir firmanın giriş yapmış kullanıcısı.
    /// </summary>
    /// <returns>EN: The user. TR: Kullanıcı.</returns>
    public static TestUser OfNewTenant() => new(Guid.CreateVersion7(), Guid.CreateVersion7());
}
