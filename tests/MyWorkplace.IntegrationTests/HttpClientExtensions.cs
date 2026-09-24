using System.Net.Http.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: HTTP helpers shared by every module's tests (ADR-016 / ADR-017 conventions).
/// TR: Her modülün testlerinin paylaştığı HTTP yardımcıları (ADR-016 / ADR-017 kuralları).
/// </summary>
internal static class HttpClientExtensions
{
    /// <summary>
    /// EN: Sends a full update (PUT), with <paramref name="ifMatch"/> as the If-Match header when given.
    /// TR: Tam güncelleme (PUT) gönderir; <paramref name="ifMatch"/> verilmişse If-Match başlığı olarak kullanılır.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="url">EN: Resource address. TR: Kaynak adresi.</param>
    /// <param name="body">EN: Resource fields. TR: Kaynak alanları.</param>
    /// <param name="ifMatch">EN: ETag to send, or null. TR: Gönderilecek ETag veya null.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static Task<HttpResponseMessage> PutWithIfMatchAsync(
        this HttpClient client, string url, object body, string? ifMatch, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, ct);
    }
}
