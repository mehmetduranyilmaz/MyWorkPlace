// EN: Aspire AppHost — describes the whole system and starts it with one command (ADR-008).
//     Resource names ("identity-db", "identity", "gateway") are also the service-discovery / connection names.
// TR: Aspire AppHost — tüm sistemi tanımlar ve tek komutla başlatır (ADR-008).
//     Kaynak isimleri ("identity-db", "identity", "gateway") aynı zamanda servis bulma / bağlantı isimleridir.

using Microsoft.Extensions.Configuration;

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
//     as "messaging"; the first one arrives with T-014.
// TR: Servisler arası olaylar için mesaj aracı (ADR-007, ADR-023). Olay yayınlayan veya dinleyen servisler ona "messaging"
//     adıyla bağlanır; ilki T-014 ile gelir.
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

var identity = builder.AddProject<Projects.MyWorkplace_Identity>("identity")
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithHttpHealthCheck("/health");

// EN: Services reference Identity to fetch its public signing keys and validate tokens themselves (ADR-006).
// TR: Servisler, açık imzalama anahtarlarını alıp token'ları kendileri doğrulamak için Identity'ye bağlanır (ADR-006).
var customers = builder.AddProject<Projects.MyWorkplace_Customers>("customers")
    .WithReference(customersDb)
    .WaitFor(customersDb)
    .WithReference(identity)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

var inventory = builder.AddProject<Projects.MyWorkplace_Inventory>("inventory")
    .WithReference(inventoryDb)
    .WaitFor(inventoryDb)
    .WithReference(identity)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

// EN: Only the gateway is exposed externally; services are reached through it.
// TR: Dışarıya sadece gateway açılır; servislere onun üzerinden ulaşılır.
builder.AddProject<Projects.MyWorkplace_Gateway>("gateway")
    .WithReference(identity)
    .WithReference(customers)
    .WithReference(inventory)
    .WaitFor(identity)
    .WaitFor(customers)
    .WaitFor(inventory)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
