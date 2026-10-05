using Microsoft.Extensions.Options;

namespace MyWorkplace.Identity.Tokens;

/// <summary>
/// EN: Checks the signing keys every <see cref="SigningKeyOptions.CheckInterval"/> (ADR-032): creates the next key on time,
///     deletes retired ones, and picks up keys another instance created.
/// TR: İmzalama anahtarlarını her <see cref="SigningKeyOptions.CheckInterval"/>'da kontrol eder (ADR-032): sıradaki anahtarı zamanında üretir,
///     emekli olanları siler ve başka bir örneğin ürettiği anahtarları alır.
/// </summary>
/// <param name="keys">EN: The key provider. TR: Anahtar sağlayıcısı.</param>
/// <param name="time">EN: Clock. TR: Saat.</param>
/// <param name="options">EN: Timings. TR: Süreler.</param>
/// <param name="logger">EN: Logger. TR: Logger.</param>
public sealed class SigningKeyRotation(
    SigningKeyProvider keys, TimeProvider time, IOptions<SigningKeyOptions> options, ILogger<SigningKeyRotation> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CheckInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await keys.RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // EN: The loaded keys keep working; the next check tries again. TR: Yüklü anahtarlar çalışmaya devam eder; sonraki kontrol yeniden dener.
                logger.LogError(ex, "Refreshing the signing keys failed; the loaded keys stay in use until the next check.");
            }
        }
    }
}
