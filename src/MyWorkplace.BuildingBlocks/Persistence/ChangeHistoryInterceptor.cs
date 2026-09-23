using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyWorkplace.BuildingBlocks.Domain;
using MyWorkplace.BuildingBlocks.Identity;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Writes an <see cref="AuditLogEntry"/> for every changed <c>[AuditChanges]</c> property. The entries are added
///     to the same SaveChanges call, so they are committed in the same transaction as the change itself.
/// TR: Değişen her <c>[AuditChanges]</c> alanı için bir <see cref="AuditLogEntry"/> yazar. Kayıtlar aynı SaveChanges
///     çağrısına eklenir; böylece değişikliğin kendisiyle aynı transaction içinde kaydedilir.
/// </summary>
/// <param name="currentUser">EN: The current user. TR: Aktif kullanıcı.</param>
/// <param name="timeProvider">EN: Clock (UTC). TR: Saat (UTC).</param>
public sealed class ChangeHistoryInterceptor(ICurrentUser currentUser, TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// EN: Collects the changed marked properties of modified entities and adds log entries for them.
    /// TR: Değişen entity'lerin işaretli ve değişmiş alanlarını toplar ve onlar için günlük kayıtları ekler.
    /// </summary>
    /// <param name="context">EN: The saving context. TR: Kaydeden context.</param>
    private void Record(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var entries = new List<AuditLogEntry>();

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            foreach (var property in entry.Properties)
            {
                if (!property.IsModified
                    || property.Metadata.PropertyInfo?.GetCustomAttribute<AuditChangesAttribute>() is null
                    || Equals(property.OriginalValue, property.CurrentValue))
                {
                    continue;
                }

                entries.Add(new AuditLogEntry
                {
                    EntityType = entry.Metadata.ClrType.Name,
                    EntityId = entry.Entity.Id,
                    Property = property.Metadata.Name,
                    OldValue = Format(property.OriginalValue),
                    NewValue = Format(property.CurrentValue),
                    ChangedBy = currentUser.UserId,
                    TenantId = (entry.Entity as ITenantOwned)?.TenantId,
                    ChangedAt = now,
                });
            }
        }

        context.Set<AuditLogEntry>().AddRange(entries);
    }

    /// <summary>
    /// EN: Converts a value to culture-independent text so logs read the same on every machine.
    /// TR: Değeri kültürden bağımsız metne çevirir; böylece günlük her makinede aynı okunur.
    /// </summary>
    /// <param name="value">EN: Value to format. TR: Biçimlendirilecek değer.</param>
    /// <returns>EN: Text or null. TR: Metin veya null.</returns>
    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
