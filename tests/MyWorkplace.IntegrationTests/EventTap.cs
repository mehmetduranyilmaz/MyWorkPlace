using System.Collections.Concurrent;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Listens to one event exchange on the running system's RabbitMQ with a private, temporary queue — the way any
///     interested service would — so tests can prove an event was really published, not just stored.
/// TR: Çalışan sistemin RabbitMQ'sunda tek bir olay exchange'ini özel ve geçici bir kuyrukla dinler — ilgilenen herhangi bir
///     servisin yapacağı gibi; böylece testler bir olayın sadece saklandığını değil gerçekten yayınlandığını kanıtlar.
/// </summary>
internal sealed class EventTap : IAsyncDisposable
{
    /// <summary>EN: Received message bodies. TR: Alınan mesaj gövdeleri.</summary>
    private readonly ConcurrentQueue<JsonElement> _received = new();

    /// <summary>EN: Broker connection. TR: Mesaj aracı bağlantısı.</summary>
    private readonly IConnection _connection;

    /// <summary>EN: Channel of the tap queue. TR: Dinleme kuyruğunun kanalı.</summary>
    private readonly IChannel _channel;

    /// <summary>
    /// EN: Wraps an open connection and channel.
    /// TR: Açık bir bağlantıyı ve kanalı sarar.
    /// </summary>
    /// <param name="connection">EN: Connection. TR: Bağlantı.</param>
    /// <param name="channel">EN: Channel. TR: Kanal.</param>
    private EventTap(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    /// <summary>
    /// EN: Starts listening to <paramref name="exchange"/>. Declares it the way the publisher does (durable fanout), so the
    ///     tap works even before the first event is published.
    /// TR: <paramref name="exchange"/>'i dinlemeye başlar. Onu yayınlayanın yaptığı gibi tanımlar (kalıcı fanout); böylece dinleyici
    ///     ilk olay yayınlanmadan önce de çalışır.
    /// </summary>
    /// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
    /// <param name="exchange">EN: Exchange name, e.g. "OrderPlaced". TR: Exchange adı, ör. "OrderPlaced".</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The tap. TR: Dinleyici.</returns>
    public static async Task<EventTap> StartAsync(AppFixture app, string exchange, CancellationToken ct)
    {
        var connectionString = await app.App.GetConnectionStringAsync("messaging", ct)
            ?? throw new InvalidOperationException("The messaging connection string is missing.");
        var connection = await new ConnectionFactory { Uri = new Uri(connectionString) }.CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        var queue = await channel.QueueDeclareAsync(
            queue: "", durable: false, exclusive: true, autoDelete: true, cancellationToken: ct);
        await channel.QueueBindAsync(queue.QueueName, exchange, routingKey: "", cancellationToken: ct);

        var tap = new EventTap(connection, channel);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            tap._received.Enqueue(JsonDocument.Parse(delivery.Body).RootElement.Clone());
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, ct);
        return tap;
    }

    /// <summary>
    /// EN: Waits for a received message matching <paramref name="match"/>.
    /// TR: <paramref name="match"/> ile eşleşen bir mesajın gelmesini bekler.
    /// </summary>
    /// <param name="match">EN: Predicate on the JSON body. TR: JSON gövdesi üzerinde koşul.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The message. TR: Mesaj.</returns>
    public async Task<JsonElement> WaitForAsync(Func<JsonElement, bool> match, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            if (_received.FirstOrDefault(match) is { ValueKind: not JsonValueKind.Undefined } found)
            {
                return found;
            }

            Assert.True(DateTime.UtcNow < deadline, "The expected event was not published in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
