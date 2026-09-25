namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: The default roles every company has (ADR-022). Their permissions follow a convention, not a hand-kept list
///     (ADR-025): a module permission's action — the part after the last dot — decides who gets it. A new module
///     therefore never edits this file.
/// TR: Her firmada bulunan varsayılan roller (ADR-022). İzinleri elle tutulan bir listeye değil bir kurala uyar (ADR-025): bir modül
///     izninin işlemi — son noktadan sonraki kısım — onu kimin alacağını belirler. Bu yüzden yeni bir modül bu dosyayı asla düzenlemez.
/// </summary>
public static class Roles
{
    /// <summary>EN: Everything, including the plan. TR: Plan dahil her şey.</summary>
    public const string Owner = "Owner";

    /// <summary>EN: Everything except the plan. TR: Plan hariç her şey.</summary>
    public const string Admin = "Admin";

    /// <summary>EN: Day-to-day work: read and write, no delete, no administration. TR: Günlük iş: okuma ve yazma; silme ve yönetim yok.</summary>
    public const string Member = "Member";

    /// <summary>EN: Read only. TR: Sadece okuma.</summary>
    public const string Viewer = "Viewer";

    /// <summary>
    /// EN: Role → permissions, derived once from <see cref="Grants"/> over the whole catalog.
    /// TR: Rol → izinler; tüm katalog üzerinde <see cref="Grants"/>'ten bir kez türetilir.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlySet<string>> _matrix =
        new[] { Owner, Admin, Member, Viewer }.ToDictionary(
            role => role,
            role => (IReadOnlySet<string>)new HashSet<string>(
                Permissions.All.Where(permission => Grants(role, permission)), StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>EN: Every known role. TR: Bilinen tüm roller.</summary>
    public static IReadOnlyCollection<string> All => _matrix.Keys;

    /// <summary>
    /// EN: The convention (ADR-025). Owner gets everything; Admin everything but the plan; Member and Viewer only module
    ///     permissions — Member those ending in <c>.read</c> or <c>.write</c>, Viewer those ending in <c>.read</c>. Any other
    ///     action (e.g. <c>products.export</c>) goes to Owner and Admin only: least privilege until a module decides more.
    /// TR: Kural (ADR-025). Sahip her şeyi alır; Yönetici plan dışında her şeyi; Çalışan ve İzleyici sadece modül izinlerini — Çalışan
    ///     <c>.read</c> veya <c>.write</c> ile bitenleri, İzleyici <c>.read</c> ile bitenleri. Başka her işlem (ör. <c>products.export</c>) sadece
    ///     Sahip ve Yöneticiye gider: bir modül fazlasına karar verene kadar en az yetki.
    /// </summary>
    /// <param name="role">EN: Role name. TR: Rol adı.</param>
    /// <param name="permission">EN: Permission name. TR: İzin adı.</param>
    /// <returns>EN: True if the role grants the permission. TR: Rol izni veriyorsa true.</returns>
    public static bool Grants(string role, string permission)
    {
        var isModulePermission = !Permissions.Administrative.Contains(permission);
        var action = permission[(permission.LastIndexOf('.') + 1)..];
        return role switch
        {
            Owner => true,
            Admin => permission != Permissions.PlanManage,
            Member => isModulePermission && action is "read" or "write",
            Viewer => isModulePermission && action is "read",
            _ => false,
        };
    }

    /// <summary>
    /// EN: Effective permissions of a user: the union of the roles' permissions and the extra grants (grant only, no deny).
    ///     Unknown roles and permissions are ignored, so stale data can never grant anything.
    /// TR: Bir kullanıcının etkin izinleri: rollerin izinleri ile ek izinlerin birleşimi (sadece verme, yasak yok).
    ///     Bilinmeyen roller ve izinler yok sayılır; böylece eskimiş veri asla bir şey vermez.
    /// </summary>
    /// <param name="roles">EN: Assigned roles. TR: Atanmış roller.</param>
    /// <param name="extraPermissions">EN: Extra grants. TR: Ek izinler.</param>
    /// <returns>EN: Sorted permission names. TR: Sıralı izin adları.</returns>
    public static IReadOnlyList<string> EffectivePermissions(IEnumerable<string> roles, IEnumerable<string> extraPermissions) =>
    [
        .. roles
            .SelectMany(role => _matrix.TryGetValue(role, out var permissions) ? permissions : Enumerable.Empty<string>())
            .Concat(extraPermissions.Where(Permissions.All.Contains))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];
}
