using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.InventoryApi;
using static MyWorkplace.IntegrationTests.UsersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Manual stock movements and history (T-030, ADR-020) through the gateway: units converted at the edge, the negative
///     stock policy, parallel issues under Block, and a history that explains the balance.
/// TR: Gateway üzerinden elle stok hareketleri ve geçmiş (T-030, ADR-020): sınırda çevrilen birimler, eksi stok politikası, Block altında
///     paralel çıkışlar ve bakiyeyi açıklayan bir geçmiş.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class StockMovementTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InThenOut_ChangeTheBalance_AndTheHistoryExplainsIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, Ct);

        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 10m, note = " Delivery 42 " }, Ct);
        using var issued = await RecordAsync(client, itemId, new { type = "Out", quantity = 3m }, Ct);
        var lines = await HistoryAsync(client, itemId, Ct);

        Assert.Equal(HttpStatusCode.Created, received.StatusCode);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.Equal(7m, await BalanceAsync(client, itemId, Ct));
        Assert.Equal(["Out 3 → 7", "In 10 → 10"], lines.Select(Describe));
        Assert.All(lines, l => Assert.Equal("Manual", l.GetProperty("reason").GetString()));
        Assert.All(lines, l => Assert.NotEqual(JsonValueKind.Null, l.GetProperty("createdBy").ValueKind));
        Assert.Equal("Delivery 42", lines[1].GetProperty("note").GetString());
    }

    [Fact]
    public async Task RetryWithTheSameIdempotencyKey_RecordsTheMovementOnce()
    {
        // EN: The client lost the first response and sends "in 10" again with the same key (T-057, ADR-027).
        // TR: İstemci ilk cevabı kaybetti ve "10 giriş"i aynı anahtarla tekrar gönderiyor (T-057, ADR-027).
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, Ct);
        var key = Guid.NewGuid().ToString();

        using var first = await RecordWithKeyAsync(client, itemId, key);
        using var retry = await RecordWithKeyAsync(client, itemId, key);
        using var newKey = await RecordWithKeyAsync(client, itemId, Guid.NewGuid().ToString());

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, retry.StatusCode));
        Assert.Equal(["true"], retry.Headers.GetValues("Idempotent-Replayed"));
        Assert.Equal(await first.Content.ReadAsStringAsync(Ct), await retry.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Created, newKey.StatusCode);
        Assert.Equal(20m, await BalanceAsync(client, itemId, Ct));
        Assert.Equal(2, (await HistoryAsync(client, itemId, Ct)).Count);
    }

    [Fact]
    public async Task AlternativeUnit_IsConvertedToTheBaseUnit()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, Ct, units: [new { unit = "BOX", factor = 24m }]);

        using var response = await RecordAsync(client, itemId, new { type = "In", quantity = 2m, unit = "box" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movement = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("movement");
        Assert.Equal(
            ("BOX", 2m, 24m, 48m, 48m),
            (movement.GetProperty("unit").GetString(), movement.GetProperty("quantity").GetDecimal(),
                movement.GetProperty("factor").GetDecimal(), movement.GetProperty("baseQuantity").GetDecimal(),
                movement.GetProperty("balanceAfter").GetDecimal()));
        Assert.Equal(48m, await BalanceAsync(client, itemId, Ct));
    }

    [Theory]
    [InlineData("In", "2.5", null, null, "Quantity")]
    [InlineData("In", "0", null, null, "Quantity")]
    [InlineData("In", "1", "KG", null, "Unit")]
    [InlineData("In", "1", "NO SUCH", null, "Unit")]
    [InlineData("In", "1", null, 501, "Note")]
    [InlineData(null, "1", null, null, "Type")]
    public async Task InvalidMovement_Returns400WithFieldError_AndChangesNothing(
        string? type, string quantity, string? unit, int? noteLength, string invalidField)
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, Ct);

        using var response = await RecordAsync(client, itemId, new
        {
            type,
            quantity = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            unit,
            note = noteLength is { } length ? new string('n', length) : null,
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(invalidField, errors.EnumerateObject().Select(e => e.Name));
        Assert.Equal(0m, await BalanceAsync(client, itemId, Ct));
    }

    [Fact]
    public async Task ConvertedQuantityWithTooManyDecimals_Returns400()
    {
        // EN: 1 BAG = 2.5 PCS: one bag would be 2.5 pieces (refused), two bags are 5 (fine).
        // TR: 1 BAG = 2,5 PCS: bir torba 2,5 adet olurdu (reddedilir), iki torba 5'tir (uygun).
        using var client = await CreateProClientAsync(app, Ct);
        using var bag = await client.PostAsJsonAsync("/inventory/units", new { code = "BAG", name = "Bag", precision = 0 }, Ct);
        bag.EnsureSuccessStatusCode();
        var itemId = await CreateItemAsync(client, Ct, units: [new { unit = "BAG", factor = 2.5m }]);

        using var one = await RecordAsync(client, itemId, new { type = "In", quantity = 1m, unit = "BAG" }, Ct);
        using var two = await RecordAsync(client, itemId, new { type = "In", quantity = 2m, unit = "BAG" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        Assert.Equal(5m, await BalanceAsync(client, itemId, Ct));
    }

    [Fact]
    public async Task Block_OutBeyondTheBalance_Returns409_AndRecordsNothing()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, Ct);
        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 2m }, Ct);

        using var tooMuch = await RecordAsync(client, itemId, new { type = "Out", quantity = 3m }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal(2m, await BalanceAsync(client, itemId, Ct));
        Assert.Single(await HistoryAsync(client, itemId, Ct));
    }

    [Fact]
    public async Task Block_ParallelOuts_NeverTakeTheBalanceBelowZero()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, Ct);
        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 3m }, Ct);

        // EN: Five issues of 1 against a balance of 3, at once: a read-then-write check would let more than 3 through.
        // TR: 3'lük bir bakiyeye karşı aynı anda beş adet 1'lik çıkış: önce okuyup sonra yazan bir kontrol 3'ten fazlasını geçirirdi.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            RecordAsync(client, itemId, new { type = "Out", quantity = 1m }, Ct)));
        var statuses = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.Conflict));
        Assert.Equal(0m, await BalanceAsync(client, itemId, Ct));
    }

    [Theory]
    [InlineData("Allow", false)]
    [InlineData("Warn", true)]
    public async Task AllowAndWarn_ApplyTheOut_FlagIt_AndOnlyWarnWarns(string policy, bool warns)
    {
        using var client = await CreateProClientAsync(app, Ct);
        await SetPolicyAsync(client, policy);
        var itemId = await CreateItemAsync(client, Ct);

        using var issued = await RecordAsync(client, itemId, new { type = "Out", quantity = 3m }, Ct);
        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 1m }, Ct);

        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var body = await issued.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(body.GetProperty("movement").GetProperty("causedNegativeStock").GetBoolean());
        Assert.Equal(warns ? ["NegativeStock"] : [], body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));

        // EN: A receipt while still below zero improves things: never flagged. TR: Hâlâ eksideyken giriş durumu iyileştirir: asla işaretlenmez.
        var receipt = (await received.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("movement");
        Assert.False(receipt.GetProperty("causedNegativeStock").GetBoolean());
        Assert.Equal(-2m, await BalanceAsync(client, itemId, Ct));
    }

    [Fact]
    public async Task Roles_ViewerReadsTheHistory_ButOnlyWritersRecord()
    {
        using var owner = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(owner, Ct);
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await SignInAsNewUserAsync(app, owner, "Member", Ct);

        using var viewerReads = await viewer.GetAsync(Movements(itemId), Ct);
        using var viewerRecords = await RecordAsync(viewer, itemId, new { type = "In", quantity = 1m }, Ct);
        using var memberRecords = await RecordAsync(member, itemId, new { type = "In", quantity = 1m }, Ct);

        Assert.Equal(HttpStatusCode.OK, viewerReads.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerRecords.StatusCode);
        Assert.Equal(HttpStatusCode.Created, memberRecords.StatusCode);
    }

    [Fact]
    public async Task AnotherCompanysOrADeletedItem_IsNotFound()
    {
        using var owner = await CreateProClientAsync(app, Ct);
        using var stranger = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(owner, Ct);
        var deletedId = await CreateItemAsync(owner, Ct);
        using var delete = await owner.DeleteAsync($"/inventory/items/{deletedId}", Ct);
        delete.EnsureSuccessStatusCode();

        using var strangerRecords = await RecordAsync(stranger, itemId, new { type = "In", quantity = 1m }, Ct);
        using var strangerReads = await stranger.GetAsync(Movements(itemId), Ct);
        using var deletedRecords = await RecordAsync(owner, deletedId, new { type = "In", quantity = 1m }, Ct);
        using var deletedReads = await owner.GetAsync(Movements(deletedId), Ct);

        Assert.Equal(HttpStatusCode.NotFound, strangerRecords.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerReads.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedRecords.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedReads.StatusCode);
        Assert.Equal(0m, await BalanceAsync(owner, itemId, Ct));
    }

    [Fact]
    public async Task BasicCompany_Gets403_ThroughGatewayAndDirectly()
    {
        var basicToken = await GetAccessTokenAsync(app, Ct);
        using var viaGateway = app.CreateGatewayClient();
        using var direct = app.CreateDirectServiceClient("inventory");
        Authorize(viaGateway, basicToken);
        Authorize(direct, basicToken);
        var anyItem = Guid.CreateVersion7();

        using var gatewayResponse = await RecordAsync(viaGateway, anyItem, new { type = "In", quantity = 1m }, Ct);
        using var directResponse = await direct.GetAsync(Movements(anyItem), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, gatewayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, directResponse.StatusCode);
    }

    /// <summary>
    /// EN: Posts "in 10" with an Idempotency-Key.
    /// TR: Bir Idempotency-Key ile "10 giriş" gönderir.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="key">EN: The key. TR: Anahtar.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private static Task<HttpResponseMessage> RecordWithKeyAsync(HttpClient client, Guid itemId, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Movements(itemId))
        {
            Content = JsonContent.Create(new { type = "In", quantity = 10m }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request, Ct);
    }

    /// <summary>
    /// EN: Sets the company's negative stock policy.
    /// TR: Firmanın eksi stok politikasını ayarlar.
    /// </summary>
    /// <param name="client">EN: Owner or admin client. TR: Sahip veya yönetici istemcisi.</param>
    /// <param name="policy">EN: Block, Allow or Warn. TR: Block, Allow veya Warn.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static async Task SetPolicyAsync(HttpClient client, string policy)
    {
        using var current = await client.GetAsync("/inventory/settings", Ct);
        using var saved = await client.PutWithIfMatchAsync(
            "/inventory/settings", new { negativeStockPolicy = policy }, current.Headers.ETag!.ToString(), Ct);
        saved.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// EN: A history line as "Type quantity → balance after".
    /// TR: "Tür miktar → sonraki bakiye" biçiminde bir geçmiş satırı.
    /// </summary>
    /// <param name="line">EN: A movement. TR: Bir hareket.</param>
    /// <returns>EN: The text. TR: Metin.</returns>
    private static string Describe(JsonElement line) =>
        $"{line.GetProperty("type").GetString()} {Json.Trim(line.GetProperty("quantity").GetDecimal())} → {Json.Trim(line.GetProperty("balanceAfter").GetDecimal())}";
}
