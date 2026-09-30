using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Orders.Domain;
using MyWorkplace.Orders.Features;
using MyWorkplace.Orders.Persistence;
using Testcontainers.PostgreSql;

namespace MyWorkplace.Orders.Tests;

/// <summary>
/// EN: A throw-away PostgreSQL with Orders' real migrations, for handler tests.
/// TR: Handler testleri için Orders'ın gerçek migration'larıyla geçici bir PostgreSQL.
/// </summary>
public sealed class OrdersDatabase : IAsyncLifetime
{
    /// <summary>EN: The container, on the AppHost's version. TR: Konteyner; AppHost'un sürümünde.</summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(MyWorkplace.PostgresImage.Reference).Build();

    /// <summary>
    /// EN: A context acting as a company's system actor, configured like the service's.
    /// TR: Bir firmanın sistem kullanıcısı olarak çalışan, servisinkiyle aynı yapılandırılmış bir context.
    /// </summary>
    /// <param name="tenantId">EN: The company. TR: Firma.</param>
    /// <returns>EN: A new context. TR: Yeni bir context.</returns>
    public OrdersDbContext Create(Guid tenantId)
    {
        var user = new Actor(tenantId);
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseServiceConventions(
            _container.GetConnectionString(),
            new AuditingInterceptor(user, TimeProvider.System),
            new ChangeHistoryInterceptor(user, TimeProvider.System));
        return new OrdersDbContext((DbContextOptions<OrdersDbContext>)options.Options, user);
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

/// <summary>
/// EN: The customer replica on a real database (T-039, ADR-024), with customer events delivered in every order: the
///     replica must always end at the newest change (last write wins by <c>ChangedAt</c>).
/// TR: Gerçek bir veritabanında müşteri kopyası (T-039, ADR-024); müşteri olayları her sırayla iletilir: kopya her zaman en yeni değişiklikte
///     bitmelidir (<c>ChangedAt</c>'e göre son yazan kazanır).
/// </summary>
/// <param name="database">EN: The test database. TR: Test veritabanı.</param>
public sealed class CustomerReplicaTests(OrdersDatabase database) : IClassFixture<OrdersDatabase>
{
    /// <summary>EN: A fixed starting time. TR: Sabit bir başlangıç zamanı.</summary>
    private static readonly DateTimeOffset _t0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>EN: A fresh company per test. TR: Test başına yeni bir firma.</summary>
    private readonly Guid _tenant = Guid.CreateVersion7();

    /// <summary>EN: A fresh customer per test. TR: Test başına yeni bir müşteri.</summary>
    private readonly Guid _customer = Guid.CreateVersion7();

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatedThenUpdated_KeepsTheNewName()
    {
        await HandleAsync(Created("Acme", _t0));
        await HandleAsync(Updated("Acme Ltd", _t0.AddMinutes(1)));

        Assert.Equal(("Acme Ltd", false), await ReplicaAsync());
    }

    [Fact]
    public async Task AnOldUpdateArrivingLate_IsIgnored()
    {
        await HandleAsync(Created("Acme", _t0));
        await HandleAsync(Updated("Newest", _t0.AddMinutes(2)));
        await HandleAsync(Updated("Older", _t0.AddMinutes(1)));

        Assert.Equal(("Newest", false), await ReplicaAsync());
    }

    [Fact]
    public async Task UpdatedBeforeCreated_EndsAtTheUpdate()
    {
        await HandleAsync(Updated("Acme Ltd", _t0.AddMinutes(1)));
        await HandleAsync(Created("Acme", _t0));

        Assert.Equal(("Acme Ltd", false), await ReplicaAsync());
    }

    [Fact]
    public async Task Deleted_KeepsTheLastName_AndALateCreationDoesntReviveIt()
    {
        await HandleAsync(Deleted(_t0.AddMinutes(1)));
        await HandleAsync(Created("Acme", _t0));

        Assert.Equal(("", true), await ReplicaAsync());

        var other = Guid.CreateVersion7();
        await HandleAsync(Created("Beta", _t0) with { CustomerId = other });
        await HandleAsync(Deleted(_t0.AddMinutes(1)) with { CustomerId = other });
        Assert.Equal(("Beta", true), await ReplicaAsync(other));
    }

    [Fact]
    public async Task ManyUpdatesAtOnce_EndAtTheNewest()
    {
        // EN: Ten updates processed in parallel, in random order: only the newest name may remain.
        // TR: Paralel ve rastgele sırayla işlenen on güncelleme: sadece en yeni ad kalabilir.
        var updates = Enumerable.Range(1, 10).Select(i => Updated($"Name {i:D2}", _t0.AddSeconds(i))).OrderBy(_ => Random.Shared.Next());

        await Task.WhenAll(updates.Select(u => Task.Run(() => HandleAsync(u))));

        Assert.Equal(("Name 10", false), await ReplicaAsync());
    }

    /// <summary>
    /// EN: A CustomerCreated of the test's customer.
    /// TR: Testin müşterisinin bir CustomerCreated'ı.
    /// </summary>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="at">EN: When. TR: Ne zaman.</param>
    /// <returns>EN: The event. TR: Olay.</returns>
    private CustomerCreated Created(string name, DateTimeOffset at) =>
        new() { TenantId = _tenant, CustomerId = _customer, Name = name, ChangedAt = at };

    /// <summary>
    /// EN: A CustomerUpdated of the test's customer.
    /// TR: Testin müşterisinin bir CustomerUpdated'ı.
    /// </summary>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="at">EN: When. TR: Ne zaman.</param>
    /// <returns>EN: The event. TR: Olay.</returns>
    private CustomerUpdated Updated(string name, DateTimeOffset at) =>
        new() { TenantId = _tenant, CustomerId = _customer, Name = name, ChangedAt = at };

    /// <summary>
    /// EN: A CustomerDeleted of the test's customer.
    /// TR: Testin müşterisinin bir CustomerDeleted'ı.
    /// </summary>
    /// <param name="at">EN: When. TR: Ne zaman.</param>
    /// <returns>EN: The event. TR: Olay.</returns>
    private CustomerDeleted Deleted(DateTimeOffset at) =>
        new() { TenantId = _tenant, CustomerId = _customer, ChangedAt = at };

    /// <summary>
    /// EN: Processes one event like the dispatcher: its own context and transaction.
    /// TR: Bir olayı dağıtıcı gibi işler: kendi context'i ve transaction'ı.
    /// </summary>
    /// <param name="integrationEvent">EN: The event. TR: Olay.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task HandleAsync(object integrationEvent)
    {
        await using var db = database.Create(_tenant);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var handlers = new CustomerEventHandlers(new CustomerReplicas(db, TimeProvider.System));
        await (integrationEvent switch
        {
            CustomerCreated created => handlers.HandleAsync(created, Ct),
            CustomerUpdated updated => handlers.HandleAsync(updated, Ct),
            CustomerDeleted deleted => handlers.HandleAsync(deleted, Ct),
            _ => throw new ArgumentException("Not a customer event.", nameof(integrationEvent)),
        });
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
    }

    /// <summary>
    /// EN: The replica's name and removed flag for a customer.
    /// TR: Bir müşteri için kopyanın adı ve silindi işareti.
    /// </summary>
    /// <param name="customerId">EN: The customer; the test's own when null. TR: Müşteri; null ise testin kendisi.</param>
    /// <returns>EN: Name and removed. TR: Ad ve silindi.</returns>
    private async Task<(string Name, bool Removed)> ReplicaAsync(Guid? customerId = null)
    {
        await using var db = database.Create(_tenant);
        var id = customerId ?? _customer;
        var replica = await db.CustomerReplicas.SingleAsync(c => c.Id == id, Ct);
        return (replica.Name, replica.Removed);
    }
}
