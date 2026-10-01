using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Features;
using MyWorkplace.Inventory.Persistence;
using MyWorkplace.Testing;

namespace MyWorkplace.Inventory.Tests;

/// <summary>
/// EN: A throw-away PostgreSQL with Inventory's real migrations, for handler tests.
/// TR: Handler testleri için Inventory'nin gerçek migration'larıyla geçici bir PostgreSQL.
/// </summary>
public sealed class InventoryDatabase() : ServiceDatabase<InventoryDbContext>((options, user) => new InventoryDbContext(options, user));

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
    /// <summary>EN: A fresh company per test, with the shared steps. TR: Test başına yeni bir firma ve ortak adımlar.</summary>
    private readonly InventoryData _data = new(database);

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlacedThenCancelled_ReturnsExactlyWhatWasIssued()
    {
        var itemId = await _data.CreateItemAsync("BOLT", balance: 5m, Ct);
        var order = NewOrder(("bolt", 3m), ("NO-SUCH", 1m));

        await PlaceAsync(order);
        await CancelAsync(order);

        Assert.Equal(5m, await _data.BalanceAsync(itemId, Ct));
        var movements = await _data.MovementsAsync(itemId, Ct);
        Assert.Equal(
            [(StockMovementType.In, StockMovementReason.Manual, 5m), (StockMovementType.Out, StockMovementReason.Order, 3m),
                (StockMovementType.In, StockMovementReason.OrderCancelled, 3m)],
            movements.Select(m => (m.Type, m.Reason, m.Quantity)));
        Assert.Equal(order.Number, movements[^1].OrderNumber);
    }

    [Fact]
    public async Task CancelledBeforePlaced_IssuesNothing()
    {
        var itemId = await _data.CreateItemAsync("NUT", balance: 5m, Ct);
        var order = NewOrder(("NUT", 2m));

        // EN: The two events delivered in reverse. TR: İki olay ters sırayla iletilir.
        await CancelAsync(order);
        await PlaceAsync(order);

        Assert.Equal(5m, await _data.BalanceAsync(itemId, Ct));
        Assert.DoesNotContain(await _data.MovementsAsync(itemId, Ct), m => m.Reason == StockMovementReason.Order);
    }

    [Fact]
    public async Task CancelledTwice_ReturnsOnce()
    {
        var itemId = await _data.CreateItemAsync("WASHER", balance: 5m, Ct);
        var order = NewOrder(("WASHER", 2m));
        await PlaceAsync(order);

        await CancelAsync(order);
        await CancelAsync(order with { EventId = Guid.CreateVersion7() });

        Assert.Equal(5m, await _data.BalanceAsync(itemId, Ct));
    }

    [Fact]
    public async Task ItemDeletedMeanwhile_IsSkipped_TheCancellationStillCompletes()
    {
        var kept = await _data.CreateItemAsync("KEPT", balance: 5m, Ct);
        var deleted = await _data.CreateItemAsync("GONE", balance: 2m, Ct);
        var order = NewOrder(("KEPT", 1m), ("GONE", 2m));
        await PlaceAsync(order);
        await using (var db = _data.Context())
        {
            db.StockItems.Remove((await db.StockItems.FindForUpdateAsync(deleted, Ct))!);
            await db.SaveChangesAsync(Ct);
        }

        await CancelAsync(order);

        Assert.Equal(5m, await _data.BalanceAsync(kept, Ct));
    }

    [Fact]
    public async Task PlacedAndCancelledAtTheSameMoment_AlwaysEndRight()
    {
        // EN: Many orders, each placed and cancelled at once: whoever wins, the stock must end where it started.
        // TR: Birçok sipariş, her biri aynı anda verilip iptal edilir: kim kazanırsa kazansın stok başladığı yerde bitmelidir.
        var itemId = await _data.CreateItemAsync("RIVET", balance: 100m, Ct);
        var orders = Enumerable.Range(0, 20).Select(_ => NewOrder(("RIVET", 1m))).ToList();

        await Task.WhenAll(orders.Select(order => Task.WhenAll(Task.Run(() => PlaceAsync(order)), Task.Run(() => CancelAsync(order)))));

        Assert.Equal(100m, await _data.BalanceAsync(itemId, Ct));
        var movements = await _data.MovementsAsync(itemId, Ct);
        Assert.Equal(
            movements.Count(m => m.Reason == StockMovementReason.Order),
            movements.Count(m => m.Reason == StockMovementReason.OrderCancelled));
    }

    [Fact]
    public async Task UnmatchedLines_AreListedOncePerSku_UnlessIgnored()
    {
        await using (var db = _data.Context())
        {
            db.IgnoredSkus.Add(new IgnoredSku { Sku = "SHIPPING", NormalizedSku = "SHIPPING" });
            await db.SaveChangesAsync(Ct);
        }

        var order = NewOrder(("typo-1", 2m), ("TYPO-1", 3m), ("shipping", 1m));
        await PlaceAsync(order);

        await using var check = _data.Context();
        var line = Assert.Single(await check.UnmatchedOrderLines.Where(l => l.OrderId == order.OrderId).ToListAsync(Ct));
        Assert.Equal(("typo-1", "TYPO-1", 5m, UnmatchedLineStatus.Open, order.Number),
            (line.Sku, line.NormalizedSku, line.Quantity, line.Status, line.OrderNumber));
    }

    [Fact]
    public async Task CancellingTheOrder_ClosesItsOpenEntries()
    {
        var order = NewOrder(("UNKNOWN-9", 1m));
        await PlaceAsync(order);

        await CancelAsync(order);

        await using var check = _data.Context();
        Assert.Equal(UnmatchedLineStatus.OrderCancelled,
            (await check.UnmatchedOrderLines.SingleAsync(l => l.OrderId == order.OrderId, Ct)).Status);
    }

    /// <summary>
    /// EN: An order of this company with the given lines and a unique number.
    /// TR: Verilen satırlarla ve benzersiz bir numarayla bu firmanın bir siparişi.
    /// </summary>
    /// <param name="lines">EN: SKU and quantity per line. TR: Satır başına SKU ve miktar.</param>
    /// <returns>EN: The OrderPlaced event. TR: OrderPlaced olayı.</returns>
    private OrderPlaced NewOrder(params (string Sku, decimal Quantity)[] lines) => new()
    {
        TenantId = _data.Tenant,
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
        _data.InTransactionAsync(db => new OrderPlacedHandler(
            db, new StockLedger(db), new OrderStockClaims(db, TimeProvider.System), NullLogger<OrderPlacedHandler>.Instance)
            .HandleAsync(order, Ct), Ct);

    /// <summary>
    /// EN: Processes the order's OrderCancelled like the dispatcher: one transaction.
    /// TR: Siparişin OrderCancelled'ını dağıtıcı gibi işler: tek transaction.
    /// </summary>
    /// <param name="order">EN: The order's OrderPlaced. TR: Siparişin OrderPlaced'i.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private Task CancelAsync(OrderPlaced order) =>
        _data.InTransactionAsync(db => new OrderCancelledHandler(
            db, new OrderStockClaims(db, TimeProvider.System), new StockLedger(db), NullLogger<OrderCancelledHandler>.Instance)
            .HandleAsync(new OrderCancelled { TenantId = order.TenantId, OrderId = order.OrderId, Number = order.Number }, Ct), Ct);
}
