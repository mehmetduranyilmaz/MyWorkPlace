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
var postgres = builder.AddPostgres("postgres");
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

var customers = builder.AddProject<Projects.MyWorkplace_Customers>("customers")
    .WithHttpHealthCheck("/health");

// EN: Only the gateway is exposed externally; services are reached through it.
// TR: Dışarıya sadece gateway açılır; servislere onun üzerinden ulaşılır.
builder.AddProject<Projects.MyWorkplace_Gateway>("gateway")
    .WithReference(identity)
    .WithReference(customers)
    .WaitFor(identity)
    .WaitFor(customers)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
