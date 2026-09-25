// EN: Orders service (Basic plan). Owns orders-db. Built from the reference module (ADR-016, ADR-017, ADR-021) and the
//     first service that publishes events (ADR-023, ADR-024).
// TR: Orders servisi (Basic plan). orders-db veritabanının sahibidir. Referans modülden üretildi (ADR-016, ADR-017, ADR-021) ve
//     olay yayınlayan ilk servistir (ADR-023, ADR-024).

using MyWorkplace.BuildingBlocks.Hosting;
using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Features;
using MyWorkplace.Orders.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceModule<OrdersDbContext>("orders-db", Permissions.Catalog);
builder.AddServiceMessaging<OrdersDbContext>("orders-db");
// EN: Must stay here: the validation source generator runs in the project declaring the request types (ADR-021).
// TR: Burada kalmalı: doğrulama kaynak üreteci istek tiplerini tanımlayan projede çalışır (ADR-021).
builder.Services.AddValidation();

var app = builder.Build();

await app.UseServiceModuleAsync<OrdersDbContext>();

// EN: No plan policy: Orders is a Basic module; every endpoint declares its permission.
// TR: Plan politikası yok: Orders bir Basic modüldür; her uç nokta kendi iznini bildirir.
var orders = app.MapGroup("/orders").WithTags("Orders");
orders.MapListOrders();
orders.MapCreateOrder();
orders.MapGetOrder();
orders.MapUpdateOrder();
orders.MapDeleteOrder();
orders.MapPlaceOrder();

await app.RunAsync();
