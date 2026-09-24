using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace MyWorkplace.BuildingBlocks.Http;

/// <summary>
/// EN: The same API documentation setup for every service: an OpenAPI document and the Scalar UI.
/// TR: Her servis için aynı API dokümantasyonu ayarı: bir OpenAPI dokümanı ve Scalar arayüzü.
/// </summary>
public static class ApiDocsExtensions
{
    /// <summary>
    /// EN: Registers OpenAPI document generation (endpoint summaries and descriptions come from the endpoints).
    /// TR: OpenAPI dokümanı üretimini kaydeder (uç nokta özetleri ve açıklamaları uç noktalardan gelir).
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddServiceApiDocs(this IServiceCollection services) => services.AddOpenApi();

    /// <summary>
    /// EN: Maps <c>/openapi/v1.json</c> and the Scalar UI at <c>/scalar</c>, in Development only.
    ///     Code samples default to C# (HttpClient) — the language of this project.
    /// TR: <c>/openapi/v1.json</c> ve <c>/scalar</c> adresindeki Scalar arayüzünü sadece geliştirme ortamında açar.
    ///     Kod örnekleri varsayılan olarak C# (HttpClient) gelir — bu projenin dili.
    /// </summary>
    /// <param name="app">EN: The web application. TR: Web uygulaması.</param>
    /// <returns>EN: The same application. TR: Aynı uygulama.</returns>
    public static WebApplication MapServiceApiDocs(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // EN: Explicitly anonymous: services are secure by default (T-008), but the docs must stay readable.
            // TR: Bilinçli olarak anonim: servisler varsayılan olarak korumalı (T-008), ama dokümanlar okunabilir kalmalı.
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference(options => options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient))
                .AllowAnonymous();
        }

        return app;
    }
}
