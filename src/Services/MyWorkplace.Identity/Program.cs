// EN: Identity service — companies, users and access tokens. Owns the identity-db database.
// TR: Identity servisi — firmalar, kullanıcılar ve erişim token'ları. identity-db veritabanının sahibidir.

using Microsoft.AspNetCore.Identity;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Features.Discovery;
using MyWorkplace.Identity.Features.Login;
using MyWorkplace.Identity.Features.Register;
using MyWorkplace.Identity.Persistence;
using MyWorkplace.Identity.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceDbContext<IdentityDbContext>("identity-db");

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

var identity = app.MapGroup("/identity").WithTags("Identity");
identity.MapRegisterTenant();
identity.MapLogin();
identity.MapDiscovery();

await app.RunAsync();
