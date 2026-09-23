using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MyWorkplace.BuildingBlocks.Http;

/// <summary>
/// EN: Standard error responses for every service: RFC 9457 <c>application/problem+json</c> (ADR-014).
/// TR: Her servis için standart hata cevapları: RFC 9457 <c>application/problem+json</c> (ADR-014).
/// </summary>
public static class ProblemDetailsExtensions
{
    /// <summary>
    /// EN: Registers ProblemDetails generation and the 409 mapping for concurrency conflicts.
    /// TR: ProblemDetails üretimini ve eşzamanlılık çakışmaları için 409 eşlemesini kaydeder.
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddServiceProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ConcurrencyConflictHandler>();
        return services;
    }

    /// <summary>
    /// EN: Adds the middleware: unhandled exceptions and empty error responses (401, 403, 404...) become ProblemDetails.
    ///     Call it early in the pipeline so it wraps everything after it.
    /// TR: Middleware'i ekler: yakalanmamış hatalar ve gövdesiz hata cevapları (401, 403, 404...) ProblemDetails olur.
    ///     Sonrasındaki her şeyi sarması için pipeline'da erken çağrılır.
    /// </summary>
    /// <param name="app">EN: The web application. TR: Web uygulaması.</param>
    /// <returns>EN: The same application. TR: Aynı uygulama.</returns>
    public static WebApplication UseServiceProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
