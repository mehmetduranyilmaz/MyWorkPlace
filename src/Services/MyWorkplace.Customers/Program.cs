// EN: Customers service (Basic plan). Skeleton only — business endpoints arrive in T-009.
// TR: Customers servisi (Basic plan). Şimdilik iskelet — iş uç noktaları T-009'da gelecek.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

// EN: Temporary endpoint that proves gateway → service routing works end to end.
// TR: Gateway → servis yönlendirmesinin uçtan uca çalıştığını gösteren geçici uç nokta.
app.MapGet("/customers/info", () => Results.Ok(new { Service = "customers", Plan = "basic" }));

app.Run();
