using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Domain;
using MyWorkplace.BuildingBlocks.Identity;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Base DbContext of every service. Applies the shared conventions of ADR-011 to every entity, so no service
///     can forget them: tenant and soft-delete filters, <c>xmin</c> concurrency, the audit log table, no-tracking reads.
/// TR: Her servisin temel DbContext'i. ADR-011'deki ortak kuralları her entity'ye uygular; böylece hiçbir servis
///     bunları unutamaz: firma ve soft-delete filtreleri, <c>xmin</c> eşzamanlılık, denetim tablosu, takipsiz okumalar.
/// </summary>
public abstract class ServiceDbContext : DbContext
{
    /// <summary>
    /// EN: Name of the shadow property mapped to PostgreSQL's <c>xmin</c> system column.
    /// TR: PostgreSQL'in <c>xmin</c> sistem sütununa eşlenen gölge (shadow) alanın adı.
    /// </summary>
    public const string ConcurrencyTokenProperty = "Version";

    /// <summary>
    /// EN: Current user; read by the tenant filter on every query.
    /// TR: Aktif kullanıcı; firma filtresi her sorguda bunu okur.
    /// </summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// EN: Creates the context with reads untracked by default (ADR-011).
    /// TR: Okumaları varsayılan olarak takipsiz olan context'i oluşturur (ADR-011).
    /// </summary>
    /// <param name="options">EN: Context options. TR: Context seçenekleri.</param>
    /// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
    protected ServiceDbContext(DbContextOptions options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    /// <summary>
    /// EN: Recorded property changes of this service.
    /// TR: Bu servisin kaydedilmiş alan değişiklikleri.
    /// </summary>
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <summary>
    /// EN: Tenant of the current user. EF re-evaluates this member for every query because it belongs to the context.
    /// TR: Aktif kullanıcının firması. Context'e ait olduğu için EF bu üyeyi her sorguda yeniden değerlendirir.
    /// </summary>
    private Guid? CurrentTenantId => _currentUser.TenantId;

    /// <summary>
    /// EN: Sealed on purpose: services describe their model in <see cref="ConfigureModel"/>, then the shared
    ///     conventions run on top of it.
    /// TR: Bilerek sealed: servisler modellerini <see cref="ConfigureModel"/> içinde tanımlar, ardından ortak kurallar
    ///     bunun üzerine uygulanır.
    /// </summary>
    /// <param name="modelBuilder">EN: The model builder. TR: Model builder.</param>
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ApplyConventions(modelBuilder);
    }

    /// <summary>
    /// EN: Describes the service's own entities (keys, lengths, indexes, relations).
    /// TR: Servisin kendi entity'lerini tanımlar (anahtarlar, uzunluklar, indeksler, ilişkiler).
    /// </summary>
    /// <param name="modelBuilder">EN: The model builder. TR: Model builder.</param>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    /// <summary>
    /// EN: Maps the audit log table with an index for "history of one entity" lookups.
    /// TR: Denetim tablosunu, "bir entity'nin geçmişi" sorguları için bir indeksle eşler.
    /// </summary>
    /// <param name="modelBuilder">EN: The model builder. TR: Model builder.</param>
    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("audit_log");
            entity.Property(e => e.EntityType).HasMaxLength(200);
            entity.Property(e => e.Property).HasMaxLength(200);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
        });
    }

    /// <summary>
    /// EN: Applies filters and the concurrency token to every entity type based on the interfaces it implements.
    /// TR: Uyguladığı arayüzlere göre her entity tipine filtreleri ve eşzamanlılık belirtecini ekler.
    /// </summary>
    /// <param name="modelBuilder">EN: The model builder. TR: Model builder.</param>
    private void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var clrType in modelBuilder.Model.GetEntityTypes().Select(t => t.ClrType).ToList())
        {
            if (!typeof(Entity).IsAssignableFrom(clrType))
            {
                continue;
            }

            // EN: Npgsql maps a uint row version to the xmin system column: no extra column, no migration.
            // TR: Npgsql, uint row version'ı xmin sistem sütununa eşler: ek sütun yok, migration yok.
            modelBuilder.Entity(clrType)
                .Property<uint>(ConcurrencyTokenProperty)
                .HasColumnName("xmin")
                .IsRowVersion();

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                InvokeGeneric(nameof(ApplyTenantFilter), clrType, modelBuilder);
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                InvokeGeneric(nameof(ApplySoftDeleteFilter), clrType, modelBuilder);
            }
        }
    }

    /// <summary>
    /// EN: Calls a generic helper for a runtime type (filters need strongly typed lambdas).
    /// TR: Çalışma zamanında bilinen bir tip için generic yardımcıyı çağırır (filtreler tipli lambda gerektirir).
    /// </summary>
    /// <param name="methodName">EN: Helper name. TR: Yardımcı metodun adı.</param>
    /// <param name="clrType">EN: Entity type. TR: Entity tipi.</param>
    /// <param name="modelBuilder">EN: The model builder. TR: Model builder.</param>
    private void InvokeGeneric(string methodName, Type clrType, ModelBuilder modelBuilder) =>
        typeof(ServiceDbContext)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(clrType)
            .Invoke(this, [modelBuilder]);

    /// <summary>
    /// EN: Only rows of the current tenant; no tenant means no rows (safe default).
    /// TR: Sadece aktif firmanın satırları; firma yoksa hiç satır yok (güvenli varsayılan).
    /// </summary>
    /// <typeparam name="TEntity">EN: Tenant-owned entity. TR: Firmaya ait entity.</typeparam>
    /// <param name="modelBuilder">EN: The model builder. TR: Model builder.</param>
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            QueryFilters.Tenant,
            e => CurrentTenantId != null && e.TenantId == CurrentTenantId);

    /// <summary>
    /// EN: Hides soft-deleted rows.
    /// TR: Soft-delete edilmiş satırları gizler.
    /// </summary>
    /// <typeparam name="TEntity">EN: Soft-deletable entity. TR: Soft-delete edilebilen entity.</typeparam>
    /// <param name="modelBuilder">EN: The model builder. TR: Model builder.</param>
    private void ApplySoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ISoftDeletable =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(QueryFilters.SoftDelete, e => !e.IsDeleted);
}
