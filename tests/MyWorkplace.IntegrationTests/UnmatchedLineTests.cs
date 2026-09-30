using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.InventoryApi;
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
        var itemId = await CreateItemAsync(client, Ct);
        var (orderId, number) = await PlaceLinesAsync(client, Ct, ("TYPO-SKU", 3m));
        var line = await WaitForOpenLineAsync(client, orderId);

        using var resolved = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = itemId }, Ct);
        using var again = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = itemId }, Ct);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(-3m, await BalanceAsync(client, itemId, Ct));
        var issue = (await HistoryAsync(client, itemId, Ct))[0];
        Assert.Equal(("Order", number), (issue.GetProperty("reason").GetString(), issue.GetProperty("orderNumber").GetInt32()));

        // EN: The resolved quantity is an issue of the order, so T-040's cancellation returns it too.
        // TR: Çözülen miktar siparişin bir çıkışıdır; bu yüzden T-040'ın iptali onu da geri verir.
        using var cancelled = await CancelAsync(client, orderId, await ETagOfAsync(client, orderId, Ct), Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        await Eventually.UntilAsync(
            async () => await BalanceAsync(client, itemId, Ct) == 0m, "the cancellation to reach Inventory", Ct);
    }

    [Fact]
    public async Task CancelledOrder_ClosesItsLine_SoItCantBeResolved()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, Ct);
        var (orderId, _) = await PlaceLinesAsync(client, Ct, ("NOPE-SKU", 1m));
        var line = await WaitForOpenLineAsync(client, orderId);

        using var cancelled = await CancelAsync(client, orderId, await ETagOfAsync(client, orderId, Ct), Ct);
        await Eventually.UntilAsync(
            async () => (await LinesAsync(client, "OrderCancelled")).Contains(line), "the cancellation to close the line", Ct);
        using var resolve = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = itemId }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        Assert.Equal(0m, await BalanceAsync(client, itemId, Ct));
    }

    [Fact]
    public async Task DismissWithIgnoreSku_LeavesStock_AndLaterOrdersDontListTheSku()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = $"SVC-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var (firstOrder, _) = await PlaceLinesAsync(client, Ct, (sku, 1m));
        var line = await WaitForOpenLineAsync(client, firstOrder);

        using var dismissed = await client.PostAsJsonAsync($"{Lines}/{line}/dismiss", new { note = "A service", ignoreSku = true }, Ct);
        var (secondOrder, _) = await PlaceLinesAsync(client, Ct, (sku.ToLowerInvariant(), 1m));
        var (marker, _) = await PlaceLinesAsync(client, Ct, ("MARKER-SKU", 1m));
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
        var (orderId, _) = await PlaceLinesAsync(client, Ct, ("LOST-SKU", 1m));
        var line = await WaitForOpenLineAsync(client, orderId);

        using var response = await client.PostAsJsonAsync($"{Lines}/{line}/resolve", new { stockItemId = Guid.CreateVersion7() }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Roles_TenantsAndPlan()
    {
        using var owner = await CreateProClientAsync(app, Ct);
        var (orderId, _) = await PlaceLinesAsync(owner, Ct, ("ROLE-SKU", 1m));
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
    /// EN: Waits until the order's line is listed as open, and returns the entry id.
    /// TR: Siparişin satırı açık olarak listelenene kadar bekler ve kayıt kimliğini döner.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="orderId">EN: The order. TR: Sipariş.</param>
    /// <returns>EN: Entry id. TR: Kayıt kimliği.</returns>
    private static async Task<Guid> WaitForOpenLineAsync(HttpClient client, Guid orderId)
    {
        Guid? found = null;
        await Eventually.UntilAsync(
            async () =>
            {
                var page = await client.GetFromJsonAsync<JsonElement>($"{Lines}?pageSize=100", Ct);
                found = page.GetProperty("items").EnumerateArray()
                    .Where(l => l.GetProperty("orderId").GetGuid() == orderId)
                    .Select(l => (Guid?)l.GetProperty("id").GetGuid())
                    .FirstOrDefault();
                return found is not null;
            },
            "the order's line to be listed",
            Ct);
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
}
