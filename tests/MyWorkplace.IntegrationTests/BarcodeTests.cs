using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.UsersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Barcodes (T-055, ADR-019) through the gateway: added per item unit, unique within a company — enforced by the
///     database even for parallel adds — and looked up with everything a till needs.
/// TR: Gateway üzerinden barkodlar (T-055, ADR-019): kalem birimine eklenir, firma içinde benzersizdir — paralel eklemelerde bile veritabanınca
///     uygulanır — ve bir kasanın ihtiyacı olan her şeyle sorgulanır.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class BarcodeTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ScanningABoxBarcode_ReturnsTheItemUnitFactorAndStock()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, "Water", units: [new { unit = "BOX", factor = 24m }]);
        using var received = await client.PostAsJsonAsync($"/inventory/items/{itemId}/movements", new { type = "In", quantity = 2m, unit = "BOX" }, Ct);
        received.EnsureSuccessStatusCode();
        var code = NewCode();

        using var added = await AddAsync(client, itemId, $" {code} ", "box");
        var scan = await client.GetFromJsonAsync<JsonElement>($"/inventory/barcodes/{code}", Ct);
        var item = await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{itemId}", Ct);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal($"/inventory/barcodes/{code}", added.Headers.Location?.OriginalString);
        Assert.Equal(
            (itemId, "Water", "BOX", 24m, "PCS", 48m),
            (scan.GetProperty("itemId").GetGuid(), scan.GetProperty("name").GetString(), scan.GetProperty("unit").GetString(),
                scan.GetProperty("factor").GetDecimal(), scan.GetProperty("baseUnit").GetString(), scan.GetProperty("quantity").GetDecimal()));
        Assert.Equal([$"{code}:BOX"], item.GetProperty("barcodes").EnumerateArray()
            .Select(b => $"{b.GetProperty("code").GetString()}:{b.GetProperty("unit").GetString()}"));
    }

    [Theory]
    [InlineData("HAS SPACE", "PCS", "Code")]
    [InlineData("ÜMLAUT", "PCS", "Code")]
    [InlineData("51", "PCS", "Code")]
    [InlineData("OK-CODE", "KG", "Unit")]
    public async Task InvalidBarcode_Returns400WithFieldError(string code, string unit, string invalidField)
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client);

        using var response = await AddAsync(client, itemId, int.TryParse(code, out var length) ? new string('7', length) : code, unit);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(invalidField, errors.EnumerateObject().Select(e => e.Name));
    }

    [Fact]
    public async Task Code_IsUniqueWithinTheCompany_CaseSensitive_ButAnotherCompanyMayUseIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        using var otherCompany = await CreateProClientAsync(app, Ct);
        var first = await CreateItemAsync(client);
        var second = await CreateItemAsync(client);
        var elsewhere = await CreateItemAsync(otherCompany);
        var code = "abc-" + NewCode();

        using var added = await AddAsync(client, first, code, "PCS");
        using var sameCodeOtherItem = await AddAsync(client, second, code, "PCS");
        using var otherCase = await AddAsync(client, second, code.ToUpperInvariant(), "PCS");
        using var otherCompanyAdds = await AddAsync(otherCompany, elsewhere, code, "PCS");

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameCodeOtherItem.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherCase.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherCompanyAdds.StatusCode);
    }

    [Fact]
    public async Task ParallelAddsOfTheSameCode_ExactlyOneWins()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var items = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CreateItemAsync(client)));
        var code = NewCode();

        // EN: Five items claim the same code at once: the check alone could let several through; the index lets one.
        // TR: Beş kalem aynı kodu aynı anda ister: kontrol tek başına birkaçını geçirebilirdi; index sadece birini geçirir.
        var responses = await Task.WhenAll(items.Select(id => AddAsync(client, id, code, "PCS")));
        var statuses = responses.Select(r => r.StatusCode).ToList();
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(4, statuses.Count(s => s == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task RemovedBarcode_IsGone_AndItsCodeIsFree()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var first = await CreateItemAsync(client);
        var second = await CreateItemAsync(client);
        var code = NewCode();
        using var added = await AddAsync(client, first, code, "PCS");

        using var removed = await client.DeleteAsync($"/inventory/items/{first}/barcodes/{code}", Ct);
        using var removedAgain = await client.DeleteAsync($"/inventory/items/{first}/barcodes/{code}", Ct);
        using var scan = await client.GetAsync($"/inventory/barcodes/{code}", Ct);
        using var reused = await AddAsync(client, second, code, "PCS");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removedAgain.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scan.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
    }

    [Fact]
    public async Task UnitWithBarcodes_CantLeaveTheItem_ButOtherChangesKeepThem()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(client, "Juice", units: [new { unit = "BOX", factor = 12m }]);
        var code = NewCode();
        using var added = await AddAsync(client, itemId, code, "BOX");
        using var read = await client.GetAsync($"/inventory/items/{itemId}", Ct);
        var sku = (await read.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("sku").GetString();

        using var withoutBox = await client.PutWithIfMatchAsync(
            $"/inventory/items/{itemId}", new { sku, name = "Juice", baseUnit = "PCS" }, read.Headers.ETag!.ToString(), Ct);
        using var renamed = await client.PutWithIfMatchAsync(
            $"/inventory/items/{itemId}",
            new { sku, name = "Orange juice", baseUnit = "PCS", units = new[] { new { unit = "BOX", factor = 12m } } },
            read.Headers.ETag!.ToString(),
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, withoutBox.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Single((await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("barcodes").EnumerateArray());
    }

    [Fact]
    public async Task DeletedItem_TakesItsBarcodes_AndFreesTheirCodes()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var deleted = await CreateItemAsync(client);
        var other = await CreateItemAsync(client);
        var code = NewCode();
        using var added = await AddAsync(client, deleted, code, "PCS");

        using var delete = await client.DeleteAsync($"/inventory/items/{deleted}", Ct);
        using var scan = await client.GetAsync($"/inventory/barcodes/{code}", Ct);
        using var reused = await AddAsync(client, other, code, "PCS");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scan.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
    }

    [Fact]
    public async Task AnotherCompanysBarcode_IsNotFound()
    {
        using var owner = await CreateProClientAsync(app, Ct);
        using var stranger = await CreateProClientAsync(app, Ct);
        var itemId = await CreateItemAsync(owner);
        var code = NewCode();
        using var added = await AddAsync(owner, itemId, code, "PCS");

        using var scan = await stranger.GetAsync($"/inventory/barcodes/{code}", Ct);
        using var add = await AddAsync(stranger, itemId, NewCode(), "PCS");
        using var remove = await stranger.DeleteAsync($"/inventory/items/{itemId}/barcodes/{code}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, scan.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
    }

    [Fact]
    public async Task Roles_ViewerScans_MemberAdds_OnlyAdminRemoves()
    {
        var (ownerClient, _) = await CreateCompanyAsync(app, Ct);
        using var owner = ownerClient;
        using var upgrade = await UpgradeAsync(owner, Ct);
        upgrade.EnsureSuccessStatusCode();
        Authorize(owner, (await upgrade.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);
        var itemId = await CreateItemAsync(owner);
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await SignInAsNewUserAsync(app, owner, "Member", Ct);
        using var admin = await SignInAsNewUserAsync(app, owner, "Admin", Ct);
        var code = NewCode();

        using var viewerAdds = await AddAsync(viewer, itemId, code, "PCS");
        using var memberAdds = await AddAsync(member, itemId, code, "PCS");
        using var viewerScans = await viewer.GetAsync($"/inventory/barcodes/{code}", Ct);
        using var memberRemoves = await member.DeleteAsync($"/inventory/items/{itemId}/barcodes/{code}", Ct);
        using var adminRemoves = await admin.DeleteAsync($"/inventory/items/{itemId}/barcodes/{code}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, viewerAdds.StatusCode);
        Assert.Equal(HttpStatusCode.Created, memberAdds.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viewerScans.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberRemoves.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, adminRemoves.StatusCode);
    }

    [Fact]
    public async Task BasicCompany_Gets403_ThroughGatewayAndDirectly()
    {
        var basicToken = await GetAccessTokenAsync(app, Ct);
        using var viaGateway = app.CreateGatewayClient();
        using var direct = app.CreateDirectServiceClient("inventory");
        Authorize(viaGateway, basicToken);
        Authorize(direct, basicToken);

        using var gatewayResponse = await viaGateway.GetAsync("/inventory/barcodes/ANY-1", Ct);
        using var directResponse = await direct.GetAsync("/inventory/barcodes/ANY-1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, gatewayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, directResponse.StatusCode);
    }

    /// <summary>
    /// EN: A code no other test uses.
    /// TR: Başka hiçbir testin kullanmadığı bir kod.
    /// </summary>
    /// <returns>EN: The code. TR: Kod.</returns>
    private static string NewCode() => $"{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// EN: Creates a stock item (base unit PCS) with a SKU no other test uses, and returns its id.
    /// TR: Başka hiçbir testin kullanmadığı bir SKU ile bir stok kalemi (temel birim PCS) oluşturur ve kimliğini döner.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="name">EN: Item name. TR: Kalem adı.</param>
    /// <param name="units">EN: Alternative units, or none. TR: Alternatif birimler veya hiçbiri.</param>
    /// <returns>EN: Item id. TR: Kalem kimliği.</returns>
    private static async Task<Guid> CreateItemAsync(HttpClient client, string name = "Item", object[]? units = null)
    {
        var sku = $"BAR-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        using var response = await client.PostAsJsonAsync(
            "/inventory/items", new { sku, name, baseUnit = "PCS", units = units ?? [] }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// EN: Posts a barcode for an item unit.
    /// TR: Bir kalem birimi için barkod gönderir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="code">EN: Code. TR: Kod.</param>
    /// <param name="unit">EN: Unit. TR: Birim.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid itemId, string code, string unit) =>
        client.PostAsJsonAsync($"/inventory/items/{itemId}/barcodes", new { code, unit }, Ct);
}
