using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWorkplace.BuildingBlocks.Messaging;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: The messaging building block (T-015, ADR-023) end to end: outbox, delivery through a real RabbitMQ, broker
///     outage, duplicates and tenant context.
/// TR: Mesajlaşma yapı taşı (T-015, ADR-023) uçtan uca: outbox, gerçek bir RabbitMQ üzerinden teslim, mesaj aracı kesintisi,
///     tekrarlar ve firma bağlamı.
/// </summary>
/// <param name="messaging">EN: Host with messaging. TR: Mesajlaşmalı host.</param>
public sealed class MessagingTests(MessagingFixture messaging) : IClassFixture<MessagingFixture>
{
    /// <summary>EN: How long a delivery may take. TR: Bir teslimin ne kadar sürebileceği.</summary>
    private static readonly TimeSpan _deliveryTimeout = TimeSpan.FromSeconds(60);

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CommittedChange_EventIsDelivered()
    {
        var tenantId = Guid.CreateVersion7();
        var noteRequested = Event(tenantId, "delivered");

        await SaveWithEventAsync(tenantId, "source", noteRequested);

        await WaitUntilProcessedAsync(noteRequested.EventId);
        Assert.Equal(1, await CountNotesAsync(tenantId, noteRequested.Title));
    }

    [Fact]
    public async Task RolledBackChange_SendsNothing()
    {
        var tenantId = Guid.CreateVersion7();
        var lost = Event(tenantId, "never");
        var sentinel = Event(tenantId, "sentinel");

        // EN: The title is longer than its column, so the business change fails and takes the event with it.
        // TR: Başlık sütunundan uzun; iş değişikliği başarısız olur ve olayı da beraberinde götürür.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => SaveWithEventAsync(tenantId, new string('x', 300), lost));
        await SaveWithEventAsync(tenantId, "ok", sentinel);

        await WaitUntilProcessedAsync(sentinel.EventId);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        Assert.False(await IsProcessedAsync(lost.EventId));
    }

    [Fact]
    public async Task BrokerDown_EventIsDeliveredWhenItReturns()
    {
        var tenantId = Guid.CreateVersion7();
        var noteRequested = Event(tenantId, "after-outage");

        await messaging.Broker.PauseAsync(Ct);
        try
        {
            // EN: The business change must succeed although the broker is unreachable.
            // TR: Mesaj aracına ulaşılamasa da iş değişikliği başarılı olmalı.
            await SaveWithEventAsync(tenantId, "saved-during-outage", noteRequested);
            Assert.Equal(1, await CountNotesAsync(tenantId, "saved-during-outage"));
            await Task.Delay(TimeSpan.FromSeconds(3), Ct);
            Assert.False(await IsProcessedAsync(noteRequested.EventId));
        }
        finally
        {
            await messaging.Broker.UnpauseAsync(Ct);
        }

        await WaitUntilProcessedAsync(noteRequested.EventId, TimeSpan.FromSeconds(120));
        Assert.Equal(1, await CountNotesAsync(tenantId, noteRequested.Title));
    }

    [Fact]
    public async Task SameEventDeliveredTwice_IsProcessedOnce()
    {
        var tenantId = Guid.CreateVersion7();
        var noteRequested = Event(tenantId, "once");

        // EN: Twice through the broker (two sends of the same event), then twice straight into the dispatcher.
        // TR: İki kez mesaj aracı üzerinden (aynı olayın iki gönderimi), sonra iki kez doğrudan dağıtıcıya.
        await SaveWithEventAsync(tenantId, "first", noteRequested);
        await SaveWithEventAsync(tenantId, "second", noteRequested);
        await WaitUntilProcessedAsync(noteRequested.EventId);
        var dispatcher = ActivatorUtilities.CreateInstance<EventDispatcher<NoteRequested>>(messaging.Services);
        await dispatcher.Handle(noteRequested, Ct);
        await dispatcher.Handle(noteRequested, Ct);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);

