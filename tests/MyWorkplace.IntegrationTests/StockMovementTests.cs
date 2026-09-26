using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
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
        var itemId = await CreateItemAsync(client);

        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 10m, note = " Delivery 42 " });
        using var issued = await RecordAsync(client, itemId, new { type = "Out", quantity = 3m });
        var history = await client.GetFromJsonAsync<JsonElement>(Movements(itemId), Ct);

        Assert.Equal(HttpStatusCode.Created, received.StatusCode);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.Equal(7m, await BalanceAsync(client, itemId));
        var lines = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Out 3 → 7", "In 10 → 10"], lines.Select(Describe));
        Assert.All(lines, l => Assert.Equal("Manual", l.GetProperty("reason").GetString()));
        Assert.All(lines, l => Assert.NotEqual(JsonValueKind.Null, l.GetProperty("createdBy").ValueKind));
        Assert.Equal("Delivery 42", lines[1].GetProperty("note").GetString());
    }

    [Fact]
    public async Task AlternativeUnit_IsConvertedToTheBaseUnit()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, units: [new { unit = "BOX", factor = 24m }]);

        using var response = await RecordAsync(client, itemId, new { type = "In", quantity = 2m, unit = "box" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movement = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("movement");
        Assert.Equal(
            ("BOX", 2m, 24m, 48m, 48m),
            (movement.GetProperty("unit").GetString(), movement.GetProperty("quantity").GetDecimal(),
                movement.GetProperty("factor").GetDecimal(), movement.GetProperty("baseQuantity").GetDecimal(),
                movement.GetProperty("balanceAfter").GetDecimal()));
        Assert.Equal(48m, await BalanceAsync(client, itemId));
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
        var itemId = await CreateItemAsync(client);

        using var response = await RecordAsync(client, itemId, new
        {
            type,
            quantity = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            unit,
            note = noteLength is { } length ? new string('n', length) : null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(invalidField, errors.EnumerateObject().Select(e => e.Name));
        Assert.Equal(0m, await BalanceAsync(client, itemId));
    }

    [Fact]
    public async Task ConvertedQuantityWithTooManyDecimals_Returns400()
    {
        // EN: 1 BAG = 2.5 PCS: one bag would be 2.5 pieces (refused), two bags are 5 (fine).
        // TR: 1 BAG = 2,5 PCS: bir torba 2,5 adet olurdu (reddedilir), iki torba 5'tir (uygun).
        using var client = await CreateProClientAsync(app, Ct);
        using var bag = await client.PostAsJsonAsync("/inventory/units", new { code = "BAG", name = "Bag", precision = 0 }, Ct);
        bag.EnsureSuccessStatusCode();
        var itemId = await CreateItemAsync(client, units: [new { unit = "BAG", factor = 2.5m }]);

        using var one = await RecordAsync(client, itemId, new { type = "In", quantity = 1m, unit = "BAG" });
        using var two = await RecordAsync(client, itemId, new { type = "In", quantity = 2m, unit = "BAG" });

        Assert.Equal(HttpStatusCode.BadRequest, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        Assert.Equal(5m, await BalanceAsync(client, itemId));
    }

    [Fact]
    public async Task Block_OutBeyondTheBalance_Returns409_AndRecordsNothing()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client);
        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 2m });

        using var tooMuch = await RecordAsync(client, itemId, new { type = "Out", quantity = 3m });

        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal(2m, await BalanceAsync(client, itemId));
        Assert.Single((await client.GetFromJsonAsync<JsonElement>(Movements(itemId), Ct)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Block_ParallelOuts_NeverTakeTheBalanceBelowZero()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client);
        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 3m });

        // EN: Five issues of 1 against a balance of 3, at once: a read-then-write check would let more than 3 through.
        // TR: 3'lük bir bakiyeye karşı aynı anda beş adet 1'lik çıkış: önce okuyup sonra yazan bir kontrol 3'ten fazlasını geçirirdi.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            RecordAsync(client, itemId, new { type = "Out", quantity = 1m })));
        var statuses = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.Conflict));
        Assert.Equal(0m, await BalanceAsync(client, itemId));
    }

    [Theory]
    [InlineData("Allow", false)]
    [InlineData("Warn", true)]
    public async Task AllowAndWarn_ApplyTheOut_FlagIt_AndOnlyWarnWarns(string policy, bool warns)
    {
        using var client = await CreateProClientAsync(app, Ct);
        await SetPolicyAsync(client, policy);
        var itemId = await CreateItemAsync(client);

        using var issued = await RecordAsync(client, itemId, new { type = "Out", quantity = 3m });
        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 1m });

        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var body = await issued.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(body.GetProperty("movement").GetProperty("causedNegativeStock").GetBoolean());
        Assert.Equal(warns ? ["NegativeStock"] : [], body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));

        // EN: A receipt while still below zero improves things: never flagged. TR: Hâlâ eksideyken giriş durumu iyileştirir: asla işaretlenmez.
        var receipt = (await received.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("movement");
        Assert.False(receipt.GetProperty("causedNegativeStock").GetBoolean());
        Assert.Equal(-2m, await BalanceAsync(client, itemId));
    }

    [Fact]
    public async Task Roles_ViewerReadsTheHistory_ButOnlyWritersRecord()
    {
        var (ownerClient, _) = await CreateCompanyAsync(app, Ct);
        using var owner = ownerClient;
        using var upgrade = await UpgradeAsync(owner, Ct);
        upgrade.EnsureSuccessStatusCode();

        // EN: The upgrade answers with a token that already carries the Pro plan (ADR-006). TR: Yükseltme, Pro planı zaten taşıyan bir token'la cevap verir (ADR-006).
        Authorize(owner, (await upgrade.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);
        var itemId = await CreateItemAsync(owner);
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await SignInAsNewUserAsync(app, owner, "Member", Ct);

        using var viewerReads = await viewer.GetAsync(Movements(itemId), Ct);
        using var viewerRecords = await RecordAsync(viewer, itemId, new { type = "In", quantity = 1m });
        using var memberRecords = await RecordAsync(member, itemId, new { type = "In", quantity = 1m });

        Assert.Equal(HttpStatusCode.OK, viewerReads.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerRecords.StatusCode);
        Assert.Equal(HttpStatusCode.Created, memberRecords.StatusCode);
    }

    [Fact]
    public async Task AnotherCompanysOrADeletedItem_IsNotFound()
    {
        using var owner = await CreateProClientAsync(app, Ct);
        using var stranger = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(owner);
        var deletedId = await CreateItemAsync(owner);
        using var delete = await owner.DeleteAsync($"/inventory/items/{deletedId}", Ct);
        delete.EnsureSuccessStatusCode();

        using var strangerRecords = await RecordAsync(stranger, itemId, new { type = "In", quantity = 1m });
        using var strangerReads = await stranger.GetAsync(Movements(itemId), Ct);
        using var deletedRecords = await RecordAsync(owner, deletedId, new { type = "In", quantity = 1m });
        using var deletedReads = await owner.GetAsync(Movements(deletedId), Ct);

        Assert.Equal(HttpStatusCode.NotFound, strangerRecords.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerReads.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedRecords.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedReads.StatusCode);
        Assert.Equal(0m, await BalanceAsync(owner, itemId));
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

        using var gatewayResponse = await RecordAsync(viaGateway, anyItem, new { type = "In", quantity = 1m });
        using var directResponse = await direct.GetAsync(Movements(anyItem), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, gatewayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, directResponse.StatusCode);
    }

    /// <summary>
    /// EN: Address of an item's movements.
    /// TR: Bir kalemin hareketlerinin adresi.
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: The address. TR: Adres.</returns>
    private static string Movements(Guid itemId) => $"/inventory/items/{itemId}/movements";

    /// <summary>
    /// EN: Creates a stock item (base unit PCS, balance 0) with a SKU no other test uses, and returns its id.
    /// TR: Başka hiçbir testin kullanmadığı bir SKU ile bir stok kalemi (temel birim PCS, bakiye 0) oluşturur ve kimliğini döner.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="units">EN: Alternative units, or none. TR: Alternatif birimler veya hiçbiri.</param>
    /// <returns>EN: Item id. TR: Kalem kimliği.</returns>
    private static async Task<Guid> CreateItemAsync(HttpClient client, object[]? units = null)
    {
        var sku = $"MOV-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        using var response = await client.PostAsJsonAsync(
            "/inventory/items", new { sku, name = "Bolt", baseUnit = "PCS", units = units ?? [] }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// EN: Posts a movement.
    /// TR: Bir hareket gönderir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="body">EN: Movement body. TR: Hareket gövdesi.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private static Task<HttpResponseMessage> RecordAsync(HttpClient client, Guid itemId, object body) =>
        client.PostAsJsonAsync(Movements(itemId), body, Ct);

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
        $"{line.GetProperty("type").GetString()} {Trim(line.GetProperty("quantity").GetDecimal())} → {Trim(line.GetProperty("balanceAfter").GetDecimal())}";

    /// <summary>
    /// EN: A decimal without trailing zeros (columns return 7.000).
    /// TR: Sondaki sıfırlar olmadan bir ondalık (sütunlar 7.000 döner).
    /// </summary>
    /// <param name="value">EN: The value. TR: Değer.</param>
    /// <returns>EN: The text. TR: Metin.</returns>
    private static string Trim(decimal value) =>
        (value / 1.000000000000000000000000000000000m).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
