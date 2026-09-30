// EN: Inventory service (Pro plan). Owns inventory-db. Built from the reference module (ADR-016, ADR-017, ADR-021).
//     Checks the plan itself (ADR-006): a Basic company reaching it directly, bypassing the gateway, still gets 403.
// TR: Inventory servisi (Pro plan). inventory-db veritabanının sahibidir. Referans modülden üretildi (ADR-016, ADR-017, ADR-021).
//     Planı kendisi de kontrol eder (ADR-006): gateway'i atlayıp doğrudan ulaşan Basic firma yine 403 alır.

using MyWorkplace.BuildingBlocks.Hosting;
using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.BuildingBlocks.Settings;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Features;
using MyWorkplace.Inventory.Features.Barcodes;
using MyWorkplace.Inventory.Features.Movements;
using MyWorkplace.Inventory.Features.UnmatchedLines;
using MyWorkplace.Inventory.Features.Units;
using MyWorkplace.Inventory.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceModule<InventoryDbContext>("inventory-db", Permissions.Catalog);
// EN: Consumes OrderPlaced to decrease stock (T-016); the handler is found in this assembly (ADR-023).
// TR: Stok düşmek için OrderPlaced'i dinler (T-016); handler bu derlemede bulunur (ADR-023).
builder.AddServiceMessaging<InventoryDbContext>("inventory-db");
builder.Services.AddScoped<StockLedger>();
builder.Services.AddScoped<UnitCatalog>();
builder.Services.AddScoped<OrderStockClaims>();
// EN: Must stay here: the validation source generator runs in the project declaring the request types (ADR-021).
// TR: Burada kalmalı: doğrulama kaynak üreteci istek tiplerini tanımlayan projede çalışır (ADR-021).
builder.Services.AddValidation();

var app = builder.Build();

await app.UseServiceModuleAsync<InventoryDbContext>();

// EN: Every inventory endpoint lives under this group and inherits the Pro-plan policy ("plan:pro").
// TR: Her stok uç noktası bu grubun altında yer alır ve Pro plan politikasını ("plan:pro") devralır.
var inventory = app.MapGroup("/inventory").RequireAuthorization(Plans.ProPolicy);

var items = inventory.MapGroup("/items").WithTags("Stock items");
items.MapListStockItems();
items.MapCreateStockItem();
items.MapGetStockItem();
items.MapUpdateStockItem();
items.MapDeleteStockItem();

// EN: Stock movements: the only way a balance changes by hand (ADR-020). TR: Stok hareketleri: bir bakiyenin elle değişmesinin tek yolu (ADR-020).
items.MapRecordMovement();
items.MapListMovements();

// EN: Barcodes: added per item unit, looked up by code (ADR-019, T-055). TR: Barkodlar: kalem birimine eklenir, koduyla sorgulanır (ADR-019, T-055).
items.MapAddBarcode();
items.MapRemoveBarcode();
inventory.MapGroup("/barcodes").WithTags("Barcodes").MapLookupBarcode();

// EN: Order lines that matched no item, for a person to settle (ADR-020, T-042). TR: Hiçbir kalemle eşleşmeyen sipariş satırları, bir insanın çözmesi için (ADR-020, T-042).
inventory.MapUnmatchedLines();

// EN: The unit catalog: system units plus the company's own (ADR-019). TR: Birim kataloğu: sistem birimleri artı firmanın kendi birimleri (ADR-019).
var units = inventory.MapGroup("/units").WithTags("Units");
units.MapListUnits();
units.MapCreateUnit();
units.MapGetUnit();
units.MapUpdateUnit();
units.MapDeleteUnit();

// EN: GET / PUT /inventory/settings (ADR-018). TR: GET / PUT /inventory/settings (ADR-018).
inventory.MapModuleSettings<InventorySettings>(Permissions.Inventory.Read);

await app.RunAsync();
