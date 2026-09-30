namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The one wait for eventual consistency (T-047): an event travels through RabbitMQ before its effect is visible, so
///     tests poll until it is. One timeout for all of them, generous enough for a busy CI runner (ADR-028: a slow machine
///     must not look like a bug).
/// TR: Olaya dayalı tutarlılık için tek bekleme (T-047): bir olay etkisi görünmeden önce RabbitMQ'dan geçer; bu yüzden testler görünene kadar
///     yoklar. Hepsi için tek bir zaman aşımı; meşgul bir CI makinesine yetecek kadar geniş (ADR-028: yavaş bir makine hata gibi görünmemeli).
/// </summary>
internal static class Eventually
{
    /// <summary>EN: How long to wait before failing. TR: Başarısız saymadan önce ne kadar beklenir.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>EN: Pause between two checks. TR: İki kontrol arasındaki ara.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// EN: Polls <paramref name="condition"/> until it holds; fails the test after <see cref="Timeout"/>, naming what was
    ///     awaited.
    /// TR: <paramref name="condition"/> sağlanana kadar yoklar; <see cref="Timeout"/> sonunda beklenen şeyi söyleyerek testi düşürür.
    /// </summary>
    /// <param name="condition">EN: The condition. TR: Koşul.</param>
    /// <param name="what">EN: What is awaited, e.g. "the order to reach Inventory". TR: Beklenen şey, ör. "the order to reach Inventory".</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public static async Task UntilAsync(Func<Task<bool>> condition, string what, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Waited {Timeout.TotalSeconds:0} s for {what}, in vain.");
            await Task.Delay(PollInterval, ct);
        }
    }
}
