// EN: Customers service (Basic plan). Skeleton only — business endpoints arrive in T-009.
//     Validates tokens itself (ADR-006): reaching it directly, bypassing the gateway, still requires a valid token.
// TR: Customers servisi (Basic plan). Şimdilik iskelet — iş uç noktaları T-009'da gelecek.
//     Token'ları kendisi de doğrular (ADR-006): gateway atlanıp doğrudan ulaşılsa bile geçerli token gerekir.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenAuthentication();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

// EN: Temporary endpoint; no policy given, so the secure-by-default fallback requires a signed-in user.
// TR: Geçici uç nokta; politika verilmedi, bu yüzden varsayılan kural giriş yapmış kullanıcı ister.
app.MapGet("/customers/info", () => Results.Ok(new { Service = "customers", Plan = "basic" }));

app.Run();
