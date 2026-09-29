using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyWorkplace.BuildingBlocks.Idempotency;

/// <summary>
/// EN: Removes expired idempotency keys once an hour, so the table stays small (ADR-027).
/// TR: Tablo küçük kalsın diye süresi dolmuş idempotency anahtarlarını saatte bir siler (ADR-027).
/// </summary>
/// <param name="store">EN: The key store. TR: Anahtar deposu.</param>
/// <param name="time">EN: Clock. TR: Saat.</param>
/// <param name="logger">EN: Logger. TR: Logger.</param>
public sealed class IdempotencyCleanup(IdempotencyStore store, TimeProvider time, ILogger<IdempotencyCleanup> logger)
    : BackgroundService
{
    /// <summary>EN: How often it runs. TR: Ne sıklıkla çalıştığı.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var deleted = await store.DeleteExpiredAsync(stoppingToken);
                logger.LogDebug("Deleted {Count} expired idempotency keys.", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // EN: A failed round (e.g. the database restarting) must not stop the next ones.
                // TR: Başarısız bir tur (ör. veritabanı yeniden başlıyor) sonrakileri durdurmamalı.
                logger.LogWarning(ex, "Cleaning up idempotency keys failed; retrying in {Interval}.", Interval);
            }
        }
    }
}

/// <summary>
/// EN: Registration of the <c>Idempotency-Key</c> capability (ADR-027); both calls are made by the service module setup.
/// TR: <c>Idempotency-Key</c> yeteneğinin kaydı (ADR-027); iki çağrıyı da servis modülü kurulumu yapar.
/// </summary>
public static class IdempotencyExtensions
{
    /// <summary>
    /// EN: Registers the key store and the hourly cleanup.
    /// TR: Anahtar deposunu ve saatlik temizliği kaydeder.
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddIdempotency(this IServiceCollection services)
    {
        services.AddSingleton<IdempotencyStore>();
        services.AddHostedService<IdempotencyCleanup>();
        return services;
    }

    /// <summary>
    /// EN: Adds the middleware; it must come after authentication, because keys are per company.
    /// TR: Middleware'i ekler; anahtarlar firma bazında olduğu için kimlik doğrulamadan sonra gelmelidir.
    /// </summary>
    /// <param name="app">EN: The application. TR: Uygulama.</param>
    /// <returns>EN: The same application. TR: Aynı uygulama.</returns>
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();
}
