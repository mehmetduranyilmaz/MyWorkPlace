using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Orders.Domain;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: Keeps the customer replica up to date from Customers' events (T-039). Each one is a single last-write-wins upsert,
///     so the order the events arrive in doesn't matter.
/// TR: Müşteri kopyasını Customers'ın olaylarından güncel tutar (T-039). Her biri tek bir "son yazan kazanır" upsert'idir; böylece olayların
///     geliş sırası önemli değildir.
/// </summary>
/// <param name="replicas">EN: The replica store. TR: Kopya deposu.</param>
public sealed class CustomerEventHandlers(CustomerReplicas replicas)
    : IEventHandler<CustomerCreated>, IEventHandler<CustomerUpdated>, IEventHandler<CustomerDeleted>
{
    /// <inheritdoc />
    public Task HandleAsync(CustomerCreated integrationEvent, CancellationToken cancellationToken) =>
        replicas.ApplyAsync(
            integrationEvent.TenantId, integrationEvent.CustomerId, integrationEvent.Name, integrationEvent.ChangedAt, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(CustomerUpdated integrationEvent, CancellationToken cancellationToken) =>
        replicas.ApplyAsync(
            integrationEvent.TenantId, integrationEvent.CustomerId, integrationEvent.Name, integrationEvent.ChangedAt, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(CustomerDeleted integrationEvent, CancellationToken cancellationToken) =>
        replicas.ApplyAsync(
            integrationEvent.TenantId, integrationEvent.CustomerId, null, integrationEvent.ChangedAt, cancellationToken);
}
