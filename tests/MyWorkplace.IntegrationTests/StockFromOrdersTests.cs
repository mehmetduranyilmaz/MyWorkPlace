using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.InventoryApi;
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
    /// <summary>EN: What the waits below wait for. TR: Aşağıdaki beklemelerin beklediği şey.</summary>
    private const string OrderReachesInventory = "the order to reach Inventory";

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlacedOrder_DecreasesStock_EvenBelowZero_AndFlagsIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, Ct, sku);

        var (_, number) = await PlaceLinesAsync(client, Ct, (sku, 2.5m));

        await Eventually.UntilAsync(async () => await BalanceAsync(client, itemId, Ct) == -2.5m, OrderReachesInventory, Ct);
        var movement = Assert.Single(await MovementsAsync(itemId));
        Assert.Equal((number, -2.5m, true), (movement.OrderNumber, movement.BalanceAfter, movement.Negative));
    }

    [Fact]
    public async Task OrdersForTheSameItemAtOnce_AllDecreaseIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, Ct, sku);

        // EN: Five orders placed at once for the same item: a lost update would leave the balance above −5.
        // TR: Aynı kalem için aynı anda verilen beş sipariş: kayıp bir güncelleme bakiyeyi −5'in üstünde bırakırdı.
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PlaceLinesAsync(client, Ct, (sku, 1m))));

        await Eventually.UntilAsync(async () => (await MovementsAsync(itemId)).Count == 5, OrderReachesInventory, Ct);
        Assert.Equal(-5m, await BalanceAsync(client, itemId, Ct));
        Assert.Equal(
            [-5m, -4m, -3m, -2m, -1m],
            (await MovementsAsync(itemId)).Select(m => m.BalanceAfter).Order());
    }

    [Fact]
    public async Task UnknownSku_IsSkipped_TheOtherLineApplies()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, Ct, sku);

        await PlaceLinesAsync(client, Ct, ("NO-SUCH-SKU", 5m), (sku.ToLowerInvariant(), 1m));

        // EN: Matched case-insensitively, like the SKU uniqueness rule. TR: SKU benzersizlik kuralı gibi harf duyarsız eşlenir.
        await Eventually.UntilAsync(async () => await BalanceAsync(client, itemId, Ct) == -1m, OrderReachesInventory, Ct);
        Assert.Single(await MovementsAsync(itemId));
    }

    [Fact]
    public async Task SameSkuInAnotherCompany_IsUntouched()
    {
        using var buyer = await CreateProClientAsync(app, Ct);
        using var other = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var buyerItem = await CreateItemAsync(buyer, Ct, sku);
        var otherItem = await CreateItemAsync(other, Ct, sku);

        await PlaceLinesAsync(buyer, Ct, (sku, 3m));

        await Eventually.UntilAsync(async () => await BalanceAsync(buyer, buyerItem, Ct) == -3m, OrderReachesInventory, Ct);
        Assert.Equal(0m, await BalanceAsync(other, otherItem, Ct));
    }

    [Fact]
    public async Task ItemWithMovements_KeepsItsBaseUnit_ButOtherFieldsMayChange()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, Ct, sku);
        await PlaceLinesAsync(client, Ct, (sku, 1m));
        await Eventually.UntilAsync(async () => (await MovementsAsync(itemId)).Count == 1, OrderReachesInventory, Ct);

        // EN: The balance −1 is in PCS; reading it as −1 KG would be wrong (ADR-019).
        // TR: −1 bakiyesi PCS cinsinden; onu −1 KG okumak yanlış olurdu (ADR-019).
        using var read = await client.GetAsync($"{Items}/{itemId}", Ct);
        var etag = read.Headers.ETag!.ToString();
        using var toKg = await client.PutWithIfMatchAsync(
            $"{Items}/{itemId}", new { sku, name = "Bolt", baseUnit = "KG" }, etag, Ct);
        using var rename = await client.PutWithIfMatchAsync(
            $"{Items}/{itemId}", new { sku, name = "Bolt M8", baseUnit = "pcs" }, etag, Ct);

        Assert.Equal(HttpStatusCode.Conflict, toKg.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task History_ShowsOrderAndManualMovementsTogether_NewestFirst()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, Ct, sku);
        var (_, number) = await PlaceLinesAsync(client, Ct, (sku, 2m));
        await Eventually.UntilAsync(async () => (await MovementsAsync(itemId)).Count == 1, OrderReachesInventory, Ct);

        using var received = await RecordAsync(client, itemId, new { type = "In", quantity = 5m }, Ct);
        var history = await HistoryAsync(client, itemId, Ct);

        Assert.Equal(HttpStatusCode.Created, received.StatusCode);
        Assert.Equal(
            [("Manual", (int?)null, "PCS"), ("Order", number, "PCS")],
            history.Select(m => (
                m.GetProperty("reason").GetString(),
                m.GetProperty("orderNumber").ValueKind == JsonValueKind.Null ? null : (int?)m.GetProperty("orderNumber").GetInt32(),
                m.GetProperty("unit").GetString())));
        Assert.Equal(3m, await BalanceAsync(client, itemId, Ct));
    }

    [Fact]
    public async Task CancelledOrder_ReturnsItsStock()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var sku = NewSku();
        var itemId = await CreateItemAsync(client, Ct, sku);
        var (orderId, number) = await PlaceLinesAsync(client, Ct, (sku, 2m), ("NO-SUCH-SKU", 1m));
        await Eventually.UntilAsync(async () => await BalanceAsync(client, itemId, Ct) == -2m, OrderReachesInventory, Ct);

        using var cancelled = await CancelAsync(client, orderId, await ETagOfAsync(client, orderId, Ct), Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        // EN: Through RabbitMQ: OrderCancelled reaches Inventory, which returns exactly what it issued (T-040).
        // TR: RabbitMQ üzerinden: OrderCancelled Inventory'ye ulaşır; Inventory çıkardığını birebir geri verir (T-040).
        await Eventually.UntilAsync(
            async () => await BalanceAsync(client, itemId, Ct) == 0m, "the cancellation to reach Inventory", Ct);
        var newest = (await HistoryAsync(client, itemId, Ct))[0];
        Assert.Equal(("In", "OrderCancelled", number), (
            newest.GetProperty("type").GetString(), newest.GetProperty("reason").GetString(), newest.GetProperty("orderNumber").GetInt32()));
    }

    [Fact]
    public async Task BasicCompanyOrder_ChangesNothing()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var tenantId = TenantOf(client);

        await PlaceLinesAsync(client, Ct, (NewSku(), 1m));

        // EN: Wait until Inventory has processed the event, so "nothing changed" isn't just "not yet".
        // TR: Inventory olayı işleyene kadar beklenir; böylece "hiçbir şey değişmedi", "henüz değil" demek olmaz.
        await Eventually.UntilAsync(
            async () => await CountAsync(
                "select count(*) from processed_events where tenant_id = @tenant and event_type = 'OrderPlaced'", tenantId) == 1,
            OrderReachesInventory,
            Ct);
        Assert.Equal(0, await CountAsync("select count(*) from stock_movements where tenant_id = @tenant", tenantId));
    }

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
}