        Assert.Equal(1, await CountNotesAsync(tenantId, noteRequested.Title));
    }

    [Fact]
    public async Task Consumer_SeesAndWritesOnlyTheEventsTenant()
    {
        var tenantId = Guid.CreateVersion7();
        var otherTenantId = Guid.CreateVersion7();
        await SaveWithEventAsync(otherTenantId, "someone-else", integrationEvent: null);
        var noteRequested = Event(tenantId, "isolated");

        await SaveWithEventAsync(tenantId, "mine", noteRequested);
        await WaitUntilProcessedAsync(noteRequested.EventId);

        var probe = messaging.Services.GetRequiredService<TenantProbe>();
        Assert.Equal([tenantId], probe.VisibleTenants[noteRequested.EventId]);
        await using var scope = messaging.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<TestDbContext>().Notes
            .IgnoreQueryFilters()
            .SingleAsync(n => n.Title == noteRequested.Title, Ct);
        Assert.Equal(tenantId, created.TenantId);
        Assert.Null(created.CreatedBy);
    }

    /// <summary>
    /// EN: A note request for <paramref name="tenantId"/> with a title no other test uses.
    /// TR: <paramref name="tenantId"/> için başka hiçbir testin kullanmadığı başlıkla bir not isteği.
    /// </summary>
    /// <param name="tenantId">EN: Tenant. TR: Firma.</param>
    /// <param name="name">EN: Readable part of the title. TR: Başlığın okunur kısmı.</param>
    /// <returns>EN: The event. TR: Olay.</returns>
    private static NoteRequested Event(Guid tenantId, string name) =>
        new() { TenantId = tenantId, Title = $"{name}-{Guid.NewGuid():N}" };

    /// <summary>
    /// EN: Saves a note of <paramref name="tenantId"/> and, if given, an event in the same transaction.
    /// TR: <paramref name="tenantId"/> firmasına bir not ve verilmişse aynı transaction'da bir olay kaydeder.
    /// </summary>
    /// <param name="tenantId">EN: Tenant. TR: Firma.</param>
    /// <param name="title">EN: Note title. TR: Not başlığı.</param>
    /// <param name="integrationEvent">EN: Event, or null. TR: Olay veya null.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task SaveWithEventAsync(Guid tenantId, string title, NoteRequested? integrationEvent)
    {
        await using var scope = messaging.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();

        db.Notes.Add(new TestNote { Title = title, TenantId = tenantId });
        if (integrationEvent is not null)
        {
            await outbox.AddAsync(integrationEvent);
        }

        await outbox.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// EN: Counts notes with a title, across tenants.
    /// TR: Bir başlığa sahip notları firmalar arası sayar.
    /// </summary>
    /// <param name="tenantId">EN: Tenant. TR: Firma.</param>
    /// <param name="title">EN: Title. TR: Başlık.</param>
    /// <returns>EN: The count. TR: Sayı.</returns>
    private async Task<int> CountNotesAsync(Guid tenantId, string title)
    {
        await using var scope = messaging.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Notes
            .IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == tenantId && n.Title == title, Ct);
    }

    /// <summary>
    /// EN: Whether the event has been processed by the consumer.
    /// TR: Olayın dinleyici tarafından işlenip işlenmediği.
    /// </summary>
    /// <param name="eventId">EN: Event id. TR: Olay kimliği.</param>
    /// <returns>EN: True if processed. TR: İşlendiyse true.</returns>
    private async Task<bool> IsProcessedAsync(Guid eventId)
    {
        await using var scope = messaging.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().ProcessedEvents
            .AnyAsync(e => e.EventId == eventId, Ct);
    }

    /// <summary>
    /// EN: Polls until the event is processed; fails the test after the timeout.
    /// TR: Olay işlenene kadar yoklar; süre dolunca testi düşürür.
    /// </summary>
    /// <param name="eventId">EN: Event id. TR: Olay kimliği.</param>
    /// <param name="timeout">EN: Optional timeout. TR: İsteğe bağlı süre sınırı.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task WaitUntilProcessedAsync(Guid eventId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? _deliveryTimeout);
        while (!await IsProcessedAsync(eventId))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Event {eventId} was not processed in time.");
            await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        }
    }
}
