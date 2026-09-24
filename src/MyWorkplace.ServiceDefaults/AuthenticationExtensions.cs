using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MyWorkplace.Contracts.Identity;

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
    /// EN: Identity's discovery document; "https+http://identity" is resolved by Aspire service discovery (prefers HTTPS).
    /// TR: Identity'nin keşif dokümanı; "https+http://identity" adresini Aspire servis bulma çözer (HTTPS'i tercih eder).
    /// </summary>
    public const string IdentityMetadataAddress = "https+http://identity/identity/.well-known/openid-configuration";

    /// <summary>
    /// EN: Validates JWTs locally with Identity's published keys (fetched once, cached, refreshed on an unknown kid),
    ///     requires a valid token by default (secure by default) and registers the <c>pro-plan</c> policy.
    /// TR: JWT'leri Identity'nin yayınladığı anahtarlarla yerelde doğrular (bir kez çekilir, önbelleğe alınır, bilinmeyen
    ///     kid'de yenilenir), varsayılan olarak geçerli token ister (secure by default) ve <c>pro-plan</c> politikasını kaydeder.
    /// </summary>
    /// <typeparam name="TBuilder">EN: The host builder type. TR: Host builder tipi.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <returns>EN: The same builder for chaining. TR: Zincirleme kullanım için aynı builder.</returns>
    public static TBuilder AddTokenAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHttpClientFactory>((options, httpClients) =>
            {
                options.MetadataAddress = IdentityMetadataAddress;
                // EN: A factory client, so service discovery and resilience apply to the key download too.
                // TR: Fabrika istemcisi; böylece servis bulma ve hata toleransı anahtar indirmede de geçerli olur.
                options.Backchannel = httpClients.CreateClient("identity-metadata");
                // EN: The logical "https+http" scheme is not literally "https://"; see ADR-006.
                // TR: Mantıksal "https+http" şeması harfiyen "https://" değildir; bkz. ADR-006.
                options.RequireHttpsMetadata = false;

                // EN: Keep claim names exactly as issued ("sub", "tenant_id", "plan").
                // TR: Claim adlarını üretildiği gibi tut ("sub", "tenant_id", "plan").
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = TokenClaims.Issuer;
                options.TokenValidationParameters.ValidAudience = TokenClaims.Audience;
                options.TokenValidationParameters.NameClaimType = TokenClaims.Subject;
            });

        builder.Services.AddAuthorizationBuilder()
            // EN: Secure by default: an endpoint without a policy requires a valid token.
            // TR: Varsayılan olarak korumalı: politikası olmayan uç nokta geçerli token ister.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(PolicyNames.ProPlan, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(TokenClaims.Plan, TokenClaims.ProPlan));

        // EN: Every catalog permission becomes a usable policy name (ADR-022).
        // TR: Katalogdaki her izin kullanılabilir bir politika adı olur (ADR-022).
        builder.Services.AddPermissionPolicies();

        return builder;
    }
}
