using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.OrdersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: One service being down doesn't stop the others (T-017, ADR-007): with Inventory stopped, orders are still
///     accepted; when it starts again, the stock catches up from the events waiting in its queue. Runs on its own copy
///     of the system, because every other test shares one and must not see Inventory go down.
/// TR: Bir servisin kapalı olması diğerlerini durdurmaz (T-017, ADR-007): Inventory durdurulmuşken siparişler yine alınır; yeniden
///     başlayınca stok, kuyruğunda bekleyen olaylardan yetişir. Kendi sistem kopyasında çalışır; çünkü diğer bütün testler tek bir
///     sistemi paylaşır ve Inventory'nin kapandığını görmemelidir.
/// </summary>
/// <param name="app">EN: A private copy of the system. TR: Sistemin özel bir kopyası.</param>
[Collection(typeof(ServiceOutageCollection))]
public sealed class ResilienceTests(IsolatedAppFixture app) : IClassFixture<IsolatedAppFixture>
{
    /// <summary>EN: The service stopped in these tests. TR: Bu testlerde durdurulan servis.</summary>
    private const string Inventory = "inventory";

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// EN: Quarantine (T-044): on Linux, Aspire fails to stop the resource and loses track of it (state "Unknown", CI #28
    ///     and #29), while it works on Windows. Skipped there — visibly, with this reason — until T-044 finds the cause.
    /// TR: Karantina (T-044): Linux'ta Aspire kaynağı durduramıyor ve izini kaybediyor (durum "Unknown", CI #28 ve #29); Windows'ta
    ///     ise çalışıyor. T-044 sebebi bulana kadar orada atlanır — görünür şekilde, bu gerekçeyle.
    /// </summary>
    public static bool StopIsUnreliableHere => OperatingSystem.IsLinux();

    [Fact(
        Skip = "Quarantined on Linux: Aspire can't stop the resource there (state 'Unknown'). See T-044.",
        SkipWhen = nameof(StopIsUnreliableHere))]
    public async Task OrdersAreAccepted_WhileInventoryIsDown_AndStockCatchesUpWhenItReturns()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = $"RES-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var itemId = await CreateItemAsync(client, sku);

        // EN: 1) Inventory goes down — really stopped, not simulated.
        // TR: 1) Inventory kapanır — gerçekten durdurulur, taklit edilmez.
        await StopInventoryAsync();

        // EN: 2) The gateway answers quickly with an error instead of hanging.
        // TR: 2) Gateway asılı kalmak yerine hızla bir hatayla cevap verir.
        var watch = Stopwatch.StartNew();
        using (var down = await client.GetAsync($"/inventory/items/{itemId}", Ct))
        {
            Assert.True((int)down.StatusCode >= 500, $"Expected a 5xx while Inventory is down, got {down.StatusCode}.");
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The gateway took {watch.Elapsed} to answer.");

        // EN: 3) Orders keep working: Orders doesn't need Inventory.
        // TR: 3) Siparişler çalışmaya devam eder: Orders'ın Inventory'ye ihtiyacı yoktur.
        await PlaceOrderAsync(client, sku, 1m);
        await PlaceOrderAsync(client, sku, 2m);

        // EN: 4) Inventory comes back and processes what it missed.
        // TR: 4) Inventory geri gelir ve kaçırdıklarını işler.
        await ExecuteAsync(KnownResourceCommands.StartCommand);
        await app.App.ResourceNotifications.WaitForResourceHealthyAsync(Inventory, Ct);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (await QuantityAsync(client, itemId) != -3m)
        {
            Assert.True(DateTime.UtcNow < deadline, "Stock did not catch up after Inventory returned.");
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }
    }

    /// <summary>
    /// EN: Stops Inventory and waits until it has really stopped. On Linux the command asks the process to shut down
    ///     gracefully and may report a failure while the process is still finishing (CI #28, T-043); what counts is the
    ///     final state, so that is what this waits for — and it reports the command's answer and the state if it never
    ///     stops.
    /// TR: Inventory'yi durdurur ve gerçekten durana kadar bekler. Linux'ta komut sürecin düzgün kapanmasını ister ve süreç hâlâ
    ///     kapanırken hata bildirebilir (CI #28, T-043); önemli olan son durumdur, bu yüzden onu bekler — ve süreç hiç durmazsa
    ///     komutun cevabını ve durumu raporlar.
    /// </summary>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task StopInventoryAsync()
    {
        var notifications = app.App.ResourceNotifications;
        await notifications.WaitForResourceAsync(Inventory, KnownResourceStates.Running, Ct);

        var result = await app.App.ResourceCommands.ExecuteCommandAsync(Inventory, KnownResourceCommands.StopCommand, Ct);
        if (!result.Success)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Stop reported: {result.Message}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await notifications.WaitForResourceAsync(Inventory, KnownResourceStates.TerminalStates, timeout.Token);
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
            var state = notifications.TryGetCurrentState(Inventory, out var current)
                ? current.Snapshot.State?.Text
                : "unknown";
            Assert.Fail($"{Inventory} did not stop within 60 s. Stop command: " +
                $"{(result.Success ? "succeeded" : $"failed ({result.Message})")}; current state: {state}.");
        }
    }

    /// <summary>
    /// EN: Runs an Aspire command (stop / start) on Inventory and fails the test if it didn't succeed.
    /// TR: Inventory üzerinde bir Aspire komutu (durdur / başlat) çalıştırır; başarılı olmazsa testi düşürür.
    /// </summary>
    /// <param name="command">EN: Command name. TR: Komut adı.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task ExecuteAsync(string command)
    {
        var result = await app.App.ResourceCommands.ExecuteCommandAsync(Inventory, command, Ct);
        Assert.True(result.Success, $"'{command}' on {Inventory} failed: {result.Message}");
    }

    /// <summary>
    /// EN: Creates a stock item (balance 0) and returns its id.
    /// TR: Bir stok kalemi (bakiye 0) oluşturur ve kimliğini döner.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <returns>EN: Item id. TR: Kalem kimliği.</returns>
    private static async Task<Guid> CreateItemAsync(HttpClient client, string sku)
    {
        using var response = await client.PostAsJsonAsync(
            "/inventory/items", new { sku, name = "Bolt", baseUnit = "PCS" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// EN: Creates and places a one-line order; fails the test unless both succeed.
    /// TR: Tek satırlı bir sipariş oluşturur ve verir; ikisi de başarılı olmazsa testi düşürür.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="quantity">EN: Quantity. TR: Miktar.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static async Task PlaceOrderAsync(HttpClient client, string sku, decimal quantity)
    {
        var (id, etag) = await CreateDraftAsync(client, Ct, new
        {
            lines = new[] { new { sku, name = "Bolt", quantity, unitPrice = 1m } },
        });
        using var placed = await PlaceAsync(client, id, etag, Ct);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
    }

    /// <summary>
    /// EN: Reads an item's balance through the gateway.
    /// TR: Bir kalemin bakiyesini gateway üzerinden okur.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: The balance. TR: Bakiye.</returns>
    private static async Task<decimal> QuantityAsync(HttpClient client, Guid itemId)
    {
        using var response = await client.GetAsync($"/inventory/items/{itemId}", Ct);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("quantity").GetDecimal()
            : decimal.MinValue;
    }
}
