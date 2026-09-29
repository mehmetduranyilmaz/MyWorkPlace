using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyWorkplace.BuildingBlocks.Identity;

namespace MyWorkplace.BuildingBlocks.Idempotency;

/// <summary>
/// EN: Makes every authenticated <c>POST</c> safe to retry with an <c>Idempotency-Key</c> header (ADR-027). Without the
///     header, or without a company (sign-up, sign-in), the request passes untouched. With it: the same request again gets
///     the first response replayed, a different request under the key gets <c>422</c>, a repeat while the first runs gets
///     <c>409</c>. <c>2xx</c> and <c>4xx</c> are stored; <c>5xx</c> and exceptions release the key, so a retry can run.
/// TR: Giriş gerektiren her <c>POST</c>'u bir <c>Idempotency-Key</c> başlığıyla güvenle yeniden denenebilir yapar (ADR-027). Başlık yoksa veya
///     firma yoksa (kayıt, giriş) istek dokunulmadan geçer. Başlık varsa: aynı istek tekrar gelince ilk cevap tekrar oynatılır, anahtar altında
///     farklı bir istek <c>422</c>, ilki çalışırken bir tekrar <c>409</c> alır. <c>2xx</c> ve <c>4xx</c> saklanır; <c>5xx</c> ve hatalar
///     anahtarı bırakır, böylece bir yeniden deneme çalışabilir.
/// </summary>
/// <param name="next">EN: The rest of the pipeline. TR: Pipeline'ın geri kalanı.</param>
/// <param name="store">EN: The key store. TR: Anahtar deposu.</param>
public sealed class IdempotencyMiddleware(RequestDelegate next, IdempotencyStore store)
{
    /// <summary>EN: The request header. TR: İstek başlığı.</summary>
    public const string KeyHeader = "Idempotency-Key";

    /// <summary>EN: Marks a replayed response. TR: Tekrar oynatılmış bir cevabı işaretler.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>
    /// EN: Handles one request.
    /// TR: Bir isteği işler.
    /// </summary>
    /// <param name="context">EN: The HTTP context. TR: HTTP bağlamı.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Headers.TryGetValue(KeyHeader, out var header)
            || context.RequestServices.GetRequiredService<ICurrentUser>().TenantId is not { } tenantId)
        {
            await next(context);
            return;
        }

        var key = header.ToString().Trim();
        if (header.Count != 1 || key.Length is 0 or > IdempotencyRecord.KeyMaxLength)
        {
            await Problem(context, StatusCodes.Status400BadRequest, "Invalid Idempotency-Key.",
                $"Send one key of 1-{IdempotencyRecord.KeyMaxLength} characters.");
            return;
        }

        var ct = context.RequestAborted;
        var reservation = await store.ReserveAsync(tenantId, key, await FingerprintAsync(context.Request, ct), ct);
        switch (reservation.Outcome)
        {
            case ReservationOutcome.Replay:
                await ReplayAsync(context, reservation.Record!);
                return;
            case ReservationOutcome.Mismatch:
                await Problem(context, StatusCodes.Status422UnprocessableEntity, "Idempotency-Key reused.",
                    "This key was used for a different request; use a new key for a new request.");
                return;
            case ReservationOutcome.InProgress:
                await Problem(context, StatusCodes.Status409Conflict, "Request in progress.",
                    "A request with this Idempotency-Key is still running; retry later.");
                return;
        }

        await RunAndRecordAsync(context, tenantId, key, reservation.LockedAt);
    }

    /// <summary>
    /// EN: Runs the request, capturing its response; stores it (2xx, 4xx) or releases the key (5xx, exception).
    /// TR: İsteği çalıştırır ve cevabını yakalar; saklar (2xx, 4xx) veya anahtarı bırakır (5xx, hata).
    /// </summary>
    /// <param name="context">EN: The HTTP context. TR: HTTP bağlamı.</param>
    /// <param name="tenantId">EN: Company. TR: Firma.</param>
    /// <param name="key">EN: The key. TR: Anahtar.</param>
    /// <param name="lockedAt">EN: Our lock. TR: Kilidimiz.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task RunAndRecordAsync(HttpContext context, Guid tenantId, string key, DateTimeOffset lockedAt)
    {
        var original = context.Response.Body;
        await using var captured = new MemoryStream();
        context.Response.Body = captured;
        try
        {
            await next(context);
        }
        catch
        {
            await store.ReleaseAsync(tenantId, key, lockedAt);
            throw;
        }
        finally
        {
            context.Response.Body = original;
        }

        var response = context.Response;
        if (response.StatusCode is >= 200 and < 500)
        {
            await store.CompleteAsync(
                tenantId,
                key,
                lockedAt,
                new StoredResponse(response.StatusCode, response.ContentType, response.Headers.Location, response.Headers.ETag, captured.ToArray()),
                context.RequestAborted);
        }
        else
        {
            await store.ReleaseAsync(tenantId, key, lockedAt);
        }

        captured.Position = 0;
        await captured.CopyToAsync(original, context.RequestAborted);
    }

    /// <summary>
    /// EN: Writes the stored response again, marked as replayed.
    /// TR: Saklanan cevabı, tekrar oynatıldığı işaretlenerek yeniden yazar.
    /// </summary>
    /// <param name="context">EN: The HTTP context. TR: HTTP bağlamı.</param>
    /// <param name="record">EN: The stored record. TR: Saklanan kayıt.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord record)
    {
        var response = context.Response;
        response.StatusCode = record.StatusCode!.Value;
        response.ContentType = record.ContentType;
        if (record.Location is not null)
        {
            response.Headers.Location = record.Location;
        }

        if (record.ETag is not null)
        {
            response.Headers.ETag = record.ETag;
        }

        response.Headers[ReplayedHeader] = "true";
        if (record.Body is { Length: > 0 } body)
        {
            await response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    /// <summary>
    /// EN: SHA-256 of method, path, query and body — the same request always has the same fingerprint. The body is
    ///     buffered, so the endpoint can still read it.
    /// TR: Metot, adres, sorgu ve gövdenin SHA-256'sı — aynı isteğin parmak izi hep aynıdır. Gövde tamponlanır; böylece uç nokta onu yine
    ///     okuyabilir.
    /// </summary>
    /// <param name="request">EN: The request. TR: İstek.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Hex fingerprint. TR: Onaltılık parmak izi.</returns>
    private static async Task<string> FingerprintAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));

        var buffer = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// EN: Writes a ProblemDetails answer.
    /// TR: Bir ProblemDetails cevabı yazar.
    /// </summary>
    /// <param name="context">EN: The HTTP context. TR: HTTP bağlamı.</param>
    /// <param name="status">EN: Status code. TR: Durum kodu.</param>
    /// <param name="title">EN: Title. TR: Başlık.</param>
    /// <param name="detail">EN: Detail. TR: Ayrıntı.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static Task Problem(HttpContext context, int status, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
}
