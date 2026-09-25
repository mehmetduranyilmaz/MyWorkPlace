using System.Buffers.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Tokens;

namespace MyWorkplace.Identity.Features.Discovery;

/// <summary>
/// EN: Publishes what verifiers need to check our tokens on their own: the public keys (JWKS) and a discovery
///     document pointing to them, which standard JWT middleware reads automatically (used by the gateway in T-007).
/// TR: Doğrulayanların token'larımızı kendi başlarına kontrol etmesi için gerekenleri yayınlar: açık anahtarlar (JWKS)
///     ve onları gösteren keşif dokümanı; standart JWT middleware'i bunu otomatik okur (T-007'de gateway kullanacak).
/// </summary>
public static class Discovery
{
    /// <summary>
    /// EN: Maps the two <c>/.well-known</c> endpoints on the given route group.
    /// TR: Verilen rota grubunda iki <c>/.well-known</c> uç noktasını tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity route group. TR: /identity rota grubu.</param>
    /// <returns>EN: The same group. TR: Aynı grup.</returns>
    public static IEndpointRouteBuilder MapDiscovery(this IEndpointRouteBuilder group)
    {
        group.MapGet("/.well-known/jwks.json", GetJwks)
            .WithName("GetJwks")
            // EN: Public on purpose: no token exists yet at this point. TR: Bilerek herkese açık: bu noktada henüz token yok.
            .AllowAnonymous()
            .WithSummary("EN: Public signing keys (JWKS) | TR: Açık imzalama anahtarları (JWKS)")
            .WithDescription(
                "EN: Public keys that verify access tokens, matched by the token's kid. Contains no private key material. " +
                "TR: Erişim token'larını doğrulayan açık anahtarlar; token'daki kid ile eşleşir. Özel anahtara dair hiçbir bilgi içermez.");

        group.MapGet("/.well-known/openid-configuration", GetConfiguration)
            .WithName("GetOpenIdConfiguration")
            // EN: Public on purpose: no token exists yet at this point. TR: Bilerek herkese açık: bu noktada henüz token yok.
            .AllowAnonymous()
            .WithSummary("EN: Discovery document | TR: Keşif dokümanı")
            .WithDescription(
                "EN: Tells token verifiers the issuer and where the public keys are. " +
                "TR: Token doğrulayanlara üreticiyi ve açık anahtarların nerede olduğunu söyler.");

        return group;
    }

    /// <summary>
    /// EN: Builds the key set by hand from public RSA parameters only, so no private field can ever leak.
    /// TR: Anahtar kümesini sadece açık RSA parametrelerinden elle oluşturur; böylece hiçbir özel alan sızamaz.
    /// </summary>
    /// <param name="keys">EN: Signing keys. TR: İmzalama anahtarları.</param>
    /// <returns>EN: The JWKS document. TR: JWKS dokümanı.</returns>
    private static Ok<JwksDocument> GetJwks(SigningKeyProvider keys) =>
        TypedResults.Ok(new JwksDocument([.. keys.All.Select(ToPublicJwk)]));

    /// <summary>
    /// EN: Minimal OpenID Connect discovery document; the JWKS address is built from the incoming request, so it is
    ///     correct whether the caller came through the gateway or directly.
    /// TR: En küçük OpenID Connect keşif dokümanı; JWKS adresi gelen istekten oluşturulur, böylece çağıran gateway'den de
    ///     doğrudan da gelse doğrudur.
    /// </summary>
    /// <param name="request">EN: The incoming request. TR: Gelen istek.</param>
    /// <returns>EN: The discovery document. TR: Keşif dokümanı.</returns>
    private static Ok<OpenIdConfiguration> GetConfiguration(HttpRequest request) =>
        TypedResults.Ok(new OpenIdConfiguration(
            ProductTokens.Issuer,
            $"{request.Scheme}://{request.Host}{request.PathBase}/identity/.well-known/jwks.json",
            [SecurityAlgorithms.RsaSha256]));

    /// <summary>
    /// EN: Converts a signing key to its public JWK form (modulus and exponent only).
    /// TR: Bir imzalama anahtarını açık JWK biçimine çevirir (sadece modulus ve exponent).
    /// </summary>
    /// <param name="key">EN: Signing key. TR: İmzalama anahtarı.</param>
    /// <returns>EN: Public JWK. TR: Açık JWK.</returns>
    private static PublicJwk ToPublicJwk(RsaSecurityKey key)
    {
        var parameters = key.Rsa.ExportParameters(includePrivateParameters: false);
        return new PublicJwk(
            Kty: "RSA",
            Use: "sig",
            Alg: SecurityAlgorithms.RsaSha256,
            Kid: key.KeyId,
            N: Base64Url.EncodeToString(parameters.Modulus),
            E: Base64Url.EncodeToString(parameters.Exponent));
    }
}

/// <summary>
/// EN: JSON Web Key Set (RFC 7517).
/// TR: JSON Web Key Set (RFC 7517).
/// </summary>
/// <param name="Keys">EN: Public keys. TR: Açık anahtarlar.</param>
public sealed record JwksDocument(IReadOnlyList<PublicJwk> Keys);

/// <summary>
/// EN: Public part of an RSA signing key in JWK format.
/// TR: JWK formatında bir RSA imzalama anahtarının açık kısmı.
/// </summary>
/// <param name="Kty">EN: Key type. TR: Anahtar tipi.</param>
/// <param name="Use">EN: Intended use (signature). TR: Kullanım amacı (imza).</param>
/// <param name="Alg">EN: Algorithm. TR: Algoritma.</param>
/// <param name="Kid">EN: Key id. TR: Anahtar kimliği.</param>
/// <param name="N">EN: Modulus (base64url). TR: Modulus (base64url).</param>
/// <param name="E">EN: Exponent (base64url). TR: Exponent (base64url).</param>
public sealed record PublicJwk(string Kty, string Use, string Alg, string Kid, string N, string E);

/// <summary>
/// EN: The subset of the OpenID Connect discovery document that token verifiers need.
/// TR: OpenID Connect keşif dokümanının token doğrulayanların ihtiyaç duyduğu kısmı.
/// </summary>
/// <param name="Issuer">EN: Token issuer. TR: Token üreticisi.</param>
/// <param name="JwksUri">EN: Address of the public keys. TR: Açık anahtarların adresi.</param>
/// <param name="IdTokenSigningAlgValuesSupported">EN: Signing algorithms. TR: İmzalama algoritmaları.</param>
public sealed record OpenIdConfiguration(
    string Issuer,
    [property: System.Text.Json.Serialization.JsonPropertyName("jwks_uri")] string JwksUri,
    [property: System.Text.Json.Serialization.JsonPropertyName("id_token_signing_alg_values_supported")]
    IReadOnlyList<string> IdTokenSigningAlgValuesSupported);
