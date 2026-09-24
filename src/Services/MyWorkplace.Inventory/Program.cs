// EN: Inventory service (Pro plan). Skeleton only — business endpoints arrive in T-010.
//     Checks the plan itself (ADR-006): a Basic company reaching it directly, bypassing the gateway, still gets 403.
// TR: Inventory servisi (Pro plan). Şimdilik iskelet — iş uç noktaları T-010'da gelecek.
//     Planı kendisi de kontrol eder (ADR-006): gateway'i atlayıp doğrudan ulaşan Basic firma yine 403 alır.

using MyWorkplace.Contracts.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenAuthentication();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

// EN: Every inventory endpoint lives in this group and inherits the Pro-plan policy.
// TR: Her stok uç noktası bu grupta yer alır ve Pro plan politikasını devralır.
var inventory = app.MapGroup("/inventory").RequireAuthorization(PolicyNames.ProPlan);

// EN: Temporary endpoint that lets the Pro-plan policy be tested end to end.
// TR: Pro plan politikasının uçtan uca test edilebilmesini sağlayan geçici uç nokta.
inventory.MapGet("/info", () => Results.Ok(new { Service = "inventory", Plan = "pro" }));

app.Run();
