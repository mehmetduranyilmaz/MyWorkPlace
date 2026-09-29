namespace MyWorkplace.BuildingBlocks.Idempotency;

/// <summary>
/// EN: One <c>Idempotency-Key</c> of a company (ADR-027): reserved while its request runs, then holding the response to
///     replay. Kept in the service's own database next to <c>processed_events</c>; always read with an explicit tenant.
/// TR: Bir firmanın bir <c>Idempotency-Key</c>'i (ADR-027): isteği çalışırken ayrılır, sonra tekrar oynatılacak cevabı tutar. Servisin
///     kendi veritabanında <c>processed_events</c>'in yanında tutulur; her zaman açık bir firma ile okunur.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>EN: Longest key. TR: En uzun anahtar.</summary>
    public const int KeyMaxLength = 100;

    /// <summary>EN: The company (part of the key). TR: Firma (anahtarın parçası).</summary>
    public Guid TenantId { get; init; }

    /// <summary>EN: The client's key (part of the key). TR: İstemcinin anahtarı (anahtarın parçası).</summary>
    public string Key { get; init; } = "";

    /// <summary>
    /// EN: SHA-256 of method, path and body: a repeat must be the same request (otherwise 422).
    /// TR: Metot, adres ve gövdenin SHA-256'sı: bir tekrar aynı istek olmalıdır (değilse 422).
    /// </summary>
    public string RequestHash { get; init; } = "";

    /// <summary>EN: The stored status; null while the request is running. TR: Saklanan durum; istek çalışırken null.</summary>
    public int? StatusCode { get; init; }

    /// <summary>EN: Stored Content-Type. TR: Saklanan Content-Type.</summary>
    public string? ContentType { get; init; }

    /// <summary>EN: Stored Location. TR: Saklanan Location.</summary>
    public string? Location { get; init; }

    /// <summary>EN: Stored ETag. TR: Saklanan ETag.</summary>
    public string? ETag { get; init; }

    /// <summary>EN: Stored body. TR: Saklanan gövde.</summary>
    public byte[]? Body { get; init; }

    /// <summary>
    /// EN: When the key was reserved (or taken over); it also identifies the holder, so only it can complete or release.
    /// TR: Anahtarın ne zaman ayrıldığı (veya devralındığı); sahibini de tanımlar, böylece sadece o tamamlayabilir veya bırakabilir.
    /// </summary>
    public DateTimeOffset LockedAt { get; init; }

    /// <summary>EN: When the key stops counting (24 hours). TR: Anahtarın geçerliliğini yitirdiği an (24 saat).</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
