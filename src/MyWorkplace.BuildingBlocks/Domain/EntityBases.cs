namespace MyWorkplace.BuildingBlocks.Domain;

/// <summary>
/// EN: An entity with audit fields, filled by the auditing interceptor (ADR-011, ADR-021).
/// TR: Denetim alanları olan, bu alanları denetim interceptor'ının doldurduğu entity (ADR-011, ADR-021).
/// </summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// EN: An audited entity that belongs to one company; queries see only the current company's rows (ADR-004).
/// TR: Tek bir firmaya ait, denetlenen entity; sorgular sadece aktif firmanın satırlarını görür (ADR-004).
/// </summary>
public abstract class TenantOwnedEntity : AuditableEntity, ITenantOwned
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>
/// EN: The usual base of business data: tenant-owned, audited and soft-deletable. A new module's entity typically
///     starts as <c>class Product : BusinessEntity</c> and contains only its own fields and rules.
/// TR: İş verilerinin olağan temeli: firmaya ait, denetlenen ve soft-delete edilebilen. Yeni bir modülün entity'si genelde
///     <c>class Product : BusinessEntity</c> olarak başlar ve sadece kendi alanlarını ve kurallarını içerir.
/// </summary>
public abstract class BusinessEntity : TenantOwnedEntity, ISoftDeletable
{
    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }
}
