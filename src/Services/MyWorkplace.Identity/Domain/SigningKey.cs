using System.Security.Cryptography;
using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Identity.Domain;

/// <summary>
/// EN: An RSA key pair that signs access tokens (ADR-012, ADR-032). The public half is published via JWKS; the private half
///     is stored encrypted under a master secret that is not in the database, and never leaves this service.
/// TR: Erişim token'larını imzalayan bir RSA anahtar çifti (ADR-012, ADR-032). Açık yarısı JWKS ile yayınlanır; özel yarısı veritabanında
///     olmayan bir ana sır altında şifreli saklanır ve bu servisten asla çıkmaz.
/// </summary>
public sealed class SigningKey : AuditableEntity
{
    /// <summary>
    /// EN: Key id (<c>kid</c>) written into every token header, so verifiers know which public key to use.
    /// TR: Her token başlığına yazılan anahtar kimliği (<c>kid</c>); doğrulayanlar hangi açık anahtarı kullanacağını bilir.
    /// </summary>
    public required string KeyId { get; init; }

    /// <summary>
    /// EN: Signing algorithm, e.g. RS256.
    /// TR: İmzalama algoritması, ör. RS256.
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// EN: When the key starts signing; until then it is only published (ADR-032).
    /// TR: Anahtarın imzalamaya başladığı an; o zamana kadar sadece yayınlanır (ADR-032).
    /// </summary>
    public required DateTimeOffset ActivatesAt { get; init; }

    /// <summary>
    /// EN: Public key (SubjectPublicKeyInfo). Enough to verify tokens and to publish the JWKS.
    /// TR: Açık anahtar (SubjectPublicKeyInfo). Token doğrulamaya ve JWKS'i yayınlamaya yeter.
    /// </summary>
    public required byte[] PublicKey { get; set; }

    /// <summary>
    /// EN: Private key, encrypted by <c>ISigningKeyProtector</c>. Null for a retired plaintext key, which may only verify.
    ///     Sensitive: never audited, never returned by any endpoint.
    /// TR: <c>ISigningKeyProtector</c> ile şifrelenmiş özel anahtar. Sadece doğrulayabilen emekli düz bir anahtar için null. Hassas: asla
    ///     denetim günlüğüne yazılmaz, hiçbir uç noktadan dönmez.
    /// </summary>
    public byte[]? EncryptedPrivateKey { get; init; }

    /// <summary>
    /// EN: Plaintext PKCS#8 private key of a key created before ADR-032. Wiped at start-up; the column is dropped in T-073
    ///     (expand / contract).
    /// TR: ADR-032'den önce üretilmiş bir anahtarın düz PKCS#8 özel anahtarı. Açılışta silinir; sütun T-073'te kaldırılır (genişlet / daralt).
    /// </summary>
    public byte[]? LegacyPrivateKey { get; private set; }

    /// <summary>EN: Whether it can sign (it has an encrypted private key). TR: İmzalayıp imzalayamayacağı (şifreli özel anahtarı var mı).</summary>
    public bool CanSign => EncryptedPrivateKey is not null;

    /// <summary>
    /// EN: Retires a plaintext key: keeps its public half for verifying and wipes the private half. A key that sat in clear
    ///     text counts as exposed, so it is never encrypted and never signs again (ADR-032).
    /// TR: Düz bir anahtarı emekliye ayırır: doğrulamak için açık yarısını tutar, özel yarısını siler. Düz metin olarak durmuş bir anahtar açığa
    ///     çıkmış sayılır; bu yüzden asla şifrelenmez ve bir daha asla imzalamaz (ADR-032).
    /// </summary>
    public void RetireLegacyPrivateKey()
    {
        if (LegacyPrivateKey is null)
        {
            return;
        }

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(LegacyPrivateKey, out _);
        PublicKey = rsa.ExportSubjectPublicKeyInfo();
        LegacyPrivateKey = null;
    }
}
