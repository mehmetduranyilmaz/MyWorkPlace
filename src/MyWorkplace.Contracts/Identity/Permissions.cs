using MyWorkplace.Abstractions.Identity;

namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: This product's permission catalog (ADR-022, ADR-025, ADR-026), named <c>module.action</c>. Endpoints declare them
///     with <c>RequireAuthorization(Permissions.Customers.Write)</c>; tokens carry the user's effective set as <c>perm</c>
///     claims. A new module only <b>adds</b> its own nested class (<c>Read</c> / <c>Write</c> / <c>Delete</c>); the
///     <see cref="Catalog"/> and the role convention pick it up by themselves — no other line changes.
/// TR: Bu ürünün izin kataloğu (ADR-022, ADR-025, ADR-026), <c>modül.işlem</c> biçiminde. Uç noktalar bunları
///     <c>RequireAuthorization(Permissions.Customers.Write)</c> ile bildirir; token'lar kullanıcının etkin kümesini <c>perm</c> claim'leri
///     olarak taşır. Yeni bir modül sadece kendi iç sınıfını (<c>Read</c> / <c>Write</c> / <c>Delete</c>) <b>ekler</b>; <see cref="Catalog"/> ve
///     rol kuralı onu kendiliğinden alır — başka hiçbir satır değişmez.
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

    /// <summary>EN: Products module. TR: Products modülü.</summary>
    public static class Products
    {
        /// <summary>EN: View the product catalog. TR: Ürün kataloğunu görme.</summary>
        public const string Read = "products.read";

        /// <summary>EN: Create and edit products. TR: Ürün ekleme ve düzenleme.</summary>
        public const string Write = "products.write";

        /// <summary>EN: Delete products. TR: Ürün silme.</summary>
        public const string Delete = "products.delete";
    }

    /// <summary>EN: Manage the company's users and their roles. TR: Firmanın kullanıcılarını ve rollerini yönetme.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>EN: Change the company's settings — the core's own permission. TR: Firmanın ayarlarını değiştirme — çekirdeğin kendi izni.</summary>
    public const string SettingsManage = CorePermissions.SettingsManage;

    /// <summary>EN: Change the company's plan; only the Owner. TR: Firmanın planını değiştirme; sadece Sahip.</summary>
    [OwnerOnly]
    public const string PlanManage = "plan.manage";

    /// <summary>
    /// EN: This catalog, read once; services register it (<c>AddServiceModule</c>) and Identity computes tokens with it.
    /// TR: Bir kez okunan bu katalog; servisler onu kaydeder (<c>AddServiceModule</c>) ve Identity token'ları onunla hesaplar.
    /// </summary>
    public static readonly PermissionCatalog Catalog = PermissionCatalog.FromType(typeof(Permissions));
}
