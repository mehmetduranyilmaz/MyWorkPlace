using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MyWorkplace.Identity.Tokens;

/// <summary>
/// EN: Encrypts signing keys for storage (ADR-032). An implementation backed by a key vault can replace the default
///     one in production (T-069).
/// TR: İmzalama anahtarlarını saklamak için şifreler (ADR-032). Production'da bir anahtar kasasına dayanan bir uygulama varsayılanın yerini
///     alabilir (T-069).
/// </summary>
public interface ISigningKeyProtector
{
    /// <summary>
    /// EN: Encrypts a private key, bound to its key id.
    /// TR: Bir özel anahtarı, anahtar kimliğine bağlı olarak şifreler.
    /// </summary>
    /// <param name="keyId">EN: The key's <c>kid</c>. TR: Anahtarın <c>kid</c>'i.</param>
    /// <param name="privateKey">EN: PKCS#8 private key. TR: PKCS#8 özel anahtar.</param>
    /// <returns>EN: The bytes to store. TR: Saklanacak baytlar.</returns>
    byte[] Protect(string keyId, ReadOnlySpan<byte> privateKey);

    /// <summary>
    /// EN: Decrypts a stored private key; throws <see cref="CryptographicException"/> if the master secret is wrong or the
    ///     bytes were changed or belong to another key.
    /// TR: Saklanan bir özel anahtarın şifresini çözer; ana sır yanlışsa veya baytlar değiştirilmiş ya da başka bir anahtara aitse
    ///     <see cref="CryptographicException"/> fırlatır.
    /// </summary>
    /// <param name="keyId">EN: The key's <c>kid</c>. TR: Anahtarın <c>kid</c>'i.</param>
    /// <param name="protectedKey">EN: The stored bytes. TR: Saklanan baytlar.</param>
    /// <returns>EN: PKCS#8 private key. TR: PKCS#8 özel anahtar.</returns>
    byte[] Unprotect(string keyId, ReadOnlySpan<byte> protectedKey);
}

/// <summary>
/// EN: Envelope encryption with AES-GCM: a 256-bit key derived (HKDF-SHA256) from the master secret encrypts each private
///     key. Stored as <c>version | nonce | tag | ciphertext</c>; the key id is authenticated data, so a blob moved to another
///     row fails to decrypt.
/// TR: AES-GCM ile zarf şifrelemesi: ana sırdan (HKDF-SHA256) türetilen 256 bitlik bir anahtar her özel anahtarı şifreler.
///     <c>sürüm | nonce | etiket | şifreli metin</c> olarak saklanır; anahtar kimliği doğrulanan veridir, böylece başka bir satıra taşınan bir
///     blob'un şifresi çözülemez.
/// </summary>
public sealed class AesGcmSigningKeyProtector : ISigningKeyProtector
{
    /// <summary>EN: Format of the stored bytes. TR: Saklanan baytların biçimi.</summary>
    private const byte FormatVersion = 1;

    /// <summary>EN: AES-GCM nonce size in bytes. TR: Bayt cinsinden AES-GCM nonce boyutu.</summary>
    private const int NonceSize = 12;

    /// <summary>EN: AES-GCM tag size in bytes. TR: Bayt cinsinden AES-GCM etiket boyutu.</summary>
    private const int TagSize = 16;

    /// <summary>EN: Size of the derived key in bytes (256 bits). TR: Türetilen anahtarın bayt cinsinden boyutu (256 bit).</summary>
    private const int KeySize = 32;

    /// <summary>EN: HKDF context: the same secret derives different keys for different uses. TR: HKDF bağlamı: aynı sır farklı kullanımlar için farklı anahtarlar türetir.</summary>
    private static readonly byte[] _info = "MyWorkplace.SigningKey.v1"u8.ToArray();

    /// <summary>EN: The derived encryption key. TR: Türetilen şifreleme anahtarı.</summary>
    private readonly byte[] _key;

    /// <summary>
    /// EN: Derives the encryption key from the configured master secret.
    /// TR: Şifreleme anahtarını yapılandırılmış ana sırdan türetir.
    /// </summary>
    /// <param name="options">EN: Signing key settings. TR: İmzalama anahtarı ayarları.</param>
    public AesGcmSigningKeyProtector(IOptions<SigningKeyOptions> options) =>
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(options.Value.MasterSecret), KeySize, info: _info);

    /// <inheritdoc />
    public byte[] Protect(string keyId, ReadOnlySpan<byte> privateKey)
    {
        var result = new byte[1 + NonceSize + TagSize + privateKey.Length];
        result[0] = FormatVersion;
        var nonce = result.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, privateKey, result.AsSpan(1 + NonceSize + TagSize), result.AsSpan(1 + NonceSize, TagSize), Encoding.UTF8.GetBytes(keyId));
        return result;
    }

    /// <inheritdoc />
    public byte[] Unprotect(string keyId, ReadOnlySpan<byte> protectedKey)
    {
        if (protectedKey.Length <= 1 + NonceSize + TagSize || protectedKey[0] != FormatVersion)
        {
            throw new CryptographicException("The stored signing key has an unknown format.");
        }

        var plaintext = new byte[protectedKey.Length - 1 - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            protectedKey.Slice(1, NonceSize),
            protectedKey[(1 + NonceSize + TagSize)..],
            protectedKey.Slice(1 + NonceSize, TagSize),
            plaintext,
            Encoding.UTF8.GetBytes(keyId));
        return plaintext;
    }
}
