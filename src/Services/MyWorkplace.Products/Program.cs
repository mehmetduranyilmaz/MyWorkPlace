// EN: Products service (Basic plan) — the company's product catalog. Owns products-db. Built only by following
//     docs/process/adding-a-module.md: the proof that the core is plug-and-play (T-013).
// TR: Products servisi (Basic plan) — firmanın ürün kataloğu. products-db veritabanının sahibidir. Sadece
//     docs/process/adding-a-module.md izlenerek yazıldı: çekirdeğin tak-çalıştır olduğunun kanıtı (T-013).

using MyWorkplace.BuildingBlocks.Hosting;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Products.Features;
using MyWorkplace.Products.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceModule<ProductsDbContext>("products-db", Permissions.Catalog);
// EN: Must stay here: the validation source generator runs in the project declaring the request types (ADR-021).
// TR: Burada kalmalı: doğrulama kaynak üreteci istek tiplerini tanımlayan projede çalışır (ADR-021).
builder.Services.AddValidation();

var app = builder.Build();

await app.UseServiceModuleAsync<ProductsDbContext>();

// EN: No policy on the group: the secure-by-default fallback requires a signed-in user of any plan.
// TR: Grupta politika yok: varsayılan kural, herhangi bir plandaki giriş yapmış kullanıcıyı ister.
var products = app.MapGroup("/products").WithTags("Products");
products.MapListProducts();
products.MapCreateProduct();
products.MapGetProduct();
products.MapUpdateProduct();
products.MapDeleteProduct();

await app.RunAsync();
