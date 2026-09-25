// EN: Identity service — companies, users, plans and access tokens. Owns the identity-db database.
//     The standard setup comes from BuildingBlocks (ADR-021); only the token-key difference is configured here.
// TR: Identity servisi — firmalar, kullanıcılar, planlar ve erişim token'ları. identity-db veritabanının sahibidir.
//     Standart kurulum BuildingBlocks'tan gelir (ADR-021); burada sadece token anahtarı farkı ayarlanır.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MyWorkplace.BuildingBlocks.Hosting;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Features.Discovery;
using MyWorkplace.Identity.Features.Login;
using MyWorkplace.Identity.Features.Register;
using MyWorkplace.Identity.Features.UpgradePlan;
using MyWorkplace.Identity.Features.Users;
using MyWorkplace.Identity.Persistence;
using MyWorkplace.Identity.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceModule<IdentityDbContext>("identity-db", Permissions.Catalog);
// EN: Must stay here: the validation source generator runs in the project declaring the request types (ADR-021).
// TR: Burada kalmalı: doğrulama kaynak üreteci istek tiplerini tanımlayan projede çalışır (ADR-021).
builder.Services.AddValidation();

// EN: Identity holds the signing keys itself, so it validates with them directly instead of downloading its own
//     discovery document. Registered after AddServiceModule, so it overrides the shared JwtBearer settings (ADR-006).
// TR: İmzalama anahtarları Identity'nin kendisinde olduğu için, kendi keşif dokümanını indirmek yerine doğrudan onlarla doğrular.
//     AddServiceModule'den sonra kaydedildiği için ortak JwtBearer ayarlarının üzerine yazar (ADR-006).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SigningKeyProvider, IOptions<TokenAuthenticationOptions>>((options, keys, auth) =>
    {
        options.Configuration = new OpenIdConnectConfiguration { Issuer = auth.Value.Issuer };
        options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => keys.All;
    });

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddSingleton<TokenIssuer>();

var app = builder.Build();

await app.UseServiceModuleAsync<IdentityDbContext>();

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

// EN: User management (ADR-022): every endpoint declares users.manage itself.
// TR: Kullanıcı yönetimi (ADR-022): her uç nokta users.manage iznini kendisi bildirir.
var users = identity.MapGroup("/users");
users.MapListUsers();
users.MapCreateUser();
users.MapGetUser();
users.MapUpdateUserAccess();
users.MapDeleteUser();

await app.RunAsync();
