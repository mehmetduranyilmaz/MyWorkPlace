namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: One recorded change of one <c>[AuditChanges]</c> property. Append-only; each service keeps its own table.
/// TR: Bir <c>[AuditChanges]</c> alanının kaydedilmiş tek bir değişikliği. Sadece eklenir; her servis kendi tablosunu tutar.
/// </summary>
public sealed class AuditLogEntry
{
    /// <summary>
    /// EN: Entry id (UUID v7, so entries sort by time).
    /// TR: Kayıt kimliği (UUID v7; kayıtlar zamana göre sıralanır).
    /// </summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// EN: CLR name of the changed entity, e.g. "Customer".
    /// TR: Değişen entity'nin CLR adı, ör. "Customer".
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// EN: Id of the changed entity.
    /// TR: Değişen entity'nin kimliği.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// EN: Name of the changed property.
    /// TR: Değişen alanın adı.
    /// </summary>
    public required string Property { get; init; }

    /// <summary>
    /// EN: Value before the change, as invariant-culture text.
    /// TR: Değişiklikten önceki değer, kültürden bağımsız metin olarak.
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// EN: Value after the change, as invariant-culture text.
    /// TR: Değişiklikten sonraki değer, kültürden bağımsız metin olarak.
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// EN: User who made the change.
    /// TR: Değişikliği yapan kullanıcı.
    /// </summary>
    public Guid? ChangedBy { get; init; }

    /// <summary>
    /// EN: Company of the changed entity, when it is tenant-owned.
    /// TR: Değişen entity firmaya aitse o firma.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// EN: Time of the change (UTC).
    /// TR: Değişiklik zamanı (UTC).
    /// </summary>
    public required DateTimeOffset ChangedAt { get; init; }
}
