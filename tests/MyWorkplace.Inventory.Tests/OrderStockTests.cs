using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Features;
using MyWorkplace.Inventory.Persistence;
using Testcontainers.PostgreSql;

namespace MyWorkplace.Inventory.Tests;

/// <summary>
/// EN: A throw-away PostgreSQL with Inventory's real migrations, for handler tests.
/// TR: Handler testleri için Inventory'nin gerçek migration'larıyla geçici bir PostgreSQL.
/// </summary>
public sealed class InventoryDatabase : IAsyncLifetime
{
    /// <summary>EN: The container, on the AppHost's version. TR: Konteyner; AppHost'un sürümünde.</summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(MyWorkplace.PostgresImage.Reference).Build();

    /// <summary>
    /// EN: A context acting as a company's system actor, configured like the service's.
    /// TR: Bir firmanın sistem kullanıcısı olarak çalışan, servisinkiyle aynı yapılandırılmış bir context.
    /// </summary>
    /// <param name="tenantId">EN: The company. TR: Firma.</param>
    /// <returns>EN: A new context. TR: Yeni bir context.</returns>
    public InventoryDbContext Create(Guid tenantId)
    {
        var user = new Actor(tenantId);
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseServiceConventions(
            _container.GetConnectionString(),
            new AuditingInterceptor(user, TimeProvider.System),
            new ChangeHistoryInterceptor(user, TimeProvider.System));
        return new InventoryDbContext((DbContextOptions<InventoryDbContext>)options.Options, user);
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
/// EN: <c>OrderPlaced</c> and <c>OrderCancelled</c> on a real database (T-040, ADR-024), called the way the event
///     dispatcher calls them: one transaction per event. The events may arrive in any order, even at the same moment —
///     the stock must end right every time.
/// TR: Gerçek bir veritabanında <c>OrderPlaced</c> ve <c>OrderCancelled</c> (T-040, ADR-024); olay dağıtıcısının çağırdığı gibi çağrılır: olay
///     başına bir transaction. Olaylar herhangi bir sırayla, hatta aynı anda gelebilir — stok her seferinde doğru bitmelidir.
/// </summary>
/// <param name="database">EN: The test database. TR: Test veritabanı.</param>
public sealed class OrderStockTests(InventoryDatabase database) : IClassFixture<InventoryDatabase>
{
    /// <summary>EN: A fresh company per test. TR: Test başına yeni bir firma.</summary>
    private readonly Guid _tenant = Guid.CreateVersion7();

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlacedThenCancelled_ReturnsExactlyWhatWasIssued()
    {
        var itemId = await CreateItemAsync("BOLT", balance: 5m);
        var order = NewOrder(("bolt", 3m), ("NO-SUCH", 1m));

        await PlaceAsync(order);
        await CancelAsync(order);

        Assert.Equal(5m, await BalanceAsync(itemId));
        var movements = await MovementsAsync(itemId);
        Assert.Equal(
            [(StockMovementType.In, StockMovementReason.Manual, 5m), (StockMovementType.Out, StockMovementReason.Order, 3m),
                (StockMovementType.In, StockMovementReason.OrderCancelled, 3m)],
            movements.Select(m => (m.Type, m.Reason, m.Quantity)));
        Assert.Equal(order.Number, movements[^1].OrderNumber);
    }

    [Fact]
    public async Task CancelledBeforePlaced_IssuesNothing()
    {
        var itemId = await CreateItemAsync("NUT", balance: 5m);
        var order = NewOrder(("NUT", 2m));

        // EN: The two events delivered in reverse. TR: İki olay ters sırayla iletilir.
        await CancelAsync(order);
        await PlaceAsync(order);

        Assert.Equal(5m, await BalanceAsync(itemId));
        Assert.DoesNotContain(await MovementsAsync(itemId), m => m.Reason == StockMovementReason.Order);
    }

    [Fact]
    public async Task CancelledTwice_ReturnsOnce()
    {
        var itemId = await CreateItemAsync("WASHER", balance: 5m);
        var order = NewOrder(("WASHER", 2m));
        await PlaceAsync(order);

        await CancelAsync(order);
        await CancelAsync(order with { EventId = Guid.CreateVersion7() });

        Assert.Equal(5m, await BalanceAsync(itemId));
    }

    [Fact]
    public async Task ItemDeletedMeanwhile_IsSkipped_TheCancellationStillCompletes()
    {
        var kept = await CreateItemAsync("KEPT", balance: 5m);
        var deleted = await CreateItemAsync("GONE", balance: 2m);
        var order = NewOrder(("KEPT", 1m), ("GONE", 2m));
        await PlaceAsync(order);
        await using (var db = database.Create(_tenant))
        {
            db.StockItems.Remove((await db.StockItems.FindForUpdateAsync(deleted, Ct))!);
            await db.SaveChangesAsync(Ct);
        }

        await CancelAsync(order);

        Assert.Equal(5m, await BalanceAsync(kept));
    }

    [Fact]
    public async Task PlacedAndCancelledAtTheSameMoment_AlwaysEndRight()
    {
        // EN: Many orders, each placed and cancelled at once: whoever wins, the stock must end where it started.
        // TR: Birçok sipariş, her biri aynı anda verilip iptal edilir: kim kazanırsa kazansın stok başladığı yerde bitmelidir.
        var itemId = await CreateItemAsync("RIVET", balance: 100m);
        var orders = Enumerable.Range(0, 20).Select(_ => NewOrder(("RIVET", 1m))).ToList();

        await Task.WhenAll(orders.Select(order => Task.WhenAll(Task.Run(() => PlaceAsync(order)), Task.Run(() => CancelAsync(order)))));

        Assert.Equal(100m, await BalanceAsync(itemId));
        var movements = await MovementsAsync(itemId);
        Assert.Equal(
            movements.Count(m => m.Reason == StockMovementReason.Order),
            movements.Count(m => m.Reason == StockMovementReason.OrderCancelled));
    }

    /// <summary>
    /// EN: Creates a stock item (base unit PCS) with a starting balance, and returns its id.
    /// TR: Başlangıç bakiyeli bir stok kalemi (temel birim PCS) oluşturur ve kimliğini döner.
    /// </summary>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="balance">EN: Starting balance. TR: Başlangıç bakiyesi.</param>
    /// <returns>EN: Item id. TR: Kalem kimliği.</returns>
    private async Task<Guid> CreateItemAsync(string sku, decimal balance)
    {
        var item = new StockItem();
        item.Update(sku, sku, SystemUnits.Piece, []);
        await using (var db = database.Create(_tenant))
        {
            db.StockItems.Add(item);
            await db.SaveChangesAsync(Ct);
        }

        await InTransactionAsync(db => new StockLedger(db).RecordManualAsync(
            item.Id,
            new ManualMovement(StockMovementType.In, balance, SystemUnits.Piece, 1m, balance, null),
            NegativeStockPolicy.Block,
            Ct));
        return item.Id;
    }

    /// <summary>
    /// EN: An order of this company with the given lines and a unique number.
    /// TR: Verilen satırlarla ve benzersiz bir numarayla bu firmanın bir siparişi.
    /// </summary>
    /// <param name="lines">EN: SKU and quantity per line. TR: Satır başına SKU ve miktar.</param>
    /// <returns>EN: The OrderPlaced event. TR: OrderPlaced olayı.</returns>
    private OrderPlaced NewOrder(params (string Sku, decimal Quantity)[] lines) => new()
    {
        TenantId = _tenant,
        OrderId = Guid.CreateVersion7(),
        Number = Random.Shared.Next(1, int.MaxValue),
        Lines = [.. lines.Select(l => new OrderPlacedLine(l.Sku, l.Sku, l.Quantity))],
    };

    /// <summary>
    /// EN: Processes OrderPlaced like the dispatcher: one transaction.
    /// TR: OrderPlaced'i dağıtıcı gibi işler: tek transaction.
    /// </summary>
    /// <param name="order">EN: The event. TR: Olay.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private Task PlaceAsync(OrderPlaced order) =>
        InTransactionAsync(db => new OrderPlacedHandler(
            db, new StockLedger(db), new OrderStockClaims(db, TimeProvider.System), NullLogger<OrderPlacedHandler>.Instance)
            .HandleAsync(order, Ct));

    /// <summary>
    /// EN: Processes the order's OrderCancelled like the dispatcher: one transaction.
    /// TR: Siparişin OrderCancelled'ını dağıtıcı gibi işler: tek transaction.
    /// </summary>
    /// <param name="order">EN: The order's OrderPlaced. TR: Siparişin OrderPlaced'i.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private Task CancelAsync(OrderPlaced order) =>
        InTransactionAsync(db => new OrderCancelledHandler(
            db, new OrderStockClaims(db, TimeProvider.System), new StockLedger(db), NullLogger<OrderCancelledHandler>.Instance)
            .HandleAsync(new OrderCancelled { TenantId = order.TenantId, OrderId = order.OrderId, Number = order.Number }, Ct));

    /// <summary>
    /// EN: Runs a unit of work in its own context and transaction, then saves and commits — as the dispatcher does.
    /// TR: Bir iş birimini kendi context'i ve transaction'ında çalıştırır, sonra kaydeder ve onaylar — dağıtıcının yaptığı gibi.
    /// </summary>
    /// <param name="work">EN: The work. TR: İş.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task InTransactionAsync(Func<InventoryDbContext, Task> work)
    {
        await using var db = database.Create(_tenant);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await work(db);
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
    }

    /// <summary>
    /// EN: An item's balance.
    /// TR: Bir kalemin bakiyesi.
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: The balance. TR: Bakiye.</returns>
    private async Task<decimal> BalanceAsync(Guid itemId)
    {
        await using var db = database.Create(_tenant);
        return await db.StockItems.Where(i => i.Id == itemId).Select(i => i.Quantity).SingleAsync(Ct);
    }

    /// <summary>
    /// EN: An item's movements, oldest first.
    /// TR: Bir kalemin hareketleri, en eski önce.
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: The movements. TR: Hareketler.</returns>
    private async Task<List<StockMovement>> MovementsAsync(Guid itemId)
    {
        await using var db = database.Create(_tenant);
        return await db.StockMovements.Where(m => m.StockItemId == itemId).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(Ct);
    }
}
