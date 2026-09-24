namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: The permission catalog (ADR-022), named <c>module.action</c>. Endpoints declare them with
///     <c>RequireAuthorization(Permissions.Customers.Write)</c>; tokens carry the user's effective set as <c>perm</c> claims.
///     A new module adds its own nested class here and to <see cref="All"/>.
/// TR: İzin kataloğu (ADR-022), <c>modül.işlem</c> biçiminde. Uç noktalar bunları <c>RequireAuthorization(Permissions.Customers.Write)</c>
///     ile bildirir; token'lar kullanıcının etkin kümesini <c>perm</c> claim'leri olarak taşır.
///     Yeni bir modül kendi iç sınıfını buraya ve <see cref="All"/>'a ekler.
/// </summary>
public static class Permissions
{
    /// <summary>EN: Customers module. TR: Customers modülü.</summary>
    public static class Customers
    {
        /// <summary>EN: View customers. TR: Müşterileri görme.</summary>
        public const string Read = "customers.read";

        /// <summary>EN: Create and edit customers. TR: Müşteri ekleme ve düzenleme.</summary>
        public const string Write = "customers.write";

        /// <summary>EN: Delete customers. TR: Müşteri silme.</summary>
        public const string Delete = "customers.delete";
    }

    /// <summary>EN: Inventory module. TR: Inventory modülü.</summary>
    public static class Inventory
    {
        /// <summary>EN: View stock. TR: Stok görme.</summary>
        public const string Read = "inventory.read";

        /// <summary>EN: Create and edit stock items. TR: Stok kalemi ekleme ve düzenleme.</summary>
        public const string Write = "inventory.write";

        /// <summary>EN: Delete stock items. TR: Stok kalemi silme.</summary>
        public const string Delete = "inventory.delete";
    }

    /// <summary>EN: Manage the company's users and their roles. TR: Firmanın kullanıcılarını ve rollerini yönetme.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>EN: Change the company's settings. TR: Firmanın ayarlarını değiştirme.</summary>
    public const string SettingsManage = "settings.manage";

    /// <summary>EN: Change the company's plan. TR: Firmanın planını değiştirme.</summary>
    public const string PlanManage = "plan.manage";

    /// <summary>
    /// EN: Every known permission. A policy name outside this set is treated as a typo and never authorizes anyone.
    /// TR: Bilinen tüm izinler. Bu kümenin dışındaki bir politika adı yazım hatası sayılır ve kimseye asla yetki vermez.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Customers.Read, Customers.Write, Customers.Delete,
        Inventory.Read, Inventory.Write, Inventory.Delete,
        UsersManage, SettingsManage, PlanManage,
    };
}
