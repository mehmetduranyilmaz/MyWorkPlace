// EN: Customers service (Basic plan) — the reference module every later module copies (ADR-016, ADR-017, ADR-021).
//     Owns customers-db. The standard setup (auth, database, errors, docs, middleware order) comes from BuildingBlocks.
// TR: Customers servisi (Basic plan) — sonraki her modülün kopyaladığı referans modül (ADR-016, ADR-017, ADR-021).
//     customers-db veritabanının sahibidir. Standart kurulum (kimlik, veritabanı, hatalar, dokümanlar, middleware sırası) BuildingBlocks'tan gelir.

using MyWorkplace.BuildingBlocks.Hosting;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Customers.Features;
using MyWorkplace.Customers.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceModule<CustomersDbContext>("customers-db", Permissions.Catalog);
// EN: Must stay here: the validation source generator runs in the project declaring the request types (ADR-021).
// TR: Burada kalmalı: doğrulama kaynak üreteci istek tiplerini tanımlayan projede çalışır (ADR-021).
builder.Services.AddValidation();

var app = builder.Build();

await app.UseServiceModuleAsync<CustomersDbContext>();

// EN: No policy on the group: the secure-by-default fallback requires a signed-in user of any plan.
// TR: Grupta politika yok: varsayılan kural, herhangi bir plandaki giriş yapmış kullanıcıyı ister.
var customers = app.MapGroup("/customers").WithTags("Customers");
customers.MapListCustomers();
customers.MapCreateCustomer();
customers.MapGetCustomer();
customers.MapUpdateCustomer();
customers.MapDeleteCustomer();

await app.RunAsync();
