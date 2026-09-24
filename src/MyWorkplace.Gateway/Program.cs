// EN: API Gateway — the single entry point for all external traffic (ADR-001, ADR-002).
//     1. Authentication: validates the JWT locally with Identity's published keys (ADR-005).
//     2. Authorization: secure by default; public routes are marked "anonymous", Pro routes need "pro-plan" (ADR-006).
//     3. Routing: YARP forwards allowed requests; routes and their policies live in appsettings.json.
// TR: API Gateway — tüm dış trafiğin tek giriş noktası (ADR-001, ADR-002).
//     1. Kimlik doğrulama: JWT'yi Identity'nin yayınladığı anahtarlarla yerelde doğrular (ADR-005).
//     2. Yetkilendirme: varsayılan olarak korumalı; açık rotalar "anonymous", Pro rotalar "pro-plan" ister (ADR-006).
//     3. Yönlendirme: YARP izin verilen istekleri iletir; rotalar ve politikaları appsettings.json'dadır.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MyWorkplace.Contracts.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IHttpClientFactory>((options, httpClients) =>
    {
        // EN: The discovery document is fetched once and the keys are cached; an unknown "kid" triggers a refresh.
        //     "https+http://identity" is resolved by Aspire service discovery, which prefers HTTPS.
        // TR: Keşif dokümanı bir kez çekilir ve anahtarlar önbelleğe alınır; bilinmeyen bir "kid" yenilemeyi tetikler.
        //     "https+http://identity" adresini, HTTPS'i tercih eden Aspire servis bulma çözer.
        options.MetadataAddress = "https+http://identity/identity/.well-known/openid-configuration";
        options.Backchannel = httpClients.CreateClient("identity-metadata");
        // EN: Required because the logical "https+http" scheme is not literally "https://"; see ADR-006.
        // TR: Mantıksal "https+http" şeması harfiyen "https://" olmadığı için gerekli; bkz. ADR-006.
        options.RequireHttpsMetadata = false;

        // EN: Keep claim names exactly as issued ("sub", "tenant_id", "plan") instead of .NET's long XML names.
        // TR: Claim adlarını .NET'in uzun XML adları yerine üretildiği gibi ("sub", "tenant_id", "plan") tut.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidIssuer = TokenClaims.Issuer;
        options.TokenValidationParameters.ValidAudience = TokenClaims.Audience;
        options.TokenValidationParameters.NameClaimType = TokenClaims.Subject;
    });

builder.Services.AddAuthorizationBuilder()
    // EN: Secure by default: a route without a policy requires a valid token. Forgetting a policy closes a route, never opens it.
    // TR: Varsayılan olarak korumalı: politikası olmayan rota geçerli token ister. Politikayı unutmak rotayı kapatır, asla açmaz.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(PolicyNames.ProPlan, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(TokenClaims.Plan, TokenClaims.ProPlan));

// EN: YARP reads routes/clusters from configuration; cluster addresses like "https+http://customers"
//     are resolved through Aspire service discovery instead of hard-coded ports.
// TR: YARP rotaları ve hedef grupları yapılandırmadan okur; "https+http://customers" gibi adresler
//     sabit port yerine Aspire servis bulma ile çözülür.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

// EN: Turns the gateway's own empty 401/403 answers into ProblemDetails (ADR-014).
// TR: Gateway'in kendi gövdesiz 401/403 cevaplarını ProblemDetails'e çevirir (ADR-014).
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
