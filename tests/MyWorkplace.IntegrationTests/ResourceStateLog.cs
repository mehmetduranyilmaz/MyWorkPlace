using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Aspire.Hosting.ApplicationModel;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Records every state change Aspire reports for one resource, with the time since the log started, its exit code and
///     process id, plus the resource's own lines about shutting down — evidence for a test that stops a service (T-044):
///     "it went Unknown" is a symptom, the sequence is what tells the cause.
/// TR: Aspire'ın bir kaynak için bildirdiği her durum değişikliğini; günlüğün başlangıcından geçen süre, çıkış kodu ve süreç kimliğiyle,
///     ayrıca kaynağın kapanmayla ilgili kendi satırlarıyla kaydeder — bir servisi durduran bir test için kanıt (T-044): "Unknown oldu" bir
///     belirtidir, nedeni sıra söyler.
/// </summary>
internal sealed class ResourceStateLog : IAsyncDisposable
{
    /// <summary>EN: Words that mark a line about the process's life. TR: Sürecin yaşamıyla ilgili bir satırı belirten kelimeler.</summary>
    private static readonly string[] _lifeWords = ["shut", "stop", "lifetime", "sigterm", "signal", "exit", "terminat", "exception"];

    /// <summary>EN: The recorded lines. TR: Kaydedilen satırlar.</summary>
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>EN: Stops the watches. TR: İzlemeleri durdurur.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>EN: Time since the log started. TR: Günlük başladığından beri geçen süre.</summary>
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>EN: Whether the resource has started shutting down. TR: Kaynağın kapanmaya başlayıp başlamadığı.</summary>
    private volatile bool _shuttingDown;

    /// <summary>EN: The background watches. TR: Arka plandaki izlemeler.</summary>
    private readonly Task[] _watches;

    /// <summary>
    /// EN: Starts watching <paramref name="resource"/>'s states and console lines.
    /// TR: <paramref name="resource"/>'un durumlarını ve konsol satırlarını izlemeye başlar.
    /// </summary>
    /// <param name="notifications">EN: Aspire's resource notifications. TR: Aspire'ın kaynak bildirimleri.</param>
    /// <param name="logs">EN: Aspire's resource logs. TR: Aspire'ın kaynak logları.</param>
    /// <param name="resource">EN: Resource name, e.g. "inventory". TR: Kaynak adı, ör. "inventory".</param>
    public ResourceStateLog(ResourceNotificationService notifications, ResourceLoggerService logs, string resource)
    {
        _watches =
        [
            Task.Run(() => WatchStatesAsync(notifications, logs, resource)),
        ];
    }

    /// <summary>
    /// EN: The recorded lines, in order.
    /// TR: Kaydedilen satırlar, sırasıyla.
    /// </summary>
    /// <returns>EN: The text. TR: Metin.</returns>
    public override string ToString() => string.Join(Environment.NewLine, _lines);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await Task.WhenAll(_watches);
        _stop.Dispose();
    }

    /// <summary>
    /// EN: Records state changes; for every new instance (resource id) it also starts watching that instance's console.
    /// TR: Durum değişikliklerini kaydeder; her yeni örnek (kaynak kimliği) için o örneğin konsolunu da izlemeye başlar.
    /// </summary>
    /// <param name="notifications">EN: Resource notifications. TR: Kaynak bildirimleri.</param>
    /// <param name="logs">EN: Resource logs. TR: Kaynak logları.</param>
    /// <param name="resource">EN: Resource name. TR: Kaynak adı.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task WatchStatesAsync(ResourceNotificationService notifications, ResourceLoggerService logs, string resource)
    {
        string? last = null;
        var consoles = new ConcurrentDictionary<string, Task>();
        try
        {
            await foreach (var change in notifications.WatchAsync(_stop.Token))
            {
                if (change.Resource.Name != resource)
                {
                    continue;
                }

                _ = consoles.GetOrAdd(change.ResourceId, id => Task.Run(() => WatchConsoleAsync(logs, id)));
                var snapshot = change.Snapshot;
                var pid = snapshot.Properties.FirstOrDefault(p => p.Name == "executable.pid")?.Value;
                var line = $"{change.ResourceId}: {snapshot.State?.Text ?? "(none)"}, exit {snapshot.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-"}, pid {pid ?? "-"}";
                if (line != last)
                {
                    Add(line);
                    last = line;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // EN: Disposed. TR: Kapatıldı.
        }

        await Task.WhenAll(consoles.Values);
    }

    /// <summary>
    /// EN: Records the console lines of one instance that are about its life (start, stop, signals, exceptions).
    /// TR: Bir örneğin yaşamıyla ilgili (başlama, durma, sinyaller, istisnalar) konsol satırlarını kaydeder.
    /// </summary>
    /// <param name="logs">EN: Resource logs. TR: Kaynak logları.</param>
    /// <param name="resourceId">EN: Instance id. TR: Örnek kimliği.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task WatchConsoleAsync(ResourceLoggerService logs, string resourceId)
    {
        try
        {
            await foreach (var batch in logs.WatchAsync(resourceId).WithCancellation(_stop.Token))
            {
                foreach (var entry in batch)
                {
                    // EN: Once shutdown starts, every line counts: the last one tells what it waits for.
                    // TR: Kapanış başlayınca her satır önemlidir: sonuncusu neyi beklediğini söyler.
                    _shuttingDown |= entry.Content.Contains("shutting down", StringComparison.OrdinalIgnoreCase);
                    if (_shuttingDown || _lifeWords.Any(w => entry.Content.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    {
                        Add($"{resourceId} console: {entry.Content.Trim()}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // EN: Disposed. TR: Kapatıldı.
        }
    }

    /// <summary>
    /// EN: Adds a line stamped with the elapsed time.
    /// TR: Geçen süreyle damgalanmış bir satır ekler.
    /// </summary>
    /// <param name="line">EN: The line. TR: Satır.</param>
    private void Add(string line) =>
        _lines.Enqueue($"{_clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),6}s {line}");
}
