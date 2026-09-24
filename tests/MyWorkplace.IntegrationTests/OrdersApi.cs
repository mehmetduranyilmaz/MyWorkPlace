using System.Net.Http.Json;
using System.Text.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Client helpers for the Orders endpoints (T-014).
/// TR: Orders uç noktaları için istemci yardımcıları (T-014).
/// </summary>
internal static class OrdersApi
{
    /// <summary>
    /// EN: A valid two-line order form: 2.5 × 10.25 = 25.625 → 25.63 (half away from zero) and 1.5 × 3 = 4.50, total 30.13.
    /// TR: Geçerli, iki satırlı bir sipariş formu: 2,5 × 10,25 = 25,625 → 25,63 (yarım sıfırdan uzağa) ve 1,5 × 3 = 4,50; toplam 30,13.
    /// </summary>
    /// <param name="firstSku">EN: SKU of the first line. TR: İlk satırın stok kodu.</param>
    /// <returns>EN: The form. TR: Form.</returns>
    public static object TwoLines(string firstSku = "BOLT-1") => new
    {
        lines = new[]
        {
            new { sku = firstSku, name = "Bolt", quantity = 2.5m, unitPrice = 10.25m },
            new { sku = "NUT-1", name = "Nut", quantity = 1.5m, unitPrice = 3m },
        },
    };

    /// <summary>
    /// EN: Posts an order form.
    /// TR: Bir sipariş formu gönderir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="body">EN: Order form. TR: Sipariş formu.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, object body, CancellationToken ct) =>
        client.PostAsJsonAsync("/orders", body, ct);

    /// <summary>
    /// EN: Creates a draft and returns its id and ETag; fails the test otherwise.
    /// TR: Bir taslak oluşturur, kimliğini ve ETag'ini döner; aksi halde testi düşürür.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <param name="body">EN: Order form; two lines by default. TR: Sipariş formu; varsayılan iki satır.</param>
    /// <returns>EN: Id and ETag. TR: Kimlik ve ETag.</returns>
    public static async Task<(Guid Id, string ETag)> CreateDraftAsync(HttpClient client, CancellationToken ct, object? body = null)
    {
        using var response = await CreateAsync(client, body ?? TwoLines(), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid(),
            response.Headers.ETag!.ToString());
    }

    /// <summary>
    /// EN: Places an order with an optional If-Match.
    /// TR: Bir siparişi isteğe bağlı If-Match ile verir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="id">EN: Order id. TR: Sipariş kimliği.</param>
    /// <param name="ifMatch">EN: ETag or null. TR: ETag veya null.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid id, string? ifMatch, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/orders/{id}/place");
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, ct);
    }

    /// <summary>
    /// EN: Creates and places an order; returns its id and number.
    /// TR: Bir sipariş oluşturur ve verir; kimliğini ve numarasını döner.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Id and number. TR: Kimlik ve numara.</returns>
    public static async Task<(Guid Id, int Number)> CreatePlacedAsync(HttpClient client, CancellationToken ct)
    {
        var (id, etag) = await CreateDraftAsync(client, ct);
        using var placed = await PlaceAsync(client, id, etag, ct);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        return (id, (await placed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("number").GetInt32());
    }
}
