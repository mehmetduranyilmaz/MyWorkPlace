using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.OrdersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The Orders service through the gateway (T-014, ADR-024): drafts, placing, numbering and the first real event.
/// TR: Gateway üzerinden Orders servisi (T-014, ADR-024): taslaklar, sipariş verme, numaralama ve ilk gerçek olay.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class OrderTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- Drafts

    [Fact]
    public async Task CreateDraft_ServerComputesTotals_AndItCanBeRead()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var customerId = Guid.NewGuid();

        using var created = await CreateAsync(client, new
        {
            customerId,
            customerName = "Acme Ltd",
            total = 999_999m, // EN: ignored TR: yok sayılır
            lines = new[] { new { sku = "BOLT-1", name = "Bolt", quantity = 2.5m, unitPrice = 10.25m } },
        }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var read = await client.GetAsync($"/orders/{id}", Ct);
        var order = await read.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Draft", order.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("number").ValueKind);
        Assert.Equal("Acme Ltd", order.GetProperty("customerName").GetString());
        Assert.Equal(25.63m, order.GetProperty("total").GetDecimal());
        var line = Assert.Single(order.GetProperty("lines").EnumerateArray());
        Assert.Equal(25.63m, line.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(created.Headers.ETag, read.Headers.ETag);
    }

    [Fact]
    public async Task CreateDraft_InvalidForm_Returns400()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);

        using var noLines = await CreateAsync(client, new { lines = Array.Empty<object>() }, Ct);
        using var zeroQuantity = await CreateAsync(client, new
        {
            lines = new[] { new { sku = "A", name = "A", quantity = 0m, unitPrice = 1m } },
        }, Ct);
        using var tooPrecise = await CreateAsync(client, new
        {
            lines = new[] { new { sku = "A", name = "A", quantity = 1m, unitPrice = 10.005m } },
        }, Ct);
        // EN: Rules on the lines themselves — silently skipped before Lines became a List (see OrderInput.Lines).
        // TR: Satırların kendi kuralları — Lines bir List olmadan önce sessizce atlanıyordu (bkz. OrderInput.Lines).
        using var skuTooLong = await CreateAsync(client, new
        {
            lines = new[] { new { sku = new string('A', 300), name = "A", quantity = 1m, unitPrice = 1m } },
        }, Ct);
        using var customerWithoutName = await CreateAsync(client, new
        {
            customerId = Guid.NewGuid(),
            lines = new[] { new { sku = "A", name = "A", quantity = 1m, unitPrice = 1m } },
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, noLines.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zeroQuantity.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, skuTooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, customerWithoutName.StatusCode);
    }

    [Fact]
    public async Task UpdateDraft_ReplacesLines_AndNeedsACurrentETag()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateDraftAsync(client, Ct);
        var oneLine = new { lines = new[] { new { sku = "X", name = "X", quantity = 1m, unitPrice = 5m } } };

        using var updated = await client.PutWithIfMatchAsync($"/orders/{id}", oneLine, etag, Ct);
        using var stale = await client.PutWithIfMatchAsync($"/orders/{id}", oneLine, etag, Ct);
        using var missing = await client.PutWithIfMatchAsync($"/orders/{id}", oneLine, ifMatch: null, Ct);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var order = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(5m, order.GetProperty("total").GetDecimal());
        Assert.Single(order.GetProperty("lines").EnumerateArray());
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
    }

    [Fact]
    public async Task DeleteDraft_Returns204_AndItIsGone()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (id, _) = await CreateDraftAsync(client, Ct);

        using var deleted = await client.DeleteAsync($"/orders/{id}", Ct);
        using var read = await client.GetAsync($"/orders/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task OrderOfAnotherCompany_IsNotFound()
    {
        using var owner = await CreateSignedInClientAsync(app, Ct);
        using var stranger = await CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateDraftAsync(owner, Ct);

        using var read = await stranger.GetAsync($"/orders/{id}", Ct);
        using var place = await PlaceAsync(stranger, id, etag, Ct);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, place.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotCreate_Member_CannotDelete()
    {
        var (owner, _) = await UsersApi.CreateCompanyAsync(app, Ct);
        var (id, _) = await CreateDraftAsync(owner, Ct);
        using var viewer = await UsersApi.SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await UsersApi.SignInAsNewUserAsync(app, owner, "Member", Ct);

        using var viewerCreate = await CreateAsync(viewer, TwoLines(), Ct);
        using var memberCreate = await CreateAsync(member, TwoLines(), Ct);
        using var memberDelete = await member.DeleteAsync($"/orders/{id}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, viewerCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, memberCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberDelete.StatusCode);
    }

    // ---------------------------------------------------------------- Placing

    [Fact]
    public async Task Place_NumbersPerCompany_AndFreezesTheOrder()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateDraftAsync(client, Ct);

        using var placed = await PlaceAsync(client, id, etag, Ct);
        var (_, second) = await CreatePlacedAsync(client, Ct);
        var newEtag = placed.Headers.ETag!.ToString();
        using var placeAgain = await PlaceAsync(client, id, newEtag, Ct);
        using var update = await client.PutWithIfMatchAsync($"/orders/{id}", TwoLines(), newEtag, Ct);
        using var delete = await client.DeleteAsync($"/orders/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var order = await placed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Placed", order.GetProperty("status").GetString());
        Assert.Equal(1001, order.GetProperty("number").GetInt32());
        Assert.Equal(1002, second);
        Assert.Equal(HttpStatusCode.Conflict, placeAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Place_WithoutOrWithStaleIfMatch_IsRejected()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateDraftAsync(client, Ct);
        using var edited = await client.PutWithIfMatchAsync($"/orders/{id}", TwoLines("OTHER"), etag, Ct);

        using var missing = await PlaceAsync(client, id, ifMatch: null, Ct);
        using var stale = await PlaceAsync(client, id, etag, Ct);

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task OrdersPlacedAtOnce_GetDistinctConsecutiveNumbers()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var drafts = new List<(Guid Id, string ETag)>();
        for (var i = 0; i < 5; i++)
        {
            drafts.Add(await CreateDraftAsync(client, Ct));
        }

        // EN: All five race for the counter at once, including the very first number of the company.
        // TR: Beşi de sayaç için aynı anda yarışır; firmanın en ilk numarası da dahil.
        var responses = await Task.WhenAll(drafts.Select(d => PlaceAsync(client, d.Id, d.ETag, Ct)));

        var numbers = new List<int>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            numbers.Add((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("number").GetInt32());
            response.Dispose();
        }

        Assert.Equal([1001, 1002, 1003, 1004, 1005], numbers.Order());
    }

    [Fact]
    public async Task Place_PublishesOrderPlaced_WithTheLines()
    {
        await using var tap = await EventTap.StartAsync(app, "OrderPlaced", Ct);
        using var client = await CreateSignedInClientAsync(app, Ct);
        var sku = $"SKU-{Guid.NewGuid():N}"[..20];
        var (id, etag) = await CreateDraftAsync(client, Ct, TwoLines(sku));

        using var placed = await PlaceAsync(client, id, etag, Ct);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);

        var published = await tap.WaitForAsync(e => Property(e, "orderId")?.GetGuid() == id, Ct);
        Assert.Equal(1001, Property(published, "number")!.Value.GetInt32());
        var lines = Property(published, "lines")!.Value.EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(sku, Property(lines[0], "sku")!.Value.GetString());
        Assert.Equal(2.5m, Property(lines[0], "quantity")!.Value.GetDecimal());
        Assert.NotEqual(Guid.Empty, Property(published, "tenantId")!.Value.GetGuid());
    }

    [Fact]
    public async Task ListOrders_NewestFirst_SearchByNumber()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (placedId, number) = await CreatePlacedAsync(client, Ct);
        var (draftId, _) = await CreateDraftAsync(client, Ct);

        var all = await client.GetFromJsonAsync<JsonElement>("/orders", Ct);
        var byNumber = await client.GetFromJsonAsync<JsonElement>($"/orders?search={number}", Ct);

        Assert.Equal(
            [draftId, placedId],
            all.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        Assert.Equal(placedId, Assert.Single(byNumber.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    /// <summary>
    /// EN: Reads a property whatever its letter case (the wire format is the messaging library's choice).
    /// TR: Bir özelliği harf büyüklüğünden bağımsız okur (kablo biçimi mesajlaşma kütüphanesinin tercihidir).
    /// </summary>
    /// <param name="element">EN: JSON object. TR: JSON nesnesi.</param>
    /// <param name="name">EN: Property name. TR: Özellik adı.</param>
    /// <returns>EN: The value, or null. TR: Değer veya null.</returns>
    private static JsonElement? Property(JsonElement element, string name) =>
        element.EnumerateObject()
            .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(p => (JsonElement?)p.Value)
            .FirstOrDefault();
}
