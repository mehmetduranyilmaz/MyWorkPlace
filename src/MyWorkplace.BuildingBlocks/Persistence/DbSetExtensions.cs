using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Query helpers for the no-tracking-by-default rule (ADR-011).
/// TR: Varsayılan olarak takipsiz okuma kuralı için sorgu yardımcıları (ADR-011).
/// </summary>
public static class DbSetExtensions
{
    /// <summary>
    /// EN: Loads an entity <b>tracked</b>, ready to be modified and saved. Always use this before an update:
    ///     an entity loaded by a normal query is untracked and its changes would be silently ignored.
    ///     Tenant and soft-delete filters still apply.
    /// TR: Bir entity'yi değiştirilip kaydedilmeye hazır, <b>takip edilen</b> haliyle yükler. Güncellemeden önce her zaman
    ///     bu kullanılır: normal sorguyla yüklenen entity takip edilmez ve değişiklikleri sessizce yok sayılır.
    ///     Firma ve soft-delete filtreleri yine uygulanır.
    /// </summary>
    /// <typeparam name="TEntity">EN: Entity type. TR: Entity tipi.</typeparam>
    /// <param name="set">EN: The entity set. TR: Entity kümesi.</param>
    /// <param name="id">EN: Entity id. TR: Entity kimliği.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The tracked entity, or null if not found. TR: Takip edilen entity; bulunamazsa null.</returns>
    public static Task<TEntity?> FindForUpdateAsync<TEntity>(
        this DbSet<TEntity> set,
        Guid id,
        CancellationToken cancellationToken = default)
        where TEntity : Entity =>
        set.AsTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
}
