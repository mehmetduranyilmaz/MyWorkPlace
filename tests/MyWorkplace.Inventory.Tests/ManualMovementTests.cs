using MyWorkplace.Inventory.Domain;
using static MyWorkplace.Inventory.Tests.InventoryData;

namespace MyWorkplace.Inventory.Tests;

/// <summary>
/// EN: Manual movements on a real database (T-046, ADR-020, ADR-029). The negative stock policy lives in one atomic SQL
///     statement, so only a real database can break it — these tests prove it there, without starting the system.
/// TR: Gerçek bir veritabanında elle hareketler (T-046, ADR-020, ADR-029). Eksi stok politikası tek bir atomik SQL ifadesinde yaşar; onu
///     sadece gerçek bir veritabanı bozabilir — bu testler onu orada, sistemi başlatmadan kanıtlar.
/// </summary>
/// <param name="database">EN: The test database. TR: Test veritabanı.</param>
public sealed class ManualMovementTests(InventoryDatabase database) : IClassFixture<InventoryDatabase>
{
    /// <summary>EN: A fresh company per test, with the shared steps. TR: Test başına yeni bir firma ve ortak adımlar.</summary>
    private readonly InventoryData _data = new(database);

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Block_IssueBeyondTheBalance_IsRefused_AndRecordsNothing()
    {
        var itemId = await _data.CreateItemAsync("BLOCK", balance: 2m, Ct);

        var (outcome, movement) = await RecordAsync(itemId, Issue(3m), NegativeStockPolicy.Block);

        Assert.Equal((ManualMovementOutcome.NotEnoughStock, (StockMovement?)null), (outcome, movement));
        Assert.Equal(2m, await _data.BalanceAsync(itemId, Ct));
        Assert.Single(await _data.MovementsAsync(itemId, Ct));
    }

    [Fact]
    public async Task Block_IssueUpToTheBalance_IsRecorded()
    {
        var itemId = await _data.CreateItemAsync("EXACT", balance: 2m, Ct);

        var (outcome, movement) = await RecordAsync(itemId, Issue(2m), NegativeStockPolicy.Block);

        Assert.Equal((ManualMovementOutcome.Recorded, 0m, false), (outcome, movement!.BalanceAfter, movement.CausedNegativeStock));
        Assert.Equal(0m, await _data.BalanceAsync(itemId, Ct));
    }

    [Theory]
    [InlineData(NegativeStockPolicy.Allow)]
    [InlineData(NegativeStockPolicy.Warn)]
    public async Task AllowAndWarn_ApplyTheIssue_AndFlagIt_ButAReceiptIsNeverFlagged(NegativeStockPolicy policy)
    {
        var itemId = await _data.CreateItemAsync("NEG", balance: 0m, Ct);

        var (issued, issue) = await RecordAsync(itemId, Issue(3m), policy);
        var (received, receipt) = await RecordAsync(itemId, Receipt(1m), policy);

        Assert.Equal((ManualMovementOutcome.Recorded, -3m, true), (issued, issue!.BalanceAfter, issue.CausedNegativeStock));
        Assert.Equal((ManualMovementOutcome.Recorded, -2m, false), (received, receipt!.BalanceAfter, receipt.CausedNegativeStock));
        Assert.Equal(-2m, await _data.BalanceAsync(itemId, Ct));
    }

    [Fact]
    public async Task UnknownItem_IsNotFound()
    {
        var (outcome, movement) = await RecordAsync(Guid.CreateVersion7(), Receipt(1m), NegativeStockPolicy.Block);

        Assert.Equal((ManualMovementOutcome.ItemNotFound, (StockMovement?)null), (outcome, movement));
    }

    [Fact]
    public async Task AnotherCompanysItem_IsNotFound_AndUnchanged()
    {
        var itemId = await _data.CreateItemAsync("MINE", balance: 5m, Ct);
        var stranger = new InventoryData(database);

        ManualMovementOutcome outcome = default;
        await stranger.InTransactionAsync(
            async db => (outcome, _) = await new StockLedger(db).RecordManualAsync(itemId, Issue(1m), NegativeStockPolicy.Allow, Ct),
            Ct);

        Assert.Equal(ManualMovementOutcome.ItemNotFound, outcome);
        Assert.Equal(5m, await _data.BalanceAsync(itemId, Ct));
    }

    [Fact]
    public async Task Block_ParallelIssues_NeverTakeTheBalanceBelowZero()
    {
        // EN: Five issues of 1 against a balance of 3, at once: a read-then-write check would let more than 3 through.
        // TR: 3'lük bir bakiyeye karşı aynı anda beş adet 1'lik çıkış: önce okuyup sonra yazan bir kontrol 3'ten fazlasını geçirirdi.
        var itemId = await _data.CreateItemAsync("RACE", balance: 3m, Ct);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Task.Run(async () => (await RecordAsync(itemId, Issue(1m), NegativeStockPolicy.Block)).Outcome)));

        Assert.Equal(3, outcomes.Count(o => o == ManualMovementOutcome.Recorded));
        Assert.Equal(2, outcomes.Count(o => o == ManualMovementOutcome.NotEnoughStock));
        Assert.Equal(0m, await _data.BalanceAsync(itemId, Ct));
        Assert.Equal(1 + 3, (await _data.MovementsAsync(itemId, Ct)).Count);
    }

    [Fact]
    public async Task AChangeOutsideATransaction_IsRefused()
    {
        // EN: The balance and its movement must commit together (ADR-020). TR: Bakiye ve hareketi birlikte onaylanmalıdır (ADR-020).
        var itemId = await _data.CreateItemAsync("NOTX", balance: 1m, Ct);
        await using var db = _data.Context();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StockLedger(db).RecordManualAsync(itemId, Receipt(1m), NegativeStockPolicy.Block, Ct));
        Assert.Equal(1m, await _data.BalanceAsync(itemId, Ct));
    }

    /// <summary>
    /// EN: Records a manual movement the way the endpoint does: one transaction.
    /// TR: Elle bir hareketi uç noktanın yaptığı gibi kaydeder: tek transaction.
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="movement">EN: The checked movement. TR: Kontrol edilmiş hareket.</param>
    /// <param name="policy">EN: Negative stock policy. TR: Eksi stok politikası.</param>
    /// <returns>EN: The outcome and the movement. TR: Sonuç ve hareket.</returns>
    private async Task<(ManualMovementOutcome Outcome, StockMovement? Movement)> RecordAsync(
        Guid itemId, ManualMovement movement, NegativeStockPolicy policy)
    {
        (ManualMovementOutcome, StockMovement?) result = default;
        await _data.InTransactionAsync(
            async db => result = await new StockLedger(db).RecordManualAsync(itemId, movement, policy, Ct),
            Ct);
        return result;
    }
}
