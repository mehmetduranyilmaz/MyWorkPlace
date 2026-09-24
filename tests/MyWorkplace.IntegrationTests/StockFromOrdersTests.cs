using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.OrdersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Placing an order decreases stock in Inventory — end to end, through the gateway and RabbitMQ, with no call between
///     the two services (T-016, ADR-020). The effect is asynchronous, so the tests poll for it.
/// TR: Sipariş vermek Inventory'de stoğu düşürür — uçtan uca, gateway ve RabbitMQ üzerinden, iki servis arasında hiçbir çağrı
///     olmadan (T-016, ADR-020). Etki asenkron olduğu için testler onu yoklayarak bekler.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class StockFromOrdersTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlacedOrder_DecreasesStock_EvenBelowZero_AndFlagsIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, sku);

        var (_, number) = await PlaceAsync(client, (sku, 2.5m));

        await WaitUntilAsync(async () => await QuantityAsync(client, itemId) == -2.5m);
        var movement = Assert.Single(await MovementsAsync(itemId));
        Assert.Equal((number, -2.5m, true), (movement.OrderNumber, movement.BalanceAfter, movement.Negative));
    }

    [Fact]
    public async Task OrdersForTheSameItemAtOnce_AllDecreaseIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, sku);

        // EN: Five orders placed at once for the same item: a lost update would leave the balance above −5.
        // TR: Aynı kalem için aynı anda verilen beş sipariş: kayıp bir güncelleme bakiyeyi −5'in üstünde bırakırdı.
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PlaceAsync(client, (sku, 1m))));

        await WaitUntilAsync(async () => (await MovementsAsync(itemId)).Count == 5);
        Assert.Equal(-5m, await QuantityAsync(client, itemId));
        Assert.Equal(
            [-5m, -4m, -3m, -2m, -1m],
            (await MovementsAsync(itemId)).Select(m => m.BalanceAfter).Order());
    }

    [Fact]
    public async Task UnknownSku_IsSkipped_TheOtherLineApplies()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, sku);

        await PlaceAsync(client, ("NO-SUCH-SKU", 5m), (sku.ToLowerInvariant(), 1m));

        // EN: Matched case-insensitively, like the SKU uniqueness rule. TR: SKU benzersizlik kuralı gibi harf duyarsız eşlenir.
        await WaitUntilAsync(async () => await QuantityAsync(client, itemId) == -1m);
        Assert.Single(await MovementsAsync(itemId));
    }

    [Fact]
    public async Task SameSkuInAnotherCompany_IsUntouched()
    {
        using var buyer = await CreateProClientAsync(app, Ct);
        using var other = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var buyerItem = await CreateItemAsync(buyer, sku);
        var otherItem = await CreateItemAsync(other, sku);

        await PlaceAsync(buyer, (sku, 3m));

        await WaitUntilAsync(async () => await QuantityAsync(buyer, buyerItem) == -3m);
        Assert.Equal(0m, await QuantityAsync(other, otherItem));
    }

    [Fact]
    public async Task BasicCompanyOrder_ChangesNothing()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var tenantId = TenantOf(client);

        await PlaceAsync(client, (NewSku(), 1m));

        // EN: Wait until Inventory has processed the event, so "nothing changed" isn't just "not yet".
        // TR: Inventory olayı işleyene kadar beklenir; böylece "hiçbir şey değişmedi", "henüz değil" demek olmaz.
        await WaitUntilAsync(async () => await CountAsync(
            "select count(*) from processed_events where tenant_id = @tenant and event_type = 'OrderPlaced'", tenantId) == 1);
        Assert.Equal(0, await CountAsync("select count(*) from stock_movements where tenant_id = @tenant", tenantId));
    }

    /// <summary>
    /// EN: A SKU no other test uses.
    /// TR: Başka hiçbir testin kullanmadığı bir SKU.
    /// </summary>
    /// <returns>EN: The SKU. TR: SKU.</returns>
    private static string NewSku() => $"SKU-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

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
    /// EN: Creates and places an order with the given lines.
    /// TR: Verilen satırlarla bir sipariş oluşturur ve verir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="lines">EN: SKU and quantity per line. TR: Satır başına SKU ve miktar.</param>
    /// <returns>EN: Order id and number. TR: Sipariş kimliği ve numarası.</returns>
    private static async Task<(Guid Id, int Number)> PlaceAsync(HttpClient client, params (string Sku, decimal Quantity)[] lines)
    {
        var (id, etag) = await CreateDraftAsync(client, Ct, new
        {
            lines = lines.Select(l => new { sku = l.Sku, name = "Item", quantity = l.Quantity, unitPrice = 1m }).ToArray(),
        });
        using var placed = await OrdersApi.PlaceAsync(client, id, etag, Ct);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        return (id, (await placed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("number").GetInt32());
    }

    /// <summary>
    /// EN: Reads an item's balance through the gateway.
    /// TR: Bir kalemin bakiyesini gateway üzerinden okur.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: The balance. TR: Bakiye.</returns>
    private static async Task<decimal> QuantityAsync(HttpClient client, Guid itemId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/inventory/items/{itemId}", Ct)).GetProperty("quantity").GetDecimal();

    /// <summary>
    /// EN: An item's movements, read from inventory-db (their API arrives with T-030).
    /// TR: Bir kalemin hareketleri; inventory-db'den okunur (API'leri T-030 ile gelir).
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: Order number, balance after and negative flag per movement. TR: Hareket başına sipariş numarası, sonraki bakiye ve eksi işareti.</returns>
    private async Task<List<(int? OrderNumber, decimal BalanceAfter, bool Negative)>> MovementsAsync(Guid itemId)
    {
        await using var connection = await OpenInventoryDbAsync();
        await using var command = new NpgsqlCommand(
            "select order_number, balance_after, caused_negative_stock from stock_movements where stock_item_id = @item",
            connection);
        command.Parameters.AddWithValue("item", itemId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var movements = new List<(int?, decimal, bool)>();
        while (await reader.ReadAsync(Ct))
        {
            movements.Add((reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.GetDecimal(1), reader.GetBoolean(2)));
        }

        return movements;
    }

    /// <summary>
    /// EN: Runs a count query on inventory-db with a @tenant parameter.
    /// TR: inventory-db üzerinde @tenant parametreli bir sayım sorgusu çalıştırır.
    /// </summary>
    /// <param name="sql">EN: The query. TR: Sorgu.</param>
    /// <param name="tenantId">EN: Tenant. TR: Firma.</param>
    /// <returns>EN: The count. TR: Sayı.</returns>
    private async Task<long> CountAsync(string sql, Guid tenantId)
    {
        await using var connection = await OpenInventoryDbAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>
    /// EN: Opens a connection to inventory-db.
    /// TR: inventory-db'ye bir bağlantı açar.
    /// </summary>
    /// <returns>EN: An open connection. TR: Açık bir bağlantı.</returns>
    private async Task<NpgsqlConnection> OpenInventoryDbAsync()
    {
        var connection = new NpgsqlConnection(await app.App.GetConnectionStringAsync("inventory-db", Ct));
        await connection.OpenAsync(Ct);
        return connection;
    }

    /// <summary>
    /// EN: The tenant id in the token a client sends.
    /// TR: Bir istemcinin gönderdiği token'daki firma kimliği.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <returns>EN: Tenant id. TR: Firma kimliği.</returns>
    private static Guid TenantOf(HttpClient client) =>
        Guid.Parse(new JsonWebTokenHandler()
            .ReadJsonWebToken(client.DefaultRequestHeaders.Authorization!.Parameter)
            .GetClaim("tenant_id").Value);

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
            Assert.True(DateTime.UtcNow < deadline, "The order did not reach Inventory in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        }
    }
}
