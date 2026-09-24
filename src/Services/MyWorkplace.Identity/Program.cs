// EN: Identity service — companies, users, plans and access tokens. Owns the identity-db database.
// TR: Identity servisi — firmalar, kullanıcılar, planlar ve erişim token'ları. identity-db veritabanının sahibidir.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Features.Discovery;
using MyWorkplace.Identity.Features.Login;
using MyWorkplace.Identity.Features.Register;
using MyWorkplace.Identity.Features.UpgradePlan;
using MyWorkplace.Identity.Persistence;
using MyWorkplace.Identity.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceDbContext<IdentityDbContext>("identity-db");

// EN: Same token rules as every service (ADR-006), with one difference: Identity holds the keys itself, so it
//     validates with them directly instead of downloading its own discovery document over the network.
// TR: Her servisle aynı token kuralları (ADR-006), tek farkla: anahtarlar Identity'nin kendisinde olduğu için,
//     kendi keşif dokümanını ağdan indirmek yerine doğrudan onlarla doğrular.
builder.AddTokenAuthentication();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SigningKeyProvider>((options, keys) =>
    {
        options.Configuration = new OpenIdConnectConfiguration { Issuer = TokenClaims.Issuer };
        options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => keys.All;
    });

builder.Services.AddServiceProblemDetails();
// EN: .NET 10 built-in validation: DataAnnotations on request types, automatic 400 ProblemDetails.
// TR: .NET 10 yerleşik doğrulaması: istek tiplerinde DataAnnotations, otomatik 400 ProblemDetails.
builder.Services.AddValidation();
builder.Services.AddServiceApiDocs();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddSingleton<TokenIssuer>();

var app = builder.Build();

app.UseServiceProblemDetails();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapServiceApiDocs();

if (app.Environment.IsDevelopment())
{
    // EN: Development only (ADR-015). TR: Sadece geliştirme ortamında (ADR-015).
    await app.MigrateDatabaseAsync<IdentityDbContext>();
}

// EN: Load (or create on first start) the signing keys before accepting any request.
// TR: Herhangi bir isteği kabul etmeden önce imzalama anahtarlarını yükle (ilk açılışta oluştur).
await app.Services.GetRequiredService<SigningKeyProvider>().InitializeAsync();

// EN: Public endpoints declare AllowAnonymous themselves; anything else requires a token (secure by default).
// TR: Açık uç noktalar AllowAnonymous'u kendileri bildirir; geri kalan her şey token ister (varsayılan olarak korumalı).
var identity = app.MapGroup("/identity").WithTags("Identity");
identity.MapRegisterTenant();
identity.MapLogin();
identity.MapDiscovery();
identity.MapUpgradePlan();

await app.RunAsync();
