using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Identity.Domain;

/// <summary>
/// EN: An RSA key pair that signs access tokens (ADR-012). The public half is published via JWKS; the private half
///     never leaves this service. Stored unencrypted — production must use a key vault or HSM instead.
/// TR: Erişim token'larını imzalayan bir RSA anahtar çifti (ADR-012). Açık yarısı JWKS ile yayınlanır; özel yarısı
///     bu servisten asla çıkmaz. Şifresiz saklanır — üretimde bunun yerine key vault veya HSM kullanılmalıdır.
/// </summary>
public sealed class SigningKey : Entity, IAuditable
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
    /// EN: Private key in PKCS#8 format. Sensitive: never audited, never returned by any endpoint.
    /// TR: PKCS#8 formatında özel anahtar. Hassas: asla denetim günlüğüne yazılmaz, hiçbir uç noktadan dönmez.
    /// </summary>
    public required byte[] PrivateKey { get; init; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}
