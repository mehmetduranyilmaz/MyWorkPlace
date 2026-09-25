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
