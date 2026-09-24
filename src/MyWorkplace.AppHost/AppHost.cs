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

var identityDb = postgres.AddDatabase("identity-db");

var identity = builder.AddProject<Projects.MyWorkplace_Identity>("identity")
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithHttpHealthCheck("/health");

// EN: Services reference Identity to fetch its public signing keys and validate tokens themselves (ADR-006).
// TR: Servisler, açık imzalama anahtarlarını alıp token'ları kendileri doğrulamak için Identity'ye bağlanır (ADR-006).
var customers = builder.AddProject<Projects.MyWorkplace_Customers>("customers")
    .WithReference(identity)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");

var inventory = builder.AddProject<Projects.MyWorkplace_Inventory>("inventory")
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
