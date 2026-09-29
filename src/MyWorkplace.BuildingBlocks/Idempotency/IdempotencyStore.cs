using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Idempotency;

/// <summary>
/// EN: What reserving a key found.
/// TR: Bir anahtarı ayırmanın bulduğu sonuç.
/// </summary>
public enum ReservationOutcome
{
    /// <summary>EN: The key is ours: run the request. TR: Anahtar bizim: isteği çalıştır.</summary>
    Reserved,

    /// <summary>EN: A stored response: replay it. TR: Saklanmış bir cevap: tekrar oynat.</summary>
    Replay,

    /// <summary>EN: The key belongs to a different request (422). TR: Anahtar farklı bir isteğe ait (422).</summary>
    Mismatch,

    /// <summary>EN: The first request is still running (409). TR: İlk istek hâlâ çalışıyor (409).</summary>
    InProgress,
}

/// <summary>
/// EN: The result of a reservation: the outcome, the stored record for a replay, and our lock for completing.
/// TR: Bir ayırmanın sonucu: sonuç, tekrar oynatma için saklanan kayıt ve tamamlamak için kilidimiz.
/// </summary>
/// <param name="Outcome">EN: The outcome. TR: Sonuç.</param>
/// <param name="Record">EN: The stored record (replay). TR: Saklanan kayıt (tekrar oynatma).</param>
/// <param name="LockedAt">EN: Our lock (reserved). TR: Kilidimiz (ayrıldı).</param>
public sealed record Reservation(ReservationOutcome Outcome, IdempotencyRecord? Record, DateTimeOffset LockedAt);

/// <summary>
/// EN: A response worth storing.
/// TR: Saklanmaya değer bir cevap.
/// </summary>
/// <param name="StatusCode">EN: Status. TR: Durum.</param>
/// <param name="ContentType">EN: Content-Type. TR: Content-Type.</param>
/// <param name="Location">EN: Location. TR: Location.</param>
/// <param name="ETag">EN: ETag. TR: ETag.</param>
/// <param name="Body">EN: Body. TR: Gövde.</param>
public sealed record StoredResponse(int StatusCode, string? ContentType, string? Location, string? ETag, byte[] Body);

/// <summary>
/// EN: Reserve → run → record for <c>Idempotency-Key</c> (ADR-027). Each call uses a scope of its own, so the key rows
///     never mix with the endpoint's unit of work (an endpoint may clear its change tracker or roll back).
/// TR: <c>Idempotency-Key</c> için ayır → çalıştır → kaydet (ADR-027). Her çağrı kendi kapsamını kullanır; böylece anahtar satırları
///     uç noktanın iş birimine asla karışmaz (bir uç nokta change tracker'ını temizleyebilir veya geri alabilir).
/// </summary>
/// <param name="scopes">EN: Scope factory. TR: Kapsam fabrikası.</param>
/// <param name="time">EN: Clock. TR: Saat.</param>
public sealed class IdempotencyStore(IServiceScopeFactory scopes, TimeProvider time)
{
    /// <summary>EN: How long a key counts. TR: Bir anahtarın ne kadar geçerli olduğu.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// EN: After this, a key still "in progress" is considered abandoned (a crash) and can be taken over.
    /// TR: Bundan sonra hâlâ "işleniyor" olan bir anahtar terk edilmiş (çökme) sayılır ve devralınabilir.
    /// </summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(1);

    /// <summary>
    /// EN: Reserves the key for this request, or tells why it can't run: replay, mismatch or still in progress.
    /// TR: Anahtarı bu istek için ayırır veya neden çalışamayacağını söyler: tekrar oynatma, uyuşmazlık veya hâlâ işleniyor.
    /// </summary>
    /// <param name="tenantId">EN: Company. TR: Firma.</param>
    /// <param name="key">EN: The client's key. TR: İstemcinin anahtarı.</param>
    /// <param name="requestHash">EN: Fingerprint of the request. TR: İsteğin parmak izi.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The reservation. TR: Ayırma.</returns>
    public async Task<Reservation> ReserveAsync(Guid tenantId, string key, string requestHash, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
            db.IdempotencyKeys.Add(new IdempotencyRecord
            {
                TenantId = tenantId, Key = key, RequestHash = requestHash, LockedAt = now, ExpiresAt = now + Lifetime,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new Reservation(ReservationOutcome.Reserved, null, now);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // EN: The key exists: decide below. TR: Anahtar var: aşağıda karar verilir.
            }
        }

        await using var readScope = scopes.CreateAsyncScope();
        var keys = readScope.ServiceProvider.GetRequiredService<ServiceDbContext>().IdempotencyKeys;
        var existing = await keys.SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Key == key, cancellationToken);
        if (existing is null)
        {
            // EN: Deleted meanwhile (released or cleaned up): start over. TR: Bu arada silindi (bırakıldı veya temizlendi): baştan başla.
            return await ReserveAsync(tenantId, key, requestHash, cancellationToken);
        }

