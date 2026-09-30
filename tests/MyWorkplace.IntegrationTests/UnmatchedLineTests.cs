using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.OrdersApi;
using static MyWorkplace.IntegrationTests.UsersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The review list of unmatched order lines (T-042, ADR-020) through the gateway: a line matching no item is listed,
///     a person resolves it from an item or dismisses it, and a cancelled order closes it — nothing is issued twice.
/// TR: Gateway üzerinden eşleşmeyen sipariş satırlarının inceleme listesi (T-042, ADR-020): hiçbir kalemle eşleşmeyen bir satır listelenir, bir
///     insan onu bir kalemden çözer veya yok sayar ve iptal edilen bir sipariş onu kapatır — hiçbir şey iki kez çıkılmaz.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class UnmatchedLineTests(AppFixture app)
{
    /// <summary>EN: Review list address. TR: İnceleme listesi adresi.</summary>
    private const string Lines = "/inventory/unmatched-lines";

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResolvedLine_IsIssuedFromTheItem_AndCancellingReturnsIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client);
        var (orderId, number) = await PlaceWithSkuAsync(client, "TYPO-SKU", 3m);
        var line = await WaitForOpenLineAsync(client, orderId);

        using var resolved = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = itemId }, Ct);
        using var again = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = itemId }, Ct);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(-3m, await BalanceAsync(client, itemId));
        var issue = (await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{itemId}/movements", Ct)).GetProperty("items")[0];
        Assert.Equal(("Order", number), (issue.GetProperty("reason").GetString(), issue.GetProperty("orderNumber").GetInt32()));

        // EN: The resolved quantity is an issue of the order, so T-040's cancellation returns it too.
        // TR: Çözülen miktar siparişin bir çıkışıdır; bu yüzden T-040'ın iptali onu da geri verir.
        using var cancelled = await CancelAsync(client, orderId, await ETagOfAsync(client, orderId, Ct), Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        await WaitUntilAsync(async () => await BalanceAsync(client, itemId) == 0m);
    }

    [Fact]
    public async Task CancelledOrder_ClosesItsLine_SoItCantBeResolved()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client);
        var (orderId, _) = await PlaceWithSkuAsync(client, "NOPE-SKU", 1m);
        var line = await WaitForOpenLineAsync(client, orderId);

        using var cancelled = await CancelAsync(client, orderId, await ETagOfAsync(client, orderId, Ct), Ct);
        await WaitUntilAsync(async () => (await LinesAsync(client, "OrderCancelled")).Contains(line));
        using var resolve = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = itemId }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        Assert.Equal(0m, await BalanceAsync(client, itemId));
    }

    [Fact]
    public async Task DismissWithIgnoreSku_LeavesStock_AndLaterOrdersDontListTheSku()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = $"SVC-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var (firstOrder, _) = await PlaceWithSkuAsync(client, sku, 1m);
        var line = await WaitForOpenLineAsync(client, firstOrder);

        using var dismissed = await client.PostAsJsonAsync($"{Lines}/{line}/dismiss", new { note = "A service", ignoreSku = true }, Ct);
        var (secondOrder, _) = await PlaceWithSkuAsync(client, sku.ToLowerInvariant(), 1m);
        var (marker, _) = await PlaceWithSkuAsync(client, "MARKER-SKU", 1m);
        await WaitForOpenLineAsync(client, marker);
        var ignored = await client.GetFromJsonAsync<JsonElement>("/inventory/ignored-skus", Ct);

        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
        Assert.Equal("Dismissed", (await dismissed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.DoesNotContain(await OpenOrdersAsync(client), o => o == secondOrder);
        Assert.Contains(ignored.EnumerateArray(), s => s.GetProperty("sku").GetString() == sku);

        using var removed = await client.DeleteAsync($"/inventory/ignored-skus/{sku.ToLowerInvariant()}", Ct);
        using var removedAgain = await client.DeleteAsync($"/inventory/ignored-skus/{sku}", Ct);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (removed.StatusCode, removedAgain.StatusCode));
    }

    [Fact]
    public async Task ResolvingFromAnUnknownItem_Returns404()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var (orderId, _) = await PlaceWithSkuAsync(client, "LOST-SKU", 1m);
        var line = await WaitForOpenLineAsync(client, orderId);

        using var response = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = Guid.CreateVersion7() }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Roles_TenantsAndPlan()
    {
        var (ownerClient, _) = await CreateCompanyAsync(app, Ct);
        using var owner = ownerClient;
        using var upgrade = await UpgradeAsync(owner, Ct);
        upgrade.EnsureSuccessStatusCode();
        Authorize(owner, (await upgrade.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);
        var (orderId, _) = await PlaceWithSkuAsync(owner, "ROLE-SKU", 1m);
        var line = await WaitForOpenLineAsync(owner, orderId);
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await SignInAsNewUserAsync(app, owner, "Member", Ct);
        using var stranger = await CreateProClientAsync(app, Ct);
        var basicToken = await GetAccessTokenAsync(app, Ct);
        using var basic = app.CreateGatewayClient();
        Authorize(basic, basicToken);

        using var viewerReads = await viewer.GetAsync(Lines, Ct);
        using var viewerDismisses = await viewer.PostAsJsonAsync($"{Lines}/{line}/dismiss", new { }, Ct);
        using var strangerDismisses = await stranger.PostAsJsonAsync($"{Lines}/{line}/dismiss", new { }, Ct);
        using var memberRemovesIgnored = await member.DeleteAsync("/inventory/ignored-skus/ANY", Ct);
        using var basicReads = await basic.GetAsync(Lines, Ct);
        using var memberDismisses = await member.PostAsJsonAsync($"{Lines}/{line}/dismiss", new { }, Ct);

        Assert.Equal(HttpStatusCode.OK, viewerReads.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerDismisses.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerDismisses.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberRemovesIgnored.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, basicReads.StatusCode);
        Assert.Equal(HttpStatusCode.OK, memberDismisses.StatusCode);
    }

    /// <summary>
    /// EN: Creates a stock item (base unit PCS, balance 0) and returns its id.
    /// TR: Bir stok kalemi (temel birim PCS, bakiye 0) oluşturur ve kimliğini döner.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <returns>EN: Item id. TR: Kalem kimliği.</returns>
    private static async Task<Guid> CreateItemAsync(HttpClient client)
    {
        var sku = $"UNM-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        using var response = await client.PostAsJsonAsync("/inventory/items", new { sku, name = "Item", baseUnit = "PCS" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// EN: Places an order with one line.
    /// TR: Tek satırlı bir sipariş verir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="quantity">EN: Quantity. TR: Miktar.</param>
    /// <returns>EN: Order id and number. TR: Sipariş kimliği ve numarası.</returns>
    private static async Task<(Guid Id, int Number)> PlaceWithSkuAsync(HttpClient client, string sku, decimal quantity)
    {
        var (id, etag) = await CreateDraftAsync(client, Ct, new { lines = new[] { new { sku, name = "Ordered", quantity, unitPrice = 1m } } });
        using var placed = await PlaceAsync(client, id, etag, Ct);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        return (id, (await placed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("number").GetInt32());
    }

    /// <summary>
    /// EN: Waits until the order's line is listed as open, and returns the entry id.
    /// TR: Siparişin satırı açık olarak listelenene kadar bekler ve kayıt kimliğini döner.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="orderId">EN: The order. TR: Sipariş.</param>
    /// <returns>EN: Entry id. TR: Kayıt kimliği.</returns>
    private static async Task<Guid> WaitForOpenLineAsync(HttpClient client, Guid orderId)
    {
        Guid? found = null;
        await WaitUntilAsync(async () =>
        {
            var page = await client.GetFromJsonAsync<JsonElement>($"{Lines}?pageSize=100", Ct);
            found = page.GetProperty("items").EnumerateArray()
                .Where(l => l.GetProperty("orderId").GetGuid() == orderId)
                .Select(l => (Guid?)l.GetProperty("id").GetGuid())
                .FirstOrDefault();
            return found is not null;
        });
        return found!.Value;
    }

    /// <summary>
    /// EN: Entry ids with a status.
    /// TR: Bir durumdaki kayıt kimlikleri.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="status">EN: Status. TR: Durum.</param>
    /// <returns>EN: The ids. TR: Kimlikler.</returns>
    private static async Task<List<Guid>> LinesAsync(HttpClient client, string status) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"{Lines}?status={status}&pageSize=100", Ct))
            .GetProperty("items").EnumerateArray().Select(l => l.GetProperty("id").GetGuid())];

    /// <summary>
    /// EN: Order ids of the open entries.
    /// TR: Açık kayıtların sipariş kimlikleri.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <returns>EN: The order ids. TR: Sipariş kimlikleri.</returns>
    private static async Task<List<Guid>> OpenOrdersAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"{Lines}?pageSize=100", Ct))
            .GetProperty("items").EnumerateArray().Select(l => l.GetProperty("orderId").GetGuid())];

    /// <summary>
    /// EN: Reads an item's balance.
    /// TR: Bir kalemin bakiyesini okur.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: The balance. TR: Bakiye.</returns>
    private static async Task<decimal> BalanceAsync(HttpClient client, Guid itemId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{itemId}", Ct)).GetProperty("quantity").GetDecimal();

    /// <summary>
    /// EN: Polls a condition until it holds; fails the test after 30 seconds.
    /// TR: Bir koşulu sağlanana kadar yoklar; 30 saniye sonra testi düşürür.
    /// </summary>
    /// <param name="condition">EN: The condition. TR: Koşul.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The event did not reach Inventory in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        }
    }
}
