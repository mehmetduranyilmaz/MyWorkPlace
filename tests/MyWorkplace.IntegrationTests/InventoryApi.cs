using System.Net.Http.Json;
using System.Text.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Client helpers for the Inventory endpoints (T-047): stock items, their balance and their movements.
/// TR: Inventory uç noktaları için istemci yardımcıları (T-047): stok kalemleri, bakiyeleri ve hareketleri.
/// </summary>
internal static class InventoryApi
{
    /// <summary>EN: Stock items address. TR: Stok kalemleri adresi.</summary>
    public const string Items = "/inventory/items";

    /// <summary>
    /// EN: A SKU no other test uses.
    /// TR: Başka hiçbir testin kullanmadığı bir SKU.
    /// </summary>
    /// <returns>EN: The SKU. TR: SKU.</returns>
    public static string NewSku() => $"SKU-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

    /// <summary>
    /// EN: Address of an item's movements.
    /// TR: Bir kalemin hareketlerinin adresi.
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <returns>EN: The address. TR: Adres.</returns>
    public static string Movements(Guid itemId) => $"{Items}/{itemId}/movements";

    /// <summary>
    /// EN: Creates a stock item (base unit PCS, balance 0) and returns its id; fails the test otherwise.
    /// TR: Bir stok kalemi (temel birim PCS, bakiye 0) oluşturur ve kimliğini döner; aksi halde testi düşürür.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <param name="sku">EN: SKU; a new one by default. TR: SKU; varsayılan olarak yeni bir tane.</param>
    /// <param name="name">EN: Item name. TR: Kalem adı.</param>
    /// <param name="units">EN: Alternative units, or none. TR: Alternatif birimler veya hiçbiri.</param>
    /// <returns>EN: Item id. TR: Kalem kimliği.</returns>
    public static async Task<Guid> CreateItemAsync(
        HttpClient client, CancellationToken ct, string? sku = null, string name = "Item", object[]? units = null)
    {
        using var response = await client.PostAsJsonAsync(
            Items, new { sku = sku ?? NewSku(), name, baseUnit = "PCS", units = units ?? [] }, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// EN: Reads an item's balance in its base unit.
    /// TR: Bir kalemin bakiyesini temel biriminde okur.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The balance. TR: Bakiye.</returns>
    public static async Task<decimal> BalanceAsync(HttpClient client, Guid itemId, CancellationToken ct) =>
        (await client.GetFromJsonAsync<JsonElement>($"{Items}/{itemId}", ct)).GetProperty("quantity").GetDecimal();

    /// <summary>
    /// EN: Posts a manual movement.
    /// TR: Elle bir hareket gönderir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="body">EN: Movement body. TR: Hareket gövdesi.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> RecordAsync(HttpClient client, Guid itemId, object body, CancellationToken ct) =>
        client.PostAsJsonAsync(Movements(itemId), body, ct);

    /// <summary>
    /// EN: Reads an item's movement history, newest first.
    /// TR: Bir kalemin hareket geçmişini okur, en yeni önce.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The movements. TR: Hareketler.</returns>
    public static async Task<List<JsonElement>> HistoryAsync(HttpClient client, Guid itemId, CancellationToken ct) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Movements(itemId), ct)).GetProperty("items").EnumerateArray()];
}
