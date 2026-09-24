namespace MyWorkplace.BuildingBlocks.Identity;

/// <summary>
/// EN: The company an incoming event is being processed for (ADR-023). Set once per message by the dispatcher; empty
///     during HTTP requests.
/// TR: Gelen bir olayın hangi firma için işlendiği (ADR-023). Dağıtıcı tarafından her mesajda bir kez atanır; HTTP isteklerinde boştur.
/// </summary>
public sealed class MessageActor
{
    /// <summary>EN: The event's tenant, or null. TR: Olayın firması veya null.</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// EN: Starts acting for <paramref name="tenantId"/>; can happen only once per scope.
    /// TR: <paramref name="tenantId"/> adına çalışmaya başlar; bir kapsamda sadece bir kez olabilir.
    /// </summary>
    /// <param name="tenantId">EN: The event's tenant. TR: Olayın firması.</param>
    public void ActFor(Guid tenantId)
    {
        if (TenantId is not null)
        {
            throw new InvalidOperationException("The acting tenant of a message scope can't change.");
        }

        TenantId = tenantId;
    }
}

/// <summary>
/// EN: The current user of a service: the token's user during a request, or the system actor of an event's tenant while
///     an event is processed — so tenant filters and audit fields work the same in both (ADR-004, ADR-023).
/// TR: Bir servisin aktif kullanıcısı: istek sırasında token'ın kullanıcısı, olay işlenirken olayın firmasının sistem kullanıcısı —
///     böylece firma filtreleri ve denetim alanları ikisinde de aynı çalışır (ADR-004, ADR-023).
/// </summary>
/// <param name="request">EN: The request's user. TR: İsteğin kullanıcısı.</param>
/// <param name="actor">EN: The message's tenant. TR: Mesajın firması.</param>
public sealed class ServiceCurrentUser(HttpCurrentUser request, MessageActor actor) : ICurrentUser
{
    /// <summary>
    /// EN: The request's user; the system actor has none (audit fields show "system").
    /// TR: İsteğin kullanıcısı; sistem kullanıcısının kimliği yoktur (denetim alanları "sistem" gösterir).
    /// </summary>
    public Guid? UserId => actor.TenantId is null ? request.UserId : null;

    /// <inheritdoc />
    public Guid? TenantId => actor.TenantId ?? request.TenantId;

    /// <inheritdoc />
    public string? Plan => actor.TenantId is null ? request.Plan : null;
}
