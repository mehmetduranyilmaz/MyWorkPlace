using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyWorkplace.BuildingBlocks.Domain;
using MyWorkplace.BuildingBlocks.Identity;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Runs just before changes are saved: assigns and protects <c>TenantId</c>, fills audit fields and turns
///     deletes of soft-deletable entities into updates.
/// TR: Değişiklikler kaydedilmeden hemen önce çalışır: <c>TenantId</c>'yi atar ve korur, denetim alanlarını doldurur,
///     soft-delete edilebilen entity'lerin silinmesini güncellemeye çevirir.
/// </summary>
/// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
/// <param name="timeProvider">EN: Clock (UTC). TR: Saat (UTC).</param>
public sealed class AuditingInterceptor(ICurrentUser currentUser, TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// EN: Applies the rules to every pending change.
    /// TR: Kuralları bekleyen her değişikliğe uygular.
    /// </summary>
    /// <param name="context">EN: The saving context. TR: Kaydeden context.</param>
    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    OnAdded(entry, now);
                    break;
                case EntityState.Modified:
                    OnModified(entry, now);
                    break;
                case EntityState.Deleted:
                    OnDeleted(entry, now);
                    break;
            }
        }
    }

    /// <summary>
    /// EN: New entity: assign the tenant and creation fields.
    /// TR: Yeni entity: firmayı ve oluşturma alanlarını atar.
    /// </summary>
    /// <param name="entry">EN: Change entry. TR: Değişiklik kaydı.</param>
    /// <param name="now">EN: Current time. TR: Şu anki zaman.</param>
    private void OnAdded(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is ITenantOwned owned && owned.TenantId == Guid.Empty)
        {
            // EN: An explicit TenantId (e.g. sign-up creating a new tenant) wins; otherwise the user's tenant is required.
            // TR: Açıkça verilen TenantId (ör. yeni firma oluşturan kayıt) önceliklidir; yoksa kullanıcının firması şarttır.
            owned.TenantId = currentUser.TenantId
                ?? throw new InvalidOperationException(
                    $"Cannot add {entry.Metadata.ClrType.Name}: no tenant is set and the current user has none.");
        }

        if (entry.Entity is IAuditable auditable)
        {
            auditable.CreatedAt = now;
            auditable.CreatedBy = currentUser.UserId;
        }
    }

    /// <summary>
    /// EN: Changed entity: block tenant changes, protect creation fields, stamp the update.
    /// TR: Değişen entity: firma değişikliğini engeller, oluşturma alanlarını korur, güncellemeyi işler.
    /// </summary>
    /// <param name="entry">EN: Change entry. TR: Değişiklik kaydı.</param>
    /// <param name="now">EN: Current time. TR: Şu anki zaman.</param>
    private void OnModified(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is ITenantOwned)
        {
            var tenant = entry.Property(nameof(ITenantOwned.TenantId));
            if (tenant.IsModified && !Equals(tenant.OriginalValue, tenant.CurrentValue))
            {
                throw new InvalidOperationException(
                    $"TenantId of {entry.Metadata.ClrType.Name} {entry.Property(nameof(Entity.Id)).CurrentValue} cannot be changed.");
            }
        }

        if (entry.Entity is IAuditable auditable)
        {
            entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
            entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
            auditable.UpdatedAt = now;
            auditable.UpdatedBy = currentUser.UserId;
        }
    }

    /// <summary>
    /// EN: Deleted entity: soft-deletable ones are kept and marked instead.
    /// TR: Silinen entity: soft-delete edilebilenler silinmez, işaretlenir.
    /// </summary>
    /// <param name="entry">EN: Change entry. TR: Değişiklik kaydı.</param>
    /// <param name="now">EN: Current time. TR: Şu anki zaman.</param>
    private void OnDeleted(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is not ISoftDeletable deletable)
        {
            return;
        }

        entry.State = EntityState.Modified;
        deletable.IsDeleted = true;
        deletable.DeletedAt = now;
        KeepOwnedParts(entry);
        OnModified(entry, now);
    }

    /// <summary>
    /// EN: Removing an owner also marks its owned parts (e.g. an order's lines) as deleted. A soft-deleted owner is
    ///     kept, so its parts must be kept too — otherwise the history would lose them.
    /// TR: Bir sahibi silmek sahip olunan parçalarını da (ör. bir siparişin satırları) silinmiş işaretler. Soft-delete edilen sahip
    ///     korunduğuna göre parçaları da korunmalıdır — aksi halde geçmiş onları kaybederdi.
    /// </summary>
    /// <param name="owner">EN: The soft-deleted owner. TR: Soft-delete edilen sahip.</param>
    private static void KeepOwnedParts(EntityEntry owner)
    {
        foreach (var navigation in owner.Navigations.Where(n => n.Metadata.TargetEntityType.IsOwned()))
        {
            IEnumerable<object> parts = navigation switch
            {
                CollectionEntry collection => collection.CurrentValue?.Cast<object>() ?? [],
                _ => navigation.CurrentValue is { } part ? [part] : [],
            };

            foreach (var part in parts)
            {
                var partEntry = owner.Context.Entry(part);
                if (partEntry.State == EntityState.Deleted)
                {
                    partEntry.State = EntityState.Unchanged;
                }
            }
        }
    }
}
