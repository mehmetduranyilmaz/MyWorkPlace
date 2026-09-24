// EN: Customers service (Basic plan) — the reference module every later module copies (ADR-016, ADR-017).
//     Owns customers-db. Validates tokens itself (ADR-006); the tenant filter follows the signed-in user (T-008).
// TR: Customers servisi (Basic plan) — sonraki her modülün kopyaladığı referans modül (ADR-016, ADR-017).
//     customers-db veritabanının sahibidir. Token'ları kendisi doğrular (ADR-006); firma filtresi giriş yapan kullanıcıyı izler (T-008).

using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Customers.Features;
using MyWorkplace.Customers.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenAuthentication();
builder.AddServiceDbContext<CustomersDbContext>("customers-db");

builder.Services.AddServiceProblemDetails();
builder.Services.AddValidation();
builder.Services.AddServiceApiDocs();

var app = builder.Build();

app.UseServiceProblemDetails();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapServiceApiDocs();

if (app.Environment.IsDevelopment())
{
    // EN: Development only (ADR-015). TR: Sadece geliştirme ortamında (ADR-015).
    await app.MigrateDatabaseAsync<CustomersDbContext>();
}

// EN: No policy on the group: the secure-by-default fallback requires a signed-in user of any plan.
// TR: Grupta politika yok: varsayılan kural, herhangi bir plandaki giriş yapmış kullanıcıyı ister.
var customers = app.MapGroup("/customers").WithTags("Customers");
customers.MapListCustomers();
customers.MapCreateCustomer();
customers.MapGetCustomer();
customers.MapUpdateCustomer();
customers.MapDeleteCustomer();

await app.RunAsync();
