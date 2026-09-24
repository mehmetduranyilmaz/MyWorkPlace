using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Stock items (T-010) through the gateway with a Pro tenant — the reference module's behavior, copied.
/// TR: Pro bir firmayla gateway üzerinden stok kalemleri (T-010) — referans modülün davranışı, kopyalanmış haliyle.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class StockItemTests(AppFixture app)
{
    /// <summary>EN: Items address. TR: Kalemler adresi.</summary>
    private const string Items = "/inventory/items";

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_ValidItem_Returns201WithZeroBalanceLocationAndETag()
    {
        using var client = await IdentityApi.CreateProClientAsync(app, Ct);

        using var response = await client.PostAsJsonAsync(Items, new { sku = " sku-001 ", name = "Water 0.5 L", baseUnit = "PCS" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal($"{Items}/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("sku-001", body.GetProperty("sku").GetString());
        Assert.Equal(0m, body.GetProperty("quantity").GetDecimal());
    }

    [Theory]
    [InlineData(null, "Water", "PCS", "sku")]
    [InlineData("51", "Water", "PCS", "sku")]
    [InlineData("SKU-1", null, "PCS", "name")]
    [InlineData("SKU-1", "Water", "BARREL", "baseUnit")]
    public async Task Create_InvalidField_Returns400WithFieldError(string? sku, string? name, string? baseUnit, string invalidField)
    {
        using var client = await IdentityApi.CreateProClientAsync(app, Ct);

        using var response = await client.PostAsJsonAsync(
            Items, new { sku = int.TryParse(sku, out var length) ? new string('x', length) : sku, name, baseUnit }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => string.Equals(e.Name, invalidField, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetUpdateDelete_FollowTheReferenceModule()
    {
        using var client = await IdentityApi.CreateProClientAsync(app, Ct);
        var (id, etag) = await CreateAsync(client, "SKU-GUD", "Before");

        using var get = await client.GetAsync($"{Items}/{id}", Ct);
        using var noPrecondition = await client.PutWithIfMatchAsync($"{Items}/{id}", Body("SKU-GUD", "After"), null, Ct);
        using var update = await client.PutWithIfMatchAsync($"{Items}/{id}", Body("SKU-GUD", "After"), etag, Ct);
        using var stale = await client.PutWithIfMatchAsync($"{Items}/{id}", Body("SKU-GUD", "Stale"), etag, Ct);
        using var delete = await client.DeleteAsync($"{Items}/{id}", Ct);
        using var afterDelete = await client.GetAsync($"{Items}/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(etag, get.Headers.ETag?.ToString());
        Assert.Equal((HttpStatusCode)428, noPrecondition.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Create_SameSkuAnyCase_Returns409_ButAnotherTenantMayUseIt()
    {
        using var client = await IdentityApi.CreateProClientAsync(app, Ct);
        using var otherTenant = await IdentityApi.CreateProClientAsync(app, Ct);
        await CreateAsync(client, "dup-sku", "First");

        using var duplicate = await client.PostAsJsonAsync(Items, Body("DUP-SKU", "Second"), Ct);
        using var elsewhere = await otherTenant.PostAsJsonAsync(Items, Body("DUP-SKU", "Other company"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task AnotherTenant_CannotGetUpdateOrDelete_Gets404()
    {
        using var owner = await IdentityApi.CreateProClientAsync(app, Ct);
        using var stranger = await IdentityApi.CreateProClientAsync(app, Ct);
        var (id, etag) = await CreateAsync(owner, "PRIVATE-1", "Private");

        using var get = await stranger.GetAsync($"{Items}/{id}", Ct);
        using var update = await stranger.PutWithIfMatchAsync($"{Items}/{id}", Body("PRIVATE-1", "Hijacked"), etag, Ct);
        using var delete = await stranger.DeleteAsync($"{Items}/{id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_ItemWithStock_Returns409()
    {
        using var client = await IdentityApi.CreateProClientAsync(app, Ct);
        var (id, _) = await CreateAsync(client, "STOCKED-1", "Has stock");

        // EN: Movements arrive in T-030; until then the balance is set directly in the database for this test.
        // TR: Hareketler T-030'da gelecek; o zamana kadar bu test için bakiye doğrudan veritabanında verilir.
        var connectionString = await app.App.GetConnectionStringAsync("inventory-db", Ct);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("update stock_items set quantity = 5 where id = @id", connection);
            command.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(Ct));
        }

        using var delete = await client.DeleteAsync($"{Items}/{id}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task List_IsSortedBySku_AndSearchesSkuAndName()
    {
        using var client = await IdentityApi.CreateProClientAsync(app, Ct);
        await CreateAsync(client, "C-300", "Cable");
        await CreateAsync(client, "A-100", "Apple juice");
        await CreateAsync(client, "B-200", "Battery");

        var all = await client.GetFromJsonAsync<JsonElement>(Items, Ct);
        var search = await client.GetFromJsonAsync<JsonElement>($"{Items}?search=batt", Ct);

        Assert.Equal(["A-100", "B-200", "C-300"], Skus(all));
        Assert.Equal(["B-200"], Skus(search));
    }

    [Fact]
    public async Task BasicTenant_Gets403_ThroughGatewayAndDirectly()
    {
        var basicToken = await IdentityApi.GetAccessTokenAsync(app, Ct);
        using var viaGateway = app.CreateGatewayClient();
        using var direct = app.CreateDirectServiceClient("inventory");
        IdentityApi.Authorize(viaGateway, basicToken);
        IdentityApi.Authorize(direct, basicToken);

        using var gatewayResponse = await viaGateway.PostAsJsonAsync(Items, Body("NOPE-1", "Basic"), Ct);
        using var directResponse = await direct.PostAsJsonAsync(Items, Body("NOPE-1", "Basic"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, gatewayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, directResponse.StatusCode);
    }

    /// <summary>
    /// EN: Creates an item (base unit PCS) and returns its id and ETag.
    /// TR: Bir kalem oluşturur (temel birim PCS), kimliğini ve ETag'ini döner.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <returns>EN: Id and ETag. TR: Kimlik ve ETag.</returns>
    private static async Task<(Guid Id, string ETag)> CreateAsync(HttpClient client, string sku, string name)
    {
        using var response = await client.PostAsJsonAsync(Items, Body(sku, name), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        return (id, response.Headers.ETag!.ToString());
    }

    /// <summary>
    /// EN: Request body with the PCS base unit.
    /// TR: PCS temel birimli istek gövdesi.
    /// </summary>
    /// <param name="sku">EN: SKU. TR: SKU.</param>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <returns>EN: The body. TR: Gövde.</returns>
    private static object Body(string sku, string name) => new { sku, name, baseUnit = "PCS" };

    /// <summary>
    /// EN: SKUs of the items on a page, in order.
    /// TR: Bir sayfadaki kalemlerin SKU'ları, sırasıyla.
    /// </summary>
    /// <param name="page">EN: Page body. TR: Sayfa gövdesi.</param>
    /// <returns>EN: The SKUs. TR: SKU'lar.</returns>
    private static List<string> Skus(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sku").GetString()!)];
}
