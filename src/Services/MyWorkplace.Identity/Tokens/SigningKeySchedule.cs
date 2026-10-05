namespace MyWorkplace.Identity.Tokens;

/// <summary>
/// EN: What the schedule needs to know about a stored key.
/// TR: Takvimin saklanan bir anahtar hakkında bilmesi gereken.
/// </summary>
/// <param name="KeyId">EN: The key's <c>kid</c>. TR: Anahtarın <c>kid</c>'i.</param>
/// <param name="ActivatesAt">EN: When it starts signing. TR: İmzalamaya ne zaman başladığı.</param>
/// <param name="CanSign">EN: False for a retired plaintext key that may only verify. TR: Sadece doğrulayabilen, emekliye ayrılmış düz bir anahtar için false.</param>
public sealed record SigningKeyState(string KeyId, DateTimeOffset ActivatesAt, bool CanSign);

/// <summary>
/// EN: What to change now.
/// TR: Şimdi neyin değişeceği.
/// </summary>
/// <param name="ToDelete">EN: Keys retired long enough to leave the JWKS. TR: JWKS'ten çıkacak kadar uzun süredir emekli olan anahtarlar.</param>
/// <param name="CreateActivatingAt">EN: Create a new key that signs from this moment, or null. TR: Bu andan itibaren imzalayan yeni bir anahtar üret ya da null.</param>
public sealed record SigningKeyPlan(IReadOnlyList<string> ToDelete, DateTimeOffset? CreateActivatingAt);

/// <summary>
/// EN: The life of a signing key as plain rules (ADR-032, ADR-029): published → signing → retiring → deleted. A new key is
///     published <see cref="SigningKeyOptions.PublishAhead"/> before it signs; a key that stopped signing stays
///     <see cref="SigningKeyOptions.RetainAfterUse"/> in the JWKS.
/// TR: Bir imzalama anahtarının yaşamı, düz kurallar olarak (ADR-032, ADR-029): yayınlandı → imzalıyor → emekliye ayrılıyor → silindi. Yeni bir
///     anahtar imzalamadan <see cref="SigningKeyOptions.PublishAhead"/> önce yayınlanır; imzalamayı bırakan bir anahtar
///     <see cref="SigningKeyOptions.RetainAfterUse"/> boyunca JWKS'te kalır.
/// </summary>
public static class SigningKeySchedule
{
    /// <summary>
    /// EN: The key that signs at <paramref name="now"/>: the newest signing-capable key already active.
    /// TR: <paramref name="now"/> anında imzalayan anahtar: zaten aktif olan, imzalayabilen en yeni anahtar.
    /// </summary>
    /// <param name="keys">EN: Stored keys. TR: Saklanan anahtarlar.</param>
    /// <param name="now">EN: Current time. TR: Şu an.</param>
    /// <returns>EN: The signing key, or null if none can sign. TR: İmzalayan anahtar veya hiçbiri imzalayamıyorsa null.</returns>
    public static SigningKeyState? SignerAt(IEnumerable<SigningKeyState> keys, DateTimeOffset now) =>
        keys.Where(k => k.CanSign && k.ActivatesAt <= now).MaxBy(k => k.ActivatesAt);

    /// <summary>
    /// EN: Decides which keys to delete and whether to create the next one.
    /// TR: Hangi anahtarların silineceğine ve sıradakinin üretilip üretilmeyeceğine karar verir.
    /// </summary>
    /// <param name="keys">EN: Stored keys. TR: Saklanan anahtarlar.</param>
    /// <param name="now">EN: Current time. TR: Şu an.</param>
    /// <param name="options">EN: The timings. TR: Süreler.</param>
    /// <returns>EN: The plan. TR: Plan.</returns>
    public static SigningKeyPlan Plan(IReadOnlyList<SigningKeyState> keys, DateTimeOffset now, SigningKeyOptions options)
    {
        // EN: A key stops signing when a later key activates; once that is RetainAfterUse ago, its tokens have expired.
        // TR: Bir anahtar, daha sonraki bir anahtar aktif olunca imzalamayı bırakır; bu RetainAfterUse kadar önceyse token'larının süresi dolmuştur.
        var toDelete = keys
            .Where(k => keys.Any(later => later.CanSign && later.ActivatesAt > k.ActivatesAt && later.ActivatesAt + options.RetainAfterUse <= now))
            .Select(k => k.KeyId)
            .ToList();

        var signer = SignerAt(keys, now);
        if (signer is null)
        {
            // EN: First start, or only a retired plaintext key left: sign with a new key at once.
            // TR: İlk açılış veya sadece emekli düz bir anahtar kaldı: hemen yeni bir anahtarla imzala.
            return new SigningKeyPlan(toDelete, now);
        }

        var nextExists = keys.Any(k => k.CanSign && k.ActivatesAt > signer.ActivatesAt);
        var due = signer.ActivatesAt + options.Lifetime;
        if (nextExists || now < due - options.PublishAhead)
        {
            return new SigningKeyPlan(toDelete, null);
        }

        // EN: Never sooner than PublishAhead from now, even if the check came late.
        // TR: Kontrol geç kalsa bile asla şu andan PublishAhead'den daha erken değil.
        var activatesAt = due > now + options.PublishAhead ? due : now + options.PublishAhead;
        return new SigningKeyPlan(toDelete, activatesAt);
    }
}