        var expired = existing.ExpiresAt <= now;
        if (!expired && existing.RequestHash != requestHash)
        {
            return new Reservation(ReservationOutcome.Mismatch, null, default);
        }

        if (!expired && existing.StatusCode is not null)
        {
            return new Reservation(ReservationOutcome.Replay, existing, default);
        }

        if (!expired && now - existing.LockedAt < AbandonedAfter)
        {
            return new Reservation(ReservationOutcome.InProgress, null, default);
        }

        // EN: Expired, or abandoned by a crash: take it over — conditionally, so of two takers only one wins.
        // TR: Süresi dolmuş veya bir çökmeyle terk edilmiş: devral — koşullu olarak; böylece iki devralandan sadece biri kazanır.
        var taken = await keys
            .Where(r => r.TenantId == tenantId && r.Key == key && r.LockedAt == existing.LockedAt)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.RequestHash, requestHash)
                    .SetProperty(r => r.StatusCode, (int?)null)
                    .SetProperty(r => r.ContentType, (string?)null)
                    .SetProperty(r => r.Location, (string?)null)
                    .SetProperty(r => r.ETag, (string?)null)
                    .SetProperty(r => r.Body, (byte[]?)null)
                    .SetProperty(r => r.LockedAt, now)
                    .SetProperty(r => r.ExpiresAt, now + Lifetime),
                cancellationToken);

        return taken == 1
            ? new Reservation(ReservationOutcome.Reserved, null, now)
            : new Reservation(ReservationOutcome.InProgress, null, default);
    }

    /// <summary>
    /// EN: Stores the response of a reservation we still hold.
    /// TR: Hâlâ elimizde olan bir ayırmanın cevabını saklar.
    /// </summary>
    /// <param name="tenantId">EN: Company. TR: Firma.</param>
    /// <param name="key">EN: The key. TR: Anahtar.</param>
    /// <param name="lockedAt">EN: Our lock. TR: Kilidimiz.</param>
    /// <param name="response">EN: The response. TR: Cevap.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public async Task CompleteAsync(
        Guid tenantId, string key, DateTimeOffset lockedAt, StoredResponse response, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ServiceDbContext>().IdempotencyKeys
            .Where(r => r.TenantId == tenantId && r.Key == key && r.LockedAt == lockedAt)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.StatusCode, response.StatusCode)
                    .SetProperty(r => r.ContentType, response.ContentType)
                    .SetProperty(r => r.Location, response.Location)
                    .SetProperty(r => r.ETag, response.ETag)
                    .SetProperty(r => r.Body, response.Body),
                cancellationToken);
    }

    /// <summary>
    /// EN: Gives the key up (a server error): a retry may then run the request again.
    /// TR: Anahtarı bırakır (sunucu hatası): bir yeniden deneme isteği o zaman tekrar çalıştırabilir.
    /// </summary>
    /// <param name="tenantId">EN: Company. TR: Firma.</param>
    /// <param name="key">EN: The key. TR: Anahtar.</param>
    /// <param name="lockedAt">EN: Our lock. TR: Kilidimiz.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public async Task ReleaseAsync(Guid tenantId, string key, DateTimeOffset lockedAt)
    {
        // EN: Not cancellable: the request may already be aborted, and a left-over lock would block retries for a minute.
        // TR: İptal edilemez: istek zaten iptal edilmiş olabilir ve geride kalan bir kilit yeniden denemeleri bir dakika engellerdi.
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ServiceDbContext>().IdempotencyKeys
            .Where(r => r.TenantId == tenantId && r.Key == key && r.LockedAt == lockedAt)
            .ExecuteDeleteAsync(CancellationToken.None);
    }

    /// <summary>
    /// EN: Deletes the expired keys of every company.
    /// TR: Tüm firmaların süresi dolmuş anahtarlarını siler.
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: How many were deleted. TR: Kaç tanesinin silindiği.</returns>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ServiceDbContext>().IdempotencyKeys
            .Where(r => r.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
