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
}
