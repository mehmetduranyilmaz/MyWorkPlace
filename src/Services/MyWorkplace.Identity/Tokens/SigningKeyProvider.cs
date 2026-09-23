using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Tokens;

/// <summary>
/// EN: Holds the signing keys in memory. At startup it loads them from identity-db, creating the first key if none
///     exists, so tokens stay valid across restarts (ADR-012). The newest key signs; all keys are published.
/// TR: İmzalama anahtarlarını bellekte tutar. Açılışta identity-db'den yükler, hiç yoksa ilk anahtarı oluşturur;
///     böylece token'lar yeniden başlatmalarda geçerli kalır (ADR-012). En yeni anahtar imzalar; hepsi yayınlanır.
/// </summary>
/// <param name="scopes">EN: Creates a scope to reach the database. TR: Veritabanına ulaşmak için scope oluşturur.</param>
public sealed class SigningKeyProvider(IServiceScopeFactory scopes)
{
    /// <summary>EN: RSA key size in bits. TR: Bit cinsinden RSA anahtar boyu.</summary>
    private const int KeySizeInBits = 2048;

    /// <summary>EN: Loaded keys, oldest first; empty until initialized. TR: Yüklenen anahtarlar, en eskisi önce; başlatılana kadar boş.</summary>
    private IReadOnlyList<RsaSecurityKey> _keys = [];

    /// <summary>
    /// EN: The key used to sign new tokens.
    /// TR: Yeni token'ları imzalamak için kullanılan anahtar.
    /// </summary>
    public RsaSecurityKey Current => _keys.Count > 0
        ? _keys[^1]
        : throw new InvalidOperationException("Signing keys are not loaded. Call InitializeAsync at startup.");

    /// <summary>
    /// EN: Every active key; all of them are published in JWKS so tokens signed by an older key still verify.
    /// TR: Tüm aktif anahtarlar; hepsi JWKS'te yayınlanır, böylece eski bir anahtarla imzalanmış token'lar da doğrulanır.
    /// </summary>
    public IReadOnlyList<RsaSecurityKey> All => _keys;

    /// <summary>
    /// EN: Loads the keys, creating the first one on the very first start. If two instances start together, both may
    ///     create a key; that is harmless because every key is published.
    /// TR: Anahtarları yükler; ilk açılışta ilk anahtarı oluşturur. İki kopya birlikte başlarsa ikisi de anahtar
    ///     oluşturabilir; her anahtar yayınlandığı için bu zararsızdır.
    /// </summary>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var stored = await db.SigningKeys.OrderBy(k => k.CreatedAt).ToListAsync(cancellationToken);
        if (stored.Count == 0)
        {
            using var rsa = RSA.Create(KeySizeInBits);
            var key = new SigningKey
            {
                KeyId = Guid.CreateVersion7().ToString("N"),
                Algorithm = SecurityAlgorithms.RsaSha256,
                PrivateKey = rsa.ExportPkcs8PrivateKey(),
            };
            db.SigningKeys.Add(key);
            await db.SaveChangesAsync(cancellationToken);
            stored = [key];
        }

        _keys = [.. stored.Select(ToSecurityKey)];
    }

    /// <summary>
    /// EN: Rebuilds a usable RSA key from its stored PKCS#8 bytes.
    /// TR: Saklanan PKCS#8 baytlarından kullanılabilir bir RSA anahtarı oluşturur.
    /// </summary>
    /// <param name="key">EN: Stored key. TR: Saklanan anahtar.</param>
    /// <returns>EN: The security key with its <c>kid</c>. TR: <c>kid</c>'i atanmış güvenlik anahtarı.</returns>
    private static RsaSecurityKey ToSecurityKey(SigningKey key)
    {
        // EN: Kept alive for the lifetime of the app; the provider is a singleton.
        // TR: Uygulama boyunca yaşar; sağlayıcı singleton'dır.
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(key.PrivateKey, out _);
        return new RsaSecurityKey(rsa) { KeyId = key.KeyId };
    }
}
