using System.Reflection;

namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: The permission catalog (ADR-022, ADR-025), named <c>module.action</c>. Endpoints declare them with
///     <c>RequireAuthorization(Permissions.Customers.Write)</c>; tokens carry the user's effective set as <c>perm</c> claims.
///     A new module only <b>adds</b> its own nested class (<c>Read</c> / <c>Write</c> / <c>Delete</c>); the catalog and the
///     role matrix pick it up by themselves — no other line changes.
/// TR: İzin kataloğu (ADR-022, ADR-025), <c>modül.işlem</c> biçiminde. Uç noktalar bunları <c>RequireAuthorization(Permissions.Customers.Write)</c>
///     ile bildirir; token'lar kullanıcının etkin kümesini <c>perm</c> claim'leri olarak taşır.
///     Yeni bir modül sadece kendi iç sınıfını (<c>Read</c> / <c>Write</c> / <c>Delete</c>) <b>ekler</b>; katalog ve rol tablosu onu kendiliğinden
///     alır — başka hiçbir satır değişmez.
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

    /// <summary>EN: Orders module. TR: Orders modülü.</summary>
    public static class Orders
    {
        /// <summary>EN: View orders. TR: Siparişleri görme.</summary>
        public const string Read = "orders.read";

        /// <summary>EN: Create, edit and place orders. TR: Sipariş oluşturma, düzenleme ve verme.</summary>
        public const string Write = "orders.write";

        /// <summary>EN: Delete draft orders. TR: Taslak siparişleri silme.</summary>
        public const string Delete = "orders.delete";
    }

    /// <summary>EN: Manage the company's users and their roles. TR: Firmanın kullanıcılarını ve rollerini yönetme.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>EN: Change the company's settings. TR: Firmanın ayarlarını değiştirme.</summary>
    public const string SettingsManage = "settings.manage";

    /// <summary>EN: Change the company's plan. TR: Firmanın planını değiştirme.</summary>
    public const string PlanManage = "plan.manage";

    /// <summary>
    /// EN: Permissions declared by modules: every constant of every nested class above, found by reflection.
    /// TR: Modüllerin bildirdiği izinler: yukarıdaki her iç sınıfın her sabiti; reflection ile bulunur.
    /// </summary>
    public static readonly IReadOnlySet<string> Modules = ConstantsOf(typeof(Permissions).GetNestedTypes(BindingFlags.Public));

    /// <summary>
    /// EN: Administrative permissions: the constants declared directly on this class. Roles grant them explicitly.
    /// TR: Yönetim izinleri: doğrudan bu sınıfta bildirilen sabitler. Roller bunları açıkça verir.
    /// </summary>
    public static readonly IReadOnlySet<string> Administrative = ConstantsOf([typeof(Permissions)]);

    /// <summary>
    /// EN: Every known permission. A policy name outside this set is treated as a typo and never authorizes anyone.
    /// TR: Bilinen tüm izinler. Bu kümenin dışındaki bir politika adı yazım hatası sayılır ve kimseye asla yetki vermez.
    /// </summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(Modules.Concat(Administrative), StringComparer.Ordinal);

    /// <summary>
    /// EN: The string constants declared on <paramref name="types"/>.
    /// TR: <paramref name="types"/> üzerinde bildirilen string sabitleri.
    /// </summary>
    /// <param name="types">EN: Declaring types. TR: Bildiren tipler.</param>
    /// <returns>EN: The constants' values. TR: Sabitlerin değerleri.</returns>
    private static HashSet<string> ConstantsOf(IEnumerable<Type> types) =>
        new(
            types
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!),
            StringComparer.Ordinal);
}
