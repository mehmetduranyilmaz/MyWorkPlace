using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Tokens;

/// <summary>
/// EN: Keeps the signing keys (ADR-032). Each refresh, under a database lock, retires plaintext keys, deletes keys retired
///     long enough, creates the next key on schedule, and reloads the key ring. Only keys that can sign are decrypted; the
///     JWKS and verification use public keys alone.
/// TR: İmzalama anahtarlarını tutar (ADR-032). Her yenilemede, bir veritabanı kilidi altında, düz anahtarları emekliye ayırır, yeterince uzun
///     süredir emekli olan anahtarları siler, sıradaki anahtarı takvimine göre üretir ve anahtar halkasını yeniden yükler. Sadece imzalayabilen
///     anahtarların şifresi çözülür; JWKS ve doğrulama sadece açık anahtarları kullanır.
/// </summary>
/// <param name="scopes">EN: Creates a scope per refresh (the DbContext is scoped). TR: Her yenileme için bir scope açar (DbContext scoped'dır).</param>
/// <param name="protector">EN: Decrypts and encrypts private keys. TR: Özel anahtarları çözer ve şifreler.</param>
/// <param name="time">EN: Clock. TR: Saat.</param>
/// <param name="options">EN: Timings. TR: Süreler.</param>
public sealed class SigningKeyProvider(
    IServiceScopeFactory scopes, ISigningKeyProtector protector, TimeProvider time, IOptions<SigningKeyOptions> options)
{
    /// <summary>EN: RSA key size. TR: RSA anahtar boyutu.</summary>
    private const int KeySizeInBits = 2048;

    /// <summary>
    /// EN: PostgreSQL advisory lock id for key changes: two instances never create a key at the same time.
    /// TR: Anahtar değişiklikleri için PostgreSQL advisory lock kimliği: iki örnek asla aynı anda anahtar üretmez.
    /// </summary>
    private const long LockId = 7_066_032;

    /// <summary>EN: The loaded keys, swapped as a whole. TR: Yüklenen anahtarlar; bir bütün olarak değiştirilir.</summary>
    private volatile KeyRing _ring = KeyRing.Empty;

    /// <summary>
    /// EN: The key that signs now. A key published ahead takes over by itself at its activation time.
    /// TR: Şu an imzalayan anahtar. Önceden yayınlanmış bir anahtar, aktivasyon anında kendiliğinden devralır.
    /// </summary>
    public RsaSecurityKey Current => _ring.SignerAt(time.GetUtcNow())
        ?? throw new InvalidOperationException("Signing keys are not loaded. Call InitializeAsync at startup.");

    /// <summary>
    /// EN: Every published key, public half only (JWKS and verification).
    /// TR: Yayınlanan her anahtar, sadece açık yarısı (JWKS ve doğrulama).
    /// </summary>
    public IReadOnlyList<RsaSecurityKey> All => _ring.PublicKeys;

    /// <summary>
    /// EN: Loads the keys at start-up. Fails loudly if they can't be decrypted — never creates a replacement (ADR-032).
    /// TR: Anahtarları açılışta yükler. Şifreleri çözülemezse yüksek sesle başarısız olur — asla yerine yenisini üretmez (ADR-032).
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    /// <summary>
    /// EN: Applies the schedule to the stored keys and reloads them.
    /// TR: Takvimi saklanan anahtarlara uygular ve onları yeniden yükler.
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // EN: Decrypt first: with a wrong master secret nothing may change — not even a new key.
        // TR: Önce şifre çöz: yanlış bir ana sırla hiçbir şey değişmemeli — yeni bir anahtar bile.
        CheckDecryptable(await db.SigningKeys.AsNoTracking().ToListAsync(cancellationToken));

        var stored = await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockId})", ct);

                // EN: Tracked on purpose: queries don't track by default, and these keys are changed or deleted below.
                // TR: Bilerek takip edilir: sorgular varsayılan olarak takip etmez ve bu anahtarlar aşağıda değiştirilir veya silinir.
                var keys = await db.SigningKeys.AsTracking().ToListAsync(ct);
                foreach (var key in keys)
                {
                    key.RetireLegacyPrivateKey();
                }

                var plan = SigningKeySchedule.Plan(
                    [.. keys.Select(k => new SigningKeyState(k.KeyId, k.ActivatesAt, k.CanSign))], time.GetUtcNow(), options.Value);
                db.SigningKeys.RemoveRange(keys.Where(k => plan.ToDelete.Contains(k.KeyId)));
                if (plan.CreateActivatingAt is { } activatesAt)
                {
                    db.SigningKeys.Add(NewKey(activatesAt));
                }

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return await db.SigningKeys.AsNoTracking().OrderBy(k => k.ActivatesAt).ToListAsync(ct);
            },
            cancellationToken);

        _ring = Load(stored);
    }

    /// <summary>
    /// EN: Creates a new key pair, private half encrypted.
    /// TR: Özel yarısı şifreli yeni bir anahtar çifti üretir.
    /// </summary>
    /// <param name="activatesAt">EN: When it starts signing. TR: İmzalamaya ne zaman başlayacağı.</param>
    /// <returns>EN: The key. TR: Anahtar.</returns>
    private SigningKey NewKey(DateTimeOffset activatesAt)
    {
        using var rsa = RSA.Create(KeySizeInBits);
        var keyId = Guid.CreateVersion7().ToString("N");
        return new SigningKey
        {
            KeyId = keyId,
            Algorithm = SecurityAlgorithms.RsaSha256,
            ActivatesAt = activatesAt,
            PublicKey = rsa.ExportSubjectPublicKeyInfo(),
            EncryptedPrivateKey = protector.Protect(keyId, rsa.ExportPkcs8PrivateKey()),
        };
    }

    /// <summary>
    /// EN: Throws a clear error if any stored key can't be decrypted with this master secret.
    /// TR: Saklanan herhangi bir anahtarın şifresi bu ana sırla çözülemiyorsa anlaşılır bir hata fırlatır.
    /// </summary>
    /// <param name="keys">EN: Stored keys. TR: Saklanan anahtarlar.</param>
    private void CheckDecryptable(IEnumerable<SigningKey> keys)
    {
        foreach (var key in keys.Where(k => k.CanSign))
        {
            Decrypt(key).Rsa.Dispose();
        }
    }

    /// <summary>
    /// EN: Builds the key ring: public keys for all, private keys only for the ones that can sign.
    /// TR: Anahtar halkasını kurar: herkes için açık anahtarlar, özel anahtarlar sadece imzalayabilenler için.
    /// </summary>
    /// <param name="keys">EN: Stored keys, oldest first. TR: Saklanan anahtarlar, en eski önce.</param>
    /// <returns>EN: The key ring. TR: Anahtar halkası.</returns>
    private KeyRing Load(IReadOnlyList<SigningKey> keys)
    {
        var signers = keys.Where(k => k.CanSign).Select(k => (k.ActivatesAt, Key: Decrypt(k))).ToList();
        var publicKeys = keys.Select(k =>
        {
            // EN: Kept alive for the lifetime of the ring; the provider is a singleton.
            // TR: Halka boyunca yaşar; sağlayıcı singleton'dır.
            var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(k.PublicKey, out _);
            return new RsaSecurityKey(rsa) { KeyId = k.KeyId };
        }).ToList();
        return new KeyRing(signers, publicKeys);
    }

    /// <summary>
    /// EN: Decrypts a key's private half; a wrong master secret becomes an error that names the cause and the recovery.
    /// TR: Bir anahtarın özel yarısının şifresini çözer; yanlış bir ana sır, nedeni ve kurtarmayı söyleyen bir hataya dönüşür.
    /// </summary>
    /// <param name="key">EN: A key that can sign. TR: İmzalayabilen bir anahtar.</param>
    /// <returns>EN: The private key. TR: Özel anahtar.</returns>
    private RsaSecurityKey Decrypt(SigningKey key)
    {
        byte[] pkcs8;
        try
        {
            pkcs8 = protector.Unprotect(key.KeyId, key.EncryptedPrivateKey!);
        }
        catch (CryptographicException e)
        {
            throw new InvalidOperationException(
                $"Signing key '{key.KeyId}' can't be decrypted: SigningKeys:MasterSecret is wrong or was changed. Restore the " +
                "secret, or delete the signing keys on purpose and restart — everyone signs in again (ADR-032).", e);
        }

        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        CryptographicOperations.ZeroMemory(pkcs8);
        return new RsaSecurityKey(rsa) { KeyId = key.KeyId };
    }

    /// <summary>
    /// EN: An immutable snapshot of the loaded keys.
    /// TR: Yüklenen anahtarların değişmez bir anlık görüntüsü.
    /// </summary>
    /// <param name="Signers">EN: Keys that can sign, with their activation time. TR: İmzalayabilen anahtarlar, aktivasyon zamanlarıyla.</param>
    /// <param name="PublicKeys">EN: Every published key. TR: Yayınlanan her anahtar.</param>
    private sealed record KeyRing(IReadOnlyList<(DateTimeOffset ActivatesAt, RsaSecurityKey Key)> Signers, IReadOnlyList<RsaSecurityKey> PublicKeys)
    {
        /// <summary>EN: Nothing loaded yet. TR: Henüz hiçbir şey yüklenmedi.</summary>
        public static readonly KeyRing Empty = new([], []);

        /// <summary>
        /// EN: The newest key already active at <paramref name="now"/>.
        /// TR: <paramref name="now"/> anında zaten aktif olan en yeni anahtar.
        /// </summary>
        /// <param name="now">EN: Current time. TR: Şu an.</param>
        /// <returns>EN: The key, or null. TR: Anahtar veya null.</returns>
        public RsaSecurityKey? SignerAt(DateTimeOffset now) =>
            Signers.Where(s => s.ActivatesAt <= now).OrderByDescending(s => s.ActivatesAt).Select(s => s.Key).FirstOrDefault();
    }
}
