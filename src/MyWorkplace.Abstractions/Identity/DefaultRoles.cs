namespace MyWorkplace.Abstractions.Identity;

/// <summary>
/// EN: The default roles every company has (ADR-022). What each role may do is decided by
///     <see cref="PermissionCatalog.Grants"/> over the product's catalog (ADR-025).
/// TR: Her firmada bulunan varsayılan roller (ADR-022). Her rolün neler yapabileceğine ürünün kataloğu üzerinde
///     <see cref="PermissionCatalog.Grants"/> karar verir (ADR-025).
/// </summary>
public static class DefaultRoles
{
    /// <summary>EN: Everything, including owner-only permissions. TR: Sadece Sahibe ait izinler dahil her şey.</summary>
    public const string Owner = "Owner";

    /// <summary>EN: Everything except owner-only permissions. TR: Sadece Sahibe ait izinler hariç her şey.</summary>
    public const string Admin = "Admin";

    /// <summary>EN: Day-to-day work: read and write, no delete, no administration. TR: Günlük iş: okuma ve yazma; silme ve yönetim yok.</summary>
    public const string Member = "Member";

    /// <summary>EN: Read only. TR: Sadece okuma.</summary>
    public const string Viewer = "Viewer";

    /// <summary>EN: Every default role. TR: Tüm varsayılan roller.</summary>
    public static IReadOnlyList<string> All { get; } = [Owner, Admin, Member, Viewer];
}
