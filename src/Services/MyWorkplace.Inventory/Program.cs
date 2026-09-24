// EN: Inventory service (Pro plan). Skeleton only — business endpoints arrive in T-010.
// TR: Inventory servisi (Pro plan). Şimdilik iskelet — iş uç noktaları T-010'da gelecek.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

// EN: Temporary endpoint that lets the gateway's Pro-plan policy be tested end to end.
// TR: Gateway'in Pro plan politikasının uçtan uca test edilebilmesini sağlayan geçici uç nokta.
app.MapGet("/inventory/info", () => Results.Ok(new { Service = "inventory", Plan = "pro" }));

app.Run();
