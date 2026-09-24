using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// EN: Kept in the Microsoft.Extensions.Hosting namespace (Aspire convention) so every service finds
//     AddServiceDefaults() without an extra using.
// TR: Aspire geleneğine uygun olarak Microsoft.Extensions.Hosting namespace'inde tutulur; böylece her servis
//     AddServiceDefaults() metodunu ek bir using olmadan bulur.
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// EN: Cross-cutting defaults every service gets: telemetry, health checks, service discovery and resilience.
/// TR: Her servisin aldığı ortak altyapı: telemetri, sağlık kontrolleri, servis bulma ve hata toleransı.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// EN: Readiness endpoint: all health checks must pass before the service receives traffic.
    /// TR: Hazır olma uç noktası: servis trafik almadan önce tüm sağlık kontrolleri geçmelidir.
    /// </summary>
    private const string HealthEndpointPath = "/health";

    /// <summary>
    /// EN: Liveness endpoint: only checks tagged "live" must pass (the process is responsive).
    /// TR: Canlılık uç noktası: sadece "live" etiketli kontroller geçmelidir (süreç yanıt veriyor).
    /// </summary>
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// EN: Registers all service defaults. Call this first in every service's Program.cs.
    /// TR: Tüm ortak altyapıyı kaydeder. Her servisin Program.cs dosyasında ilk olarak çağrılır.
    /// </summary>
    /// <typeparam name="TBuilder">EN: The host builder type. TR: Host builder tipi.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <returns>EN: The same builder for chaining. TR: Zincirleme kullanım için aynı builder.</returns>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // EN: Retry, timeout and circuit breaker for every outgoing HTTP call (ADR-007).
            // TR: Dışarı giden her HTTP çağrısı için yeniden deneme, zaman aşımı ve devre kesici (ADR-007).
            http.AddStandardResilienceHandler();

            // EN: Resolve logical names like "http://customers" to real addresses.
            // TR: "http://customers" gibi mantıksal isimleri gerçek adreslere çevirir.
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// EN: Sends logs, metrics and traces to the Aspire dashboard via OpenTelemetry.
    /// TR: Logları, metrikleri ve izleri OpenTelemetry ile Aspire paneline gönderir.
    /// </summary>
    /// <typeparam name="TBuilder">EN: The host builder type. TR: Host builder tipi.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <returns>EN: The same builder for chaining. TR: Zincirleme kullanım için aynı builder.</returns>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        // EN: Health probes would flood the traces; exclude them.
                        // TR: Sağlık kontrolleri izleri kalabalıklaştırır; hariç tutulur.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// EN: Enables the OTLP exporter when Aspire provides an endpoint.
    /// TR: Aspire bir uç nokta verdiğinde OTLP dışa aktarıcısını açar.
    /// </summary>
    /// <typeparam name="TBuilder">EN: The host builder type. TR: Host builder tipi.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <returns>EN: The same builder for chaining. TR: Zincirleme kullanım için aynı builder.</returns>
    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// EN: Adds a basic liveness check so orchestrators can tell the process is responsive.
    /// TR: Orkestratörlerin sürecin yanıt verdiğini anlayabilmesi için temel bir canlılık kontrolü ekler.
    /// </summary>
    /// <typeparam name="TBuilder">EN: The host builder type. TR: Host builder tipi.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <returns>EN: The same builder for chaining. TR: Zincirleme kullanım için aynı builder.</returns>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// EN: Maps /health and /alive. Development only: health details can leak information in production.
    /// TR: /health ve /alive uç noktalarını açar. Sadece geliştirmede: üretimde sağlık detayları bilgi sızdırabilir.
    /// </summary>
    /// <param name="app">EN: The web application. TR: Web uygulaması.</param>
    /// <returns>EN: The same application for chaining. TR: Zincirleme kullanım için aynı uygulama.</returns>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // EN: Explicitly anonymous: with secure-by-default authorization (T-007), probes would otherwise get 401.
            // TR: Bilinçli olarak anonim: varsayılan olarak korumalı yetkilendirmede (T-007) yoklamalar aksi halde 401 alırdı.
            app.MapHealthChecks(HealthEndpointPath).AllowAnonymous();
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live")
            }).AllowAnonymous();
        }

        return app;
    }
}
