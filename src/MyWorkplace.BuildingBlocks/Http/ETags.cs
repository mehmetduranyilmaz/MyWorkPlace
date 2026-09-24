using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyWorkplace.BuildingBlocks.Http;

/// <summary>
/// EN: Optimistic concurrency over HTTP (ADR-017). The row version (<c>xmin</c>) travels as a strong ETag;
///     updates must send it back in <c>If-Match</c>.
/// TR: HTTP üzerinden iyimser eşzamanlılık (ADR-017). Satır sürümü (<c>xmin</c>) güçlü bir ETag olarak taşınır;
///     güncellemeler bunu <c>If-Match</c> ile geri göndermek zorundadır.
/// </summary>
public static class ETags
{
    /// <summary>
    /// EN: Formats a row version as a strong ETag, e.g. <c>"1234"</c> (the quotes are part of the value).
    /// TR: Bir satır sürümünü güçlü bir ETag olarak biçimlendirir, ör. <c>"1234"</c> (tırnaklar değerin parçasıdır).
    /// </summary>
    /// <param name="version">EN: Row version. TR: Satır sürümü.</param>
    /// <returns>EN: The ETag. TR: ETag.</returns>
    public static string Format(uint version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// EN: Writes the ETag header of a response.
    /// TR: Bir cevabın ETag başlığını yazar.
    /// </summary>
    /// <param name="response">EN: The response. TR: Cevap.</param>
    /// <param name="version">EN: Row version. TR: Satır sürümü.</param>
    public static void SetETag(this HttpResponse response, uint version) =>
        response.Headers.ETag = Format(version);

    /// <summary>
    /// EN: Reads the version the client expects from <c>If-Match</c>. Missing → 428 (the client must say which version
    ///     it edited); unreadable → 412 (it can't match any version we issued).
    /// TR: İstemcinin beklediği sürümü <c>If-Match</c>'ten okur. Yoksa → 428 (istemci hangi sürümü düzenlediğini söylemeli);
    ///     okunamıyorsa → 412 (verdiğimiz hiçbir sürümle eşleşemez).
    /// </summary>
    /// <param name="request">EN: The request. TR: İstek.</param>
    /// <param name="version">EN: The expected version. TR: Beklenen sürüm.</param>
    /// <param name="problem">EN: The error to return when reading fails. TR: Okuma başarısız olursa dönülecek hata.</param>
    /// <returns>EN: True when a version was read. TR: Bir sürüm okunduysa true.</returns>
    public static bool TryReadIfMatch(
        this HttpRequest request,
        out uint version,
        [NotNullWhen(false)] out ProblemHttpResult? problem)
    {
        version = 0;
        var header = request.Headers.IfMatch.ToString().Trim();
        if (header.Length == 0)
        {
            problem = PreconditionRequired();
            return false;
        }

        var parsed = header.Length > 2 && header[0] == '"' && header[^1] == '"'
            && uint.TryParse(header.AsSpan(1, header.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out version);
        problem = parsed ? null : PreconditionFailed();
        return parsed;
    }

    /// <summary>
    /// EN: 412 — the record changed since the client read it.
    /// TR: 412 — kayıt, istemci okuduktan sonra değişti.
    /// </summary>
    /// <returns>EN: A 412 ProblemDetails. TR: 412 ProblemDetails.</returns>
    public static ProblemHttpResult PreconditionFailed() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "The record was changed by someone else.",
            detail: "Reload it to get the latest version and ETag, then apply your change again.");

    /// <summary>
    /// EN: 428 — an update without <c>If-Match</c> could overwrite someone else's change.
    /// TR: 428 — <c>If-Match</c> olmadan yapılan güncelleme başkasının değişikliğini ezebilir.
    /// </summary>
    /// <returns>EN: A 428 ProblemDetails. TR: 428 ProblemDetails.</returns>
    public static ProblemHttpResult PreconditionRequired() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status428PreconditionRequired,
            title: "If-Match header is required.",
            detail: "Send the ETag you received when reading the record.");
}
