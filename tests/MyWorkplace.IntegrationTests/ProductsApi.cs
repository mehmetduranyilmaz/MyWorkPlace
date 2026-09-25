using System.Net.Http.Json;
using System.Text.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Client helpers for the Products endpoints (T-013).
/// TR: Products uç noktaları için istemci yardımcıları (T-013).
/// </summary>
internal static class ProductsApi
{
    /// <summary>
    /// EN: Creates a product and returns the response (caller disposes it).
    /// TR: Bir ürün oluşturur ve cevabı döner (çağıran dispose eder).
    /// </summary>
    /// <param name="client">EN: Signed-in gateway client. TR: Giriş yapmış gateway istemcisi.</param>
    /// <param name="body">EN: Product fields. TR: Ürün alanları.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, object body, CancellationToken ct) =>
        client.PostAsJsonAsync("/products", body, ct);

    /// <summary>
    /// EN: Creates a product and returns its id and ETag; fails the test if creation fails.
    /// TR: Bir ürün oluşturur, kimliğini ve ETag'ini döner; oluşturma başarısızsa testi düşürür.
    /// </summary>
    /// <param name="client">EN: Signed-in gateway client. TR: Giriş yapmış gateway istemcisi.</param>
    /// <param name="name">EN: Product name. TR: Ürün adı.</param>
    /// <param name="sku">EN: SKU; a unique one when null. TR: SKU; null ise benzersiz bir tane.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Id and ETag. TR: Kimlik ve ETag.</returns>
    public static async Task<(Guid Id, string ETag)> CreateNewAsync(
        HttpClient client, string name, string? sku, CancellationToken ct)
    {
        using var response = await CreateAsync(client, new { sku = sku ?? UniqueSku(), name, price = 9.99m }, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        return (id, response.Headers.ETag!.ToString());
    }

    /// <summary>
    /// EN: Sends a full update, with <paramref name="ifMatch"/> as the If-Match header when given.
    /// TR: Tam güncelleme gönderir; <paramref name="ifMatch"/> verilmişse If-Match başlığı olarak kullanılır.
    /// </summary>
    /// <param name="client">EN: Signed-in gateway client. TR: Giriş yapmış gateway istemcisi.</param>
    /// <param name="id">EN: Product id. TR: Ürün kimliği.</param>
    /// <param name="body">EN: Product fields. TR: Ürün alanları.</param>
    /// <param name="ifMatch">EN: ETag to send, or null. TR: Gönderilecek ETag veya null.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client, Guid id, object body, string? ifMatch, CancellationToken ct) =>
        client.PutWithIfMatchAsync($"/products/{id}", body, ifMatch, ct);

    /// <summary>
    /// EN: A SKU no other test uses.
    /// TR: Başka hiçbir testin kullanmadığı bir SKU.
    /// </summary>
    /// <returns>EN: The SKU. TR: SKU.</returns>
    public static string UniqueSku() => $"P-{Guid.NewGuid():N}"[..20];
}
