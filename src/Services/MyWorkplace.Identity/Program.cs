// EN: Identity service — companies, users and (from T-023) tokens. Owns the identity-db database.
// TR: Identity servisi — firmalar, kullanıcılar ve (T-023'ten itibaren) token'lar. identity-db veritabanının sahibidir.

using Microsoft.AspNetCore.Identity;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Features.Register;
using MyWorkplace.Identity.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceDbContext<IdentityDbContext>("identity-db");

builder.Services.AddServiceProblemDetails();
// EN: .NET 10 built-in validation: DataAnnotations on request types, automatic 400 ProblemDetails.
// TR: .NET 10 yerleşik doğrulaması: istek tiplerinde DataAnnotations, otomatik 400 ProblemDetails.
builder.Services.AddValidation();
builder.Services.AddServiceApiDocs();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

var app = builder.Build();

app.UseServiceProblemDetails();
app.MapDefaultEndpoints();
app.MapServiceApiDocs();

if (app.Environment.IsDevelopment())
{
    // EN: Development only (ADR-015). TR: Sadece geliştirme ortamında (ADR-015).
    await app.MigrateDatabaseAsync<IdentityDbContext>();
}

var identity = app.MapGroup("/identity").WithTags("Identity");
identity.MapRegisterTenant();

await app.RunAsync();
