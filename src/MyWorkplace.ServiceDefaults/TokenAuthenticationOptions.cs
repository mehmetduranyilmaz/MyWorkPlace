using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// EN: Token settings, read from the <c>Auth</c> configuration section (ADR-026). The product sets them once in the AppHost,
///     so the issuing Identity service and every verifier always agree. The core holds no values of its own.
/// TR: <c>Auth</c> yapılandırma bölümünden okunan token ayarları (ADR-026). Ürün bunları AppHost'ta bir kez verir; böylece token'ı
///     üreten Identity servisi ve tüm doğrulayanlar her zaman aynı fikirde olur. Çekirdeğin kendine ait bir değeri yoktur.
/// </summary>
public sealed class TokenAuthenticationOptions
{
    /// <summary>EN: Configuration section name. TR: Yapılandırma bölümünün adı.</summary>
    public const string SectionName = "Auth";

    /// <summary>EN: Token issuer (<c>iss</c>). TR: Token'ı üreten (<c>iss</c>).</summary>
    [Required(ErrorMessage = "Auth:Issuer is required: the token issuer (iss), set in the AppHost.")]
    public string Issuer { get; set; } = "";

    /// <summary>EN: Token audience (<c>aud</c>). TR: Token'ın hedef kitlesi (<c>aud</c>).</summary>
    [Required(ErrorMessage = "Auth:Audience is required: the token audience (aud), set in the AppHost.")]
    public string Audience { get; set; } = "";

    /// <summary>
    /// EN: Address of the issuer's discovery document; logical "https+http://" addresses are resolved by service discovery.
    /// TR: Token üreticisinin keşif dokümanının adresi; mantıksal "https+http://" adresleri servis bulma ile çözülür.
    /// </summary>
    [Required(ErrorMessage = "Auth:MetadataAddress is required: the issuer's discovery document, set in the AppHost.")]
    public string MetadataAddress { get; set; } = "";
}

/// <summary>
/// EN: Compile-time generated validator of <see cref="TokenAuthenticationOptions"/> (no reflection, trim-safe).
/// TR: <see cref="TokenAuthenticationOptions"/> için derleme anında üretilen doğrulayıcı (reflection yok, trim uyumlu).
/// </summary>
[OptionsValidator]
public sealed partial class TokenAuthenticationOptionsValidator : IValidateOptions<TokenAuthenticationOptions>;
