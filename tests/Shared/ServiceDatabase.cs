using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using Testcontainers.PostgreSql;

namespace MyWorkplace.Testing;

/// <summary>
/// EN: A throw-away PostgreSQL with a service's real migrations, for handler tests (T-047). Linked as source into each
///     service's test project (like <c>eng/PostgresImage.cs</c>); a project only adds a one-line subclass that says how
///     to construct its context.
/// TR: Handler testleri için bir servisin gerçek migration'larıyla geçici bir PostgreSQL (T-047). Her servisin test projesine kaynak
///     olarak bağlanır (<c>eng/PostgresImage.cs</c> gibi); bir proje sadece context'ini nasıl oluşturacağını söyleyen tek satırlık bir alt sınıf ekler.
/// </summary>
/// <typeparam name="TContext">EN: The service's DbContext. TR: Servisin DbContext'i.</typeparam>
/// <param name="construct">EN: Builds the context from options and the current user. TR: Context'i seçeneklerden ve mevcut kullanıcıdan kurar.</param>
public abstract class ServiceDatabase<TContext>(Func<DbContextOptions<TContext>, ICurrentUser, TContext> construct) : IAsyncLifetime
    where TContext : DbContext
{
    /// <summary>EN: The container, on the AppHost's version. TR: Konteyner; AppHost'un sürümünde.</summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(MyWorkplace.PostgresImage.Reference).Build();

    /// <summary>
    /// EN: A context acting as a company's system actor, configured like the service's.
    /// TR: Bir firmanın sistem kullanıcısı olarak çalışan, servisinkiyle aynı yapılandırılmış bir context.
    /// </summary>
    /// <param name="tenantId">EN: The company. TR: Firma.</param>
    /// <returns>EN: A new context. TR: Yeni bir context.</returns>
    public TContext Create(Guid tenantId)
    {
        var user = new Actor(tenantId);
        var options = new DbContextOptionsBuilder<TContext>().UseServiceConventions(
            _container.GetConnectionString(),
            new AuditingInterceptor(user, TimeProvider.System),
            new ChangeHistoryInterceptor(user, TimeProvider.System));
        return construct((DbContextOptions<TContext>)options.Options, user);
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = Create(Guid.Empty);
        await db.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>
    /// EN: The system actor of a company, as while an event is processed (ADR-023).
    /// TR: Bir firmanın sistem kullanıcısı; bir olay işlenirken olduğu gibi (ADR-023).
    /// </summary>
    /// <param name="TenantId">EN: The company. TR: Firma.</param>
    private sealed record Actor(Guid? TenantId) : ICurrentUser
    {
        /// <inheritdoc />
        public Guid? UserId => null;

        /// <inheritdoc />
        public string? Plan => null;
    }
}
