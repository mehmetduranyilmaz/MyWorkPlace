// EN: Aspire AppHost — describes the whole system and starts it with one command (ADR-008).
//     Resource names ("identity-db", "identity", "gateway") are also the service-discovery / connection names.
// TR: Aspire AppHost — tüm sistemi tanımlar ve tek komutla başlatır (ADR-008).
//     Kaynak isimleri ("identity-db", "identity", "gateway") aynı zamanda servis bulma / bağlantı isimleridir.

using Microsoft.Extensions.Configuration;
using MyWorkplace.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// EN: Integration tests pass "Storage:Ephemeral=true": a fresh database per run and no extra tools.
// TR: Entegrasyon testleri "Storage:Ephemeral=true" verir: her çalıştırmada temiz veritabanı ve ek araç yok.
var ephemeral = builder.Configuration.GetValue<bool>("Storage:Ephemeral");

// EN: One PostgreSQL server, one database per service (ADR-003).
// TR: Tek PostgreSQL sunucusu, her servise ayrı veritabanı (ADR-003).
// EN: Version pinned in eng/PostgresImage.cs, shared with the tests.
// TR: Sürüm eng/PostgresImage.cs içinde sabit, testlerle ortak.
var postgres = builder.AddPostgres("postgres").WithImageTag(MyWorkplace.PostgresImage.Tag);
if (!ephemeral)
{
    // EN: Keep data between runs, and add PgWeb to browse tables from the dashboard.
    // TR: Veriyi çalıştırmalar arasında koru ve tabloları panelden incelemek için PgWeb ekle.
    postgres.WithDataVolume().WithPgWeb();
}

// EN: Message broker for cross-service events (ADR-007, ADR-023). Services that publish or consume events reference it
//     as "messaging".
// TR: Servisler arası olaylar için mesaj aracı (ADR-007, ADR-023). Olay yayınlayan veya dinleyen servisler ona "messaging"
//     adıyla bağlanır.
// EN: Version pinned in eng/RabbitMqImage.cs, shared with the tests.
// TR: Sürüm eng/RabbitMqImage.cs içinde sabit, testlerle ortak.
var messaging = builder.AddRabbitMQ("messaging").WithImageTag(MyWorkplace.RabbitMqImage.Tag);
if (!ephemeral)
{
    // EN: Keep queued messages between runs, and add the management UI to watch queues from the dashboard.
    // TR: Kuyruktaki mesajları çalıştırmalar arasında koru ve kuyrukları panelden izlemek için yönetim arayüzünü ekle.
    messaging.WithDataVolume().WithManagementPlugin();
}

var identityDb = postgres.AddDatabase("identity-db");
var customersDb = postgres.AddDatabase("customers-db");
var inventoryDb = postgres.AddDatabase("inventory-db");
var ordersDb = postgres.AddDatabase("orders-db");
var productsDb = postgres.AddDatabase("products-db");

// EN: Every project gets the same token settings from the "Auth" section of appsettings.json (ADR-026).
// TR: Her proje aynı token ayarlarını appsettings.json'daki "Auth" bölümünden alır (ADR-026).
var identity = builder.AddProject<Projects.MyWorkplace_Identity>("identity")
    .WithTokenSettings(builder.Configuration)
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithHttpHealthCheck("/health");

// EN: Services reference Identity to fetch its public signing keys and validate tokens themselves (ADR-006).
// TR: Servisler, açık imzalama anahtarlarını alıp token'ları kendileri doğrulamak için Identity'ye bağlanır (ADR-006).
var customers = builder.AddProject<Projects.MyWorkplace_Customers>("customers")
    .WithTokenSettings(builder.Configuration)
    .WithReference(customersDb)
    .WaitFor(customersDb)
    .WithReference(identity)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

// EN: Inventory consumes OrderPlaced, so it references the broker too (T-016).
// TR: Inventory OrderPlaced'i dinler; bu yüzden mesaj aracına da bağlanır (T-016).
var inventory = builder.AddProject<Projects.MyWorkplace_Inventory>("inventory")
    .WithTokenSettings(builder.Configuration)
    .WithReference(inventoryDb)
    .WaitFor(inventoryDb)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithReference(identity)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

// EN: Orders publishes events, so it also references the broker (ADR-023).
// TR: Orders olay yayınlar; bu yüzden mesaj aracına da bağlanır (ADR-023).
var orders = builder.AddProject<Projects.MyWorkplace_Orders>("orders")
    .WithTokenSettings(builder.Configuration)
    .WithReference(ordersDb)
    .WaitFor(ordersDb)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithReference(identity)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

var products = builder.AddProject<Projects.MyWorkplace_Products>("products")
    .WithTokenSettings(builder.Configuration)
    .WithReference(productsDb)
    .WaitFor(productsDb)
    .WithReference(identity)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

// EN: Only the gateway is exposed externally; services are reached through it.
// TR: Dışarıya sadece gateway açılır; servislere onun üzerinden ulaşılır.
builder.AddProject<Projects.MyWorkplace_Gateway>("gateway")
    .WithTokenSettings(builder.Configuration)
    .WithReference(identity)
    .WithReference(customers)
    .WithReference(inventory)
    .WithReference(orders)
    .WithReference(products)
    .WaitFor(identity)
    .WaitFor(customers)
    .WaitFor(inventory)
    .WaitFor(orders)
    .WaitFor(products)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
