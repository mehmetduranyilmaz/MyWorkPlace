using System.Reflection;

namespace MyWorkplace.Abstractions.Identity;

/// <summary>
/// EN: A product's permissions and the convention that maps them to the default roles (ADR-022, ADR-025, ADR-026).
///     Built from a static catalog class: every string constant of a nested class is a <b>module</b> permission
///     (<c>customers.read</c>), a constant on the class itself is an <b>administrative</b> one, and <see cref="OwnerOnlyAttribute"/>
///     marks the administrative ones only the Owner gets. The product registers its catalog explicitly.
/// TR: Bir ürünün izinleri ve onları varsayılan rollere eşleyen kural (ADR-022, ADR-025, ADR-026). Statik bir katalog sınıfından kurulur:
///     bir iç sınıfın her string sabiti bir <b>modül</b> iznidir (<c>customers.read</c>), sınıfın kendisindeki bir sabit bir <b>yönetim</b>
///     iznidir ve <see cref="OwnerOnlyAttribute"/> sadece Sahibin aldığı yönetim izinlerini işaretler. Ürün kataloğunu açıkça kaydeder.
/// </summary>
public sealed class PermissionCatalog
{
    /// <summary>EN: Role → permissions, derived once. TR: Rol → izinler; bir kez türetilir.</summary>
    private readonly Dictionary<string, IReadOnlySet<string>> _matrix;

    /// <summary>
    /// EN: Creates a catalog from the three permission groups.
    /// TR: Üç izin grubundan bir katalog oluşturur.
    /// </summary>
    /// <param name="modules">EN: Module permissions. TR: Modül izinleri.</param>
    /// <param name="administrative">EN: Administrative permissions. TR: Yönetim izinleri.</param>
    /// <param name="ownerOnly">EN: Administrative permissions only the Owner gets. TR: Sadece Sahibin aldığı yönetim izinleri.</param>
    private PermissionCatalog(IReadOnlySet<string> modules, IReadOnlySet<string> administrative, IReadOnlySet<string> ownerOnly)
    {
        Modules = modules;
        Administrative = administrative;
        OwnerOnly = ownerOnly;
        All = new HashSet<string>(modules.Concat(administrative), StringComparer.Ordinal);
        _matrix = DefaultRoles.All.ToDictionary(
            role => role,
            role => (IReadOnlySet<string>)new HashSet<string>(All.Where(p => Grants(role, p)), StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>EN: Permissions declared by modules. TR: Modüllerin bildirdiği izinler.</summary>
    public IReadOnlySet<string> Modules { get; }

    /// <summary>EN: Administrative permissions. TR: Yönetim izinleri.</summary>
    public IReadOnlySet<string> Administrative { get; }

    /// <summary>EN: Administrative permissions only the Owner gets. TR: Sadece Sahibin aldığı yönetim izinleri.</summary>
    public IReadOnlySet<string> OwnerOnly { get; }

    /// <summary>
    /// EN: Every known permission. A policy name outside this set never authorizes anyone.
    /// TR: Bilinen tüm izinler. Bu kümenin dışındaki bir politika adı kimseye asla yetki vermez.
    /// </summary>
    public IReadOnlySet<string> All { get; }

    /// <summary>
    /// EN: Reads a static catalog class. Fails if it doesn't declare the permissions the core itself uses
    ///     (<see cref="CorePermissions"/>), so a missing one is found at startup, not at the first request.
    /// TR: Statik bir katalog sınıfını okur. Çekirdeğin kendisinin kullandığı izinleri (<see cref="CorePermissions"/>) bildirmiyorsa hata verir;
    ///     böylece eksik bir izin ilk istekte değil açılışta bulunur.
    /// </summary>
    /// <param name="catalogType">EN: The product's catalog class. TR: Ürünün katalog sınıfı.</param>
    /// <returns>EN: The catalog. TR: Katalog.</returns>
    public static PermissionCatalog FromType(Type catalogType)
    {
        ArgumentNullException.ThrowIfNull(catalogType);

        var modules = ConstantsOf(catalogType.GetNestedTypes(BindingFlags.Public).SelectMany(Fields));
        var administrativeFields = Fields(catalogType).ToList();
        var administrative = ConstantsOf(administrativeFields);
        var ownerOnly = ConstantsOf(administrativeFields.Where(f => f.IsDefined(typeof(OwnerOnlyAttribute))));

        var missing = CorePermissions.All.Where(p => !administrative.Contains(p)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The permission catalog {catalogType.Name} must declare the core permissions it protects: " +
                $"{string.Join(", ", missing)} (see CorePermissions).");
        }

        return new PermissionCatalog(modules, administrative, ownerOnly);
    }

    /// <summary>
    /// EN: The convention (ADR-025). Owner gets everything; Admin everything but owner-only permissions; Member and Viewer
    ///     only module permissions — Member those ending in <c>.read</c> or <c>.write</c>, Viewer those ending in <c>.read</c>.
    ///     Any other action (e.g. <c>products.export</c>) goes to Owner and Admin only: least privilege.
    /// TR: Kural (ADR-025). Sahip her şeyi alır; Yönetici sadece Sahibe ait izinler dışında her şeyi; Çalışan ve İzleyici sadece modül izinlerini —
    ///     Çalışan <c>.read</c> veya <c>.write</c> ile bitenleri, İzleyici <c>.read</c> ile bitenleri. Başka her işlem (ör. <c>products.export</c>)
    ///     sadece Sahip ve Yöneticiye gider: en az yetki.
    /// </summary>
    /// <param name="role">EN: Role name. TR: Rol adı.</param>
    /// <param name="permission">EN: Permission name. TR: İzin adı.</param>
    /// <returns>EN: True if the role grants the permission. TR: Rol izni veriyorsa true.</returns>
    public bool Grants(string role, string permission)
    {
        var isModulePermission = !Administrative.Contains(permission);
        var action = permission[(permission.LastIndexOf('.') + 1)..];
        return role switch
        {
            DefaultRoles.Owner => true,
            DefaultRoles.Admin => !OwnerOnly.Contains(permission),
            DefaultRoles.Member => isModulePermission && action is "read" or "write",
            DefaultRoles.Viewer => isModulePermission && action is "read",
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
    public IReadOnlyList<string> EffectivePermissions(IEnumerable<string> roles, IEnumerable<string> extraPermissions) =>
    [
        .. roles
            .SelectMany(role => _matrix.TryGetValue(role, out var permissions) ? permissions : Enumerable.Empty<string>())
            .Concat(extraPermissions.Where(All.Contains))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// EN: The public static fields declared directly on a type.
    /// TR: Bir tipte doğrudan bildirilen public static alanlar.
    /// </summary>
    /// <param name="type">EN: The type. TR: Tip.</param>
    /// <returns>EN: The fields. TR: Alanlar.</returns>
    private static IEnumerable<FieldInfo> Fields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

    /// <summary>
    /// EN: The values of the string constants among <paramref name="fields"/>.
    /// TR: <paramref name="fields"/> arasındaki string sabitlerinin değerleri.
    /// </summary>
    /// <param name="fields">EN: Fields. TR: Alanlar.</param>
    /// <returns>EN: The values. TR: Değerler.</returns>
    private static HashSet<string> ConstantsOf(IEnumerable<FieldInfo> fields) =>
        new(
            fields
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!),
            StringComparer.Ordinal);
}
