using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Token settings come from <c>Auth:*</c> configuration (ADR-026): the core has no values of its own, and a missing
///     value stops the host at startup instead of failing on the first request.
/// TR: Token ayarları <c>Auth:*</c> yapılandırmasından gelir (ADR-026): çekirdeğin kendine ait değeri yoktur ve eksik bir değer
///     ilk istekte bozulmak yerine host'u açılışta durdurur.
/// </summary>
public sealed class TokenSettingsTests
{
    /// <summary>EN: A complete, made-up set of settings. TR: Eksiksiz, uydurma bir ayar kümesi.</summary>
    private static readonly Dictionary<string, string?> _complete = new()
    {
        ["Auth:Issuer"] = "test-issuer",
        ["Auth:Audience"] = "test-audience",
        ["Auth:MetadataAddress"] = "https+http://test-issuer/.well-known/openid-configuration",
    };

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompleteSettings_AreUsedToValidateTokens()
    {
        await using var app = Build(_complete);
        await app.StartAsync(Ct);

        var jwt = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal("test-issuer", jwt.TokenValidationParameters.ValidIssuer);
        Assert.Equal("test-audience", jwt.TokenValidationParameters.ValidAudience);
        Assert.Equal(_complete["Auth:MetadataAddress"], jwt.MetadataAddress);
    }

    [Theory]
    [InlineData("Auth:Issuer")]
    [InlineData("Auth:Audience")]
    [InlineData("Auth:MetadataAddress")]
    public async Task MissingSetting_StopsTheHostAtStartup_NamingTheKey(string missing)
    {
        var settings = new Dictionary<string, string?>(_complete);
        settings.Remove(missing);
        await using var app = Build(settings);

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(Ct));

        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataRefreshInterval_ReachesTheVerifier_DefaultTwelveHours()
    {
        await using var defaults = Build(_complete);
        await using var custom = Build(new Dictionary<string, string?>(_complete) { ["Auth:MetadataRefreshInterval"] = "00:30:00" });
        await defaults.StartAsync(Ct);
        await custom.StartAsync(Ct);

        Assert.Equal(TimeSpan.FromHours(12), JwtOf(defaults).AutomaticRefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(30), JwtOf(custom).AutomaticRefreshInterval);
    }

    [Fact]
    public async Task MetadataRefreshIntervalBelowFiveMinutes_StopsTheHostAtStartup()
    {
        // EN: The JWT library refuses less than 5 minutes — but only on the first request, failing every one (T-066).
        // TR: JWT kütüphanesi 5 dakikadan azını reddeder — ama sadece ilk istekte, her birini başarısız kılarak (T-066).
        await using var app = Build(new Dictionary<string, string?>(_complete) { ["Auth:MetadataRefreshInterval"] = "00:00:10" });

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(Ct));

        Assert.Contains("MetadataRefreshInterval", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// EN: The JWT bearer options of a started host.
    /// TR: Başlatılmış bir host'un JWT bearer seçenekleri.
    /// </summary>
    /// <param name="app">EN: The host. TR: Host.</param>
    /// <returns>EN: The options. TR: Seçenekler.</returns>
    private static JwtBearerOptions JwtOf(WebApplication app) =>
        app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

    /// <summary>
    /// EN: Builds a host with token authentication and the given configuration.
    /// TR: Token kimlik doğrulaması ve verilen yapılandırmayla bir host oluşturur.
    /// </summary>
    /// <param name="settings">EN: Configuration values. TR: Yapılandırma değerleri.</param>
    /// <returns>EN: The app, not started. TR: Başlatılmamış uygulama.</returns>
    private static WebApplication Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddHttpClient();
        builder.AddTokenAuthentication();
        return builder.Build();
    }
}
