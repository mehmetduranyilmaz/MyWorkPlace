using System.Net.Http.Json;
using System.Text.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Client helpers for the Customers endpoints, used by the reference-module tests.
/// TR: Referans modül testlerinin kullandığı, Customers uç noktaları için istemci yardımcıları.
/// </summary>
internal static class CustomersApi
{
    /// <summary>
    /// EN: Creates a customer and returns the response (caller disposes it).
    /// TR: Bir müşteri oluşturur ve cevabı döner (çağıran dispose eder).
    /// </summary>
    /// <param name="client">EN: Signed-in gateway client. TR: Giriş yapmış gateway istemcisi.</param>
    /// <param name="body">EN: Customer fields. TR: Müşteri alanları.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, object body, CancellationToken ct) =>
        client.PostAsJsonAsync("/customers", body, ct);

    /// <summary>
    /// EN: Creates a customer and returns its id and ETag; fails the test if creation fails.
    /// TR: Bir müşteri oluşturur, kimliğini ve ETag'ini döner; oluşturma başarısızsa testi düşürür.
    /// </summary>
    /// <param name="client">EN: Signed-in gateway client. TR: Giriş yapmış gateway istemcisi.</param>
    /// <param name="name">EN: Customer name. TR: Müşteri adı.</param>
    /// <param name="email">EN: Optional email. TR: İsteğe bağlı e-posta.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Id and ETag. TR: Kimlik ve ETag.</returns>
    public static async Task<(Guid Id, string ETag)> CreateNewAsync(
        HttpClient client, string name, string? email, CancellationToken ct)
    {
        using var response = await CreateAsync(client, new { name, email }, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        return (id, response.Headers.ETag!.ToString());
    }

    /// <summary>
    /// EN: Sends a full update, with <paramref name="ifMatch"/> as the If-Match header when given.
    /// TR: Tam güncelleme gönderir; <paramref name="ifMatch"/> verilmişse If-Match başlığı olarak kullanılır.
    /// </summary>
    /// <param name="client">EN: Signed-in gateway client. TR: Giriş yapmış gateway istemcisi.</param>
    /// <param name="id">EN: Customer id. TR: Müşteri kimliği.</param>
    /// <param name="body">EN: Customer fields. TR: Müşteri alanları.</param>
    /// <param name="ifMatch">EN: ETag to send, or null. TR: Gönderilecek ETag veya null.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client, Guid id, object body, string? ifMatch, CancellationToken ct) =>
        client.PutWithIfMatchAsync($"/customers/{id}", body, ifMatch, ct);
}
