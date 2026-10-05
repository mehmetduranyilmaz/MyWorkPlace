using Microsoft.Extensions.Configuration;

namespace MyWorkplace.AppHost;

/// <summary>
/// EN: Hands the token settings (the AppHost's <c>Auth</c> section) to a project as <c>Auth__*</c> environment variables
///     (ADR-026). Defined once here, so the issuing Identity service and every verifier always use the same values.
/// TR: Token ayarlarını (AppHost'un <c>Auth</c> bölümü) bir projeye <c>Auth__*</c> ortam değişkenleri olarak verir (ADR-026).
///     Burada bir kez tanımlanır; böylece token'ı üreten Identity servisi ve tüm doğrulayanlar hep aynı değerleri kullanır.
/// </summary>
internal static class TokenSettingsExtensions
{
    /// <summary>
    /// EN: Passes every <c>Auth:*</c> setting to the project. A missing section stops the AppHost at once.
    /// TR: Her <c>Auth:*</c> ayarını projeye geçirir. Bölüm eksikse AppHost hemen durur.
    /// </summary>
    /// <param name="project">EN: The project resource. TR: Proje kaynağı.</param>
    /// <param name="configuration">EN: The AppHost configuration. TR: AppHost yapılandırması.</param>
    /// <returns>EN: The same resource for chaining. TR: Zincirleme kullanım için aynı kaynak.</returns>
    public static IResourceBuilder<ProjectResource> WithTokenSettings(
        this IResourceBuilder<ProjectResource> project, IConfiguration configuration)
    {
        foreach (var setting in configuration.GetRequiredSection("Auth").GetChildren())
        {
            project.WithEnvironment($"Auth__{setting.Key}", setting.Value);
        }

        return project;
    }

    /// <summary>
    /// EN: Passes the signing key settings (<c>SigningKeys:*</c>) and the master secret to Identity (ADR-032). Stops the
    ///     AppHost if a new key could start signing before every verifier has fetched it: <c>PublishAhead</c> must be longer
    ///     than <c>Auth:MetadataRefreshInterval</c>.
    /// TR: İmzalama anahtarı ayarlarını (<c>SigningKeys:*</c>) ve ana sırrı Identity'ye geçirir (ADR-032). Yeni bir anahtar, her doğrulayıcı onu
    ///     almadan imzalamaya başlayabilecekse AppHost'u durdurur: <c>PublishAhead</c>, <c>Auth:MetadataRefreshInterval</c>'dan uzun olmalıdır.
    /// </summary>
    /// <param name="project">EN: The Identity project. TR: Identity projesi.</param>
    /// <param name="configuration">EN: The AppHost configuration. TR: AppHost yapılandırması.</param>
    /// <param name="masterSecret">EN: The secret parameter. TR: Gizli parametre.</param>
    /// <returns>EN: The same resource for chaining. TR: Zincirleme kullanım için aynı kaynak.</returns>
    public static IResourceBuilder<ProjectResource> WithSigningKeys(
        this IResourceBuilder<ProjectResource> project, IConfiguration configuration, IResourceBuilder<ParameterResource> masterSecret)
    {
        var settings = configuration.GetRequiredSection("SigningKeys");
        var publishAhead = settings.GetValue<TimeSpan>("PublishAhead");
        var refresh = configuration.GetValue<TimeSpan>("Auth:MetadataRefreshInterval");
        if (publishAhead <= refresh)
        {
            throw new InvalidOperationException(
                $"SigningKeys:PublishAhead ({publishAhead}) must be longer than Auth:MetadataRefreshInterval ({refresh}): " +
                "a new signing key must reach every verifier before it signs (ADR-032).");
        }

        foreach (var setting in settings.GetChildren())
        {
            project.WithEnvironment($"SigningKeys__{setting.Key}", setting.Value);
        }

        return project.WithEnvironment("SigningKeys__MasterSecret", masterSecret);
    }
}
