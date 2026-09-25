using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWorkplace.Abstractions.Identity;

// EN: Same namespace as the other service defaults, so AddTokenAuthentication() is found without an extra using.
// TR: Diğer ortak ayarlarla aynı namespace; böylece AddTokenAuthentication() ek bir using olmadan bulunur.
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// EN: Token validation and authorization policies shared by the gateway and every service, so both layers of
///     ADR-006 always accept and reject exactly the same tokens.
/// TR: Gateway ve tüm servislerin paylaştığı token doğrulama ve yetkilendirme politikaları; böylece ADR-006'nın iki
///     katmanı her zaman birebir aynı token'ları kabul eder ve reddeder.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// EN: Validates JWTs locally with the issuer's published keys (fetched once, cached, refreshed on an unknown kid),
    ///     requires a valid token by default (secure by default) and resolves permission and <c>plan:&lt;name&gt;</c>
    ///     policies by convention. Issuer, audience and the discovery address come from <c>Auth:*</c> configuration; a
    ///     missing value stops the host at startup (ADR-026).
    /// TR: JWT'leri token üreticisinin yayınladığı anahtarlarla yerelde doğrular (bir kez çekilir, önbelleğe alınır, bilinmeyen
    ///     kid'de yenilenir), varsayılan olarak geçerli token ister (secure by default) ve izin ile <c>plan:&lt;ad&gt;</c>
    ///     politikalarını kurala göre çözer. Issuer, audience ve keşif adresi <c>Auth:*</c> yapılandırmasından gelir; eksik bir
    ///     değer host'u açılışta durdurur (ADR-026).
    /// </summary>
    /// <typeparam name="TBuilder">EN: The host builder type. TR: Host builder tipi.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <returns>EN: The same builder for chaining. TR: Zincirleme kullanım için aynı builder.</returns>
    public static TBuilder AddTokenAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<TokenAuthenticationOptions>()
            .BindConfiguration(TokenAuthenticationOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TokenAuthenticationOptions>, TokenAuthenticationOptionsValidator>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHttpClientFactory, IOptions<TokenAuthenticationOptions>>((options, httpClients, auth) =>
            {
                options.MetadataAddress = auth.Value.MetadataAddress;
                // EN: A factory client, so service discovery and resilience apply to the key download too.
                // TR: Fabrika istemcisi; böylece servis bulma ve hata toleransı anahtar indirmede de geçerli olur.
                options.Backchannel = httpClients.CreateClient("token-metadata");
                // EN: The logical "https+http" scheme is not literally "https://"; see ADR-006.
                // TR: Mantıksal "https+http" şeması harfiyen "https://" değildir; bkz. ADR-006.
                options.RequireHttpsMetadata = false;

                // EN: Keep claim names exactly as issued ("sub", "tenant_id", "plan").
                // TR: Claim adlarını üretildiği gibi tut ("sub", "tenant_id", "plan").
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = auth.Value.Issuer;
                options.TokenValidationParameters.ValidAudience = auth.Value.Audience;
                options.TokenValidationParameters.NameClaimType = TokenClaims.Subject;
            });

        builder.Services.AddAuthorizationBuilder()
            // EN: Secure by default: an endpoint without a policy requires a valid token.
            // TR: Varsayılan olarak korumalı: politikası olmayan uç nokta geçerli token ister.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // EN: Every catalog permission and every "plan:<name>" becomes a usable policy name (ADR-022, ADR-026).
        // TR: Katalogdaki her izin ve her "plan:<ad>" kullanılabilir bir politika adı olur (ADR-022, ADR-026).
        builder.Services.AddConventionPolicies();

        return builder;
    }
}
