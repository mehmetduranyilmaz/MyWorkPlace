// EN: API Gateway — the single entry point for all external traffic (ADR-001, ADR-002).
//     Routes are defined in appsettings.json under "ReverseProxy".
//     Authentication and plan policies are added in T-007.
// TR: API Gateway — tüm dış trafiğin tek giriş noktası (ADR-001, ADR-002).
//     Rotalar appsettings.json içindeki "ReverseProxy" bölümünde tanımlıdır.
//     Kimlik doğrulama ve plan politikaları T-007'de eklenecek.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// EN: YARP reads routes/clusters from configuration; cluster addresses like "https+http://customers"
//     are resolved through Aspire service discovery instead of hard-coded ports.
// TR: YARP rotaları ve hedef grupları yapılandırmadan okur; "https+http://customers" gibi adresler
//     sabit port yerine Aspire servis bulma ile çözülür.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapReverseProxy();

app.Run();
