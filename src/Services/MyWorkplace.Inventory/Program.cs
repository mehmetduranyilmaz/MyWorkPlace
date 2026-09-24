// EN: Inventory service (Pro plan). Owns inventory-db. Built from the reference module (Customers, ADR-016 / ADR-017).
//     Checks the plan itself (ADR-006): a Basic company reaching it directly, bypassing the gateway, still gets 403.
// TR: Inventory servisi (Pro plan). inventory-db veritabanının sahibidir. Referans modülden üretildi (Customers, ADR-016 / ADR-017).
//     Planı kendisi de kontrol eder (ADR-006): gateway'i atlayıp doğrudan ulaşan Basic firma yine 403 alır.

using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Features;
using MyWorkplace.Inventory.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenAuthentication();
builder.AddServiceDbContext<InventoryDbContext>("inventory-db");

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
    await app.MigrateDatabaseAsync<InventoryDbContext>();
}

// EN: Every inventory endpoint lives under this group and inherits the Pro-plan policy.
// TR: Her stok uç noktası bu grubun altında yer alır ve Pro plan politikasını devralır.
var inventory = app.MapGroup("/inventory").RequireAuthorization(PolicyNames.ProPlan);

var items = inventory.MapGroup("/items").WithTags("Stock items");
items.MapListStockItems();
items.MapCreateStockItem();
items.MapGetStockItem();
items.MapUpdateStockItem();
items.MapDeleteStockItem();

await app.RunAsync();
