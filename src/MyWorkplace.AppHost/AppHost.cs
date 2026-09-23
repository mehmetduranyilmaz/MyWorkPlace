// EN: Aspire AppHost — describes the whole system and starts it with one command (ADR-008).
//     Resource names ("customers", "gateway") are also the service-discovery names.
// TR: Aspire AppHost — tüm sistemi tanımlar ve tek komutla başlatır (ADR-008).
//     Kaynak isimleri ("customers", "gateway") aynı zamanda servis bulma isimleridir.

var builder = DistributedApplication.CreateBuilder(args);

var customers = builder.AddProject<Projects.MyWorkplace_Customers>("customers")
    .WithHttpHealthCheck("/health");

// EN: Only the gateway is exposed externally; services are reached through it.
// TR: Dışarıya sadece gateway açılır; servislere onun üzerinden ulaşılır.
builder.AddProject<Projects.MyWorkplace_Gateway>("gateway")
    .WithReference(customers)
    .WaitFor(customers)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
