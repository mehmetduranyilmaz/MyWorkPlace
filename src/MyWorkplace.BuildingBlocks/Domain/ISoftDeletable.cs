namespace MyWorkplace.BuildingBlocks.Domain;

/// <summary>
/// EN: Deleting marks the row instead of removing it; marked rows are hidden from queries by a filter.
/// TR: Silme işlemi satırı kaldırmak yerine işaretler; işaretli satırlar bir filtreyle sorgulardan gizlenir.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>
    /// EN: True once the entity has been deleted.
    /// TR: Entity silindiğinde true olur.
    /// </summary>
    bool IsDeleted { get; set; }

    /// <summary>
    /// EN: Deletion time (UTC).
    /// TR: Silinme zamanı (UTC).
    /// </summary>
    DateTimeOffset? DeletedAt { get; set; }
}
