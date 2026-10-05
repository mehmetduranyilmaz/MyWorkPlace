using Microsoft.Extensions.Options;

namespace MyWorkplace.Identity.Tokens;

/// <summary>
/// EN: How signing keys are protected and rotated (ADR-032), read from the <c>SigningKeys</c> section. The AppHost sets the
///     master secret (an Aspire secret parameter) and the timings.
/// TR: İmzalama anahtarlarının nasıl korunduğu ve değiştirildiği (ADR-032); <c>SigningKeys</c> bölümünden okunur. Ana sırrı (bir Aspire gizli
///     parametresi) ve süreleri AppHost verir.
/// </summary>
public sealed class SigningKeyOptions
{
    /// <summary>EN: Configuration section name. TR: Yapılandırma bölümünün adı.</summary>
    public const string SectionName = "SigningKeys";

    /// <summary>EN: Shortest accepted master secret. TR: Kabul edilen en kısa ana sır.</summary>
    public const int MasterSecretMinLength = 32;

    /// <summary>
    /// EN: The secret the encryption key is derived from. Never in the database — that is the whole point.
    /// TR: Şifreleme anahtarının türetildiği sır. Asla veritabanında değil — bütün mesele bu.
    /// </summary>
    public string MasterSecret { get; set; } = "";

    /// <summary>EN: How long a key signs before the next one takes over. TR: Bir anahtarın, sıradaki devralmadan önce ne kadar imzaladığı.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// EN: How long a new key is published in the JWKS before it signs, so every verifier knows it in advance.
    /// TR: Yeni bir anahtarın imzalamadan önce JWKS'te ne kadar yayınlandığı; böylece her doğrulayıcı onu önceden tanır.
    /// </summary>
    public TimeSpan PublishAhead { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// EN: How long a key that stopped signing stays in the JWKS, so the tokens it signed still verify.
    /// TR: İmzalamayı bırakan bir anahtarın JWKS'te ne kadar kaldığı; böylece imzaladığı token'lar hâlâ doğrulanır.
    /// </summary>
    public TimeSpan RetainAfterUse { get; set; } = TimeSpan.FromHours(24);

    /// <summary>EN: How often the keys are checked and reloaded. TR: Anahtarların ne sıklıkla kontrol edilip yeniden yüklendiği.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// EN: Checks <see cref="SigningKeyOptions"/> at start-up; every message names the setting and why it matters.
/// TR: <see cref="SigningKeyOptions"/>'ı açılışta kontrol eder; her mesaj ayarı ve neden önemli olduğunu söyler.
/// </summary>
public sealed class SigningKeyOptionsValidator : IValidateOptions<SigningKeyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SigningKeyOptions options)
    {
        List<string> failures = [];
        if (options.MasterSecret.Length < SigningKeyOptions.MasterSecretMinLength)
        {
            failures.Add($"SigningKeys:MasterSecret is missing or shorter than {SigningKeyOptions.MasterSecretMinLength} characters: " +
                "it comes from the AppHost parameter 'signing-key-master-secret' (ADR-032).");
        }

        if (options.PublishAhead <= TimeSpan.Zero || options.PublishAhead >= options.Lifetime)
        {
            failures.Add("SigningKeys:PublishAhead must be positive and shorter than SigningKeys:Lifetime.");
        }

        if (options.RetainAfterUse < TokenIssuer.Lifetime)
        {
            failures.Add($"SigningKeys:RetainAfterUse must be at least the token lifetime ({TokenIssuer.Lifetime}): " +
                "otherwise tokens signed by the previous key stop verifying before they expire.");
        }

        if (options.CheckInterval <= TimeSpan.Zero || options.CheckInterval > options.PublishAhead)
        {
            failures.Add("SigningKeys:CheckInterval must be positive and at most SigningKeys:PublishAhead, " +
                "so the next key is always created in time.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
