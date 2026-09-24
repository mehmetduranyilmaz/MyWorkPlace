// EN: API Gateway — the single entry point for all external traffic (ADR-001, ADR-002).
//     1. Authentication: validates the JWT locally with Identity's published keys (ADR-005).
//     2. Authorization: secure by default; public routes are marked "anonymous", Pro routes need "pro-plan" (ADR-006).
//     3. Routing: YARP forwards allowed requests; routes and their policies live in appsettings.json.
//     Token rules come from ServiceDefaults, shared with the services — the second layer of ADR-006.
// TR: API Gateway — tüm dış trafiğin tek giriş noktası (ADR-001, ADR-002).
//     1. Kimlik doğrulama: JWT'yi Identity'nin yayınladığı anahtarlarla yerelde doğrular (ADR-005).
//     2. Yetkilendirme: varsayılan olarak korumalı; açık rotalar "anonymous", Pro rotalar "pro-plan" ister (ADR-006).
//     3. Yönlendirme: YARP izin verilen istekleri iletir; rotalar ve politikaları appsettings.json'dadır.
//     Token kuralları, servislerle paylaşılan ServiceDefaults'tan gelir — ADR-006'nın ikinci katmanı.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenAuthentication();
builder.Services.AddProblemDetails();

// EN: YARP reads routes/clusters from configuration; cluster addresses like "https+http://customers"
//     are resolved through Aspire service discovery instead of hard-coded ports.
// TR: YARP rotaları ve hedef grupları yapılandırmadan okur; "https+http://customers" gibi adresler
//     sabit port yerine Aspire servis bulma ile çözülür.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

// EN: Turns the gateway's own empty 401/403 answers into ProblemDetails (ADR-014).
// TR: Gateway'in kendi gövdesiz 401/403 cevaplarını ProblemDetails'e çevirir (ADR-014).
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
