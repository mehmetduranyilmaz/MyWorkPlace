namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: The default roles every company has (ADR-022): named sets of permissions, defined in code.
/// TR: Her firmada bulunan varsayılan roller (ADR-022): kodda tanımlı, isimli izin kümeleri.
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

    /// <summary>EN: Permissions every role above Viewer can use to read. TR: Okuma izinleri.</summary>
    private static readonly string[] _read = [Permissions.Customers.Read, Permissions.Inventory.Read, Permissions.Orders.Read];

    /// <summary>EN: Create and edit permissions. TR: Oluşturma ve düzenleme izinleri.</summary>
    private static readonly string[] _write = [Permissions.Customers.Write, Permissions.Inventory.Write, Permissions.Orders.Write];

    /// <summary>EN: Destructive permissions. TR: Geri alınması zor (silme) izinleri.</summary>
    private static readonly string[] _delete = [Permissions.Customers.Delete, Permissions.Inventory.Delete, Permissions.Orders.Delete];

    /// <summary>EN: Administration permissions. TR: Yönetim izinleri.</summary>
    private static readonly string[] _administration = [Permissions.UsersManage, Permissions.SettingsManage];

    /// <summary>
    /// EN: Role → permissions: the matrix of ADR-022, in one place.
    /// TR: Rol → izinler: ADR-022'deki tablo, tek yerde.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlySet<string>> _matrix = new(StringComparer.Ordinal)
    {
        [Owner] = Set(_read, _write, _delete, _administration, [Permissions.PlanManage]),
        [Admin] = Set(_read, _write, _delete, _administration),
        [Member] = Set(_read, _write),
        [Viewer] = Set(_read),
    };

    /// <summary>EN: Every known role. TR: Bilinen tüm roller.</summary>
    public static IReadOnlyCollection<string> All => _matrix.Keys;

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

    /// <summary>
    /// EN: Builds a permission set from groups.
    /// TR: Gruplardan bir izin kümesi oluşturur.
    /// </summary>
    /// <param name="groups">EN: Permission groups. TR: İzin grupları.</param>
    /// <returns>EN: The set. TR: Küme.</returns>
    private static HashSet<string> Set(params string[][] groups) => new(groups.SelectMany(g => g), StringComparer.Ordinal);
}
