using MyWorkplace.Abstractions.Identity;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: A made-up product's permission catalog, so the core's tests don't depend on this product's catalog (ADR-026).
/// TR: Uydurma bir ürünün izin kataloğu; böylece çekirdeğin testleri bu ürünün kataloğuna bağımlı olmaz (ADR-026).
/// </summary>
public static class TestPermissions
{
    /// <summary>EN: A module. TR: Bir modül.</summary>
    public static class Widgets
    {
        /// <summary>EN: Read. TR: Okuma.</summary>
        public const string Read = "widgets.read";

        /// <summary>EN: Write. TR: Yazma.</summary>
        public const string Write = "widgets.write";

        /// <summary>EN: Delete. TR: Silme.</summary>
        public const string Delete = "widgets.delete";

        /// <summary>EN: A non-standard action. TR: Standart olmayan bir işlem.</summary>
        public const string Export = "widgets.export";
    }

    /// <summary>EN: The core's settings permission. TR: Çekirdeğin ayar izni.</summary>
    public const string SettingsManage = CorePermissions.SettingsManage;

    /// <summary>EN: An administrative permission Admins also get. TR: Yöneticilerin de aldığı bir yönetim izni.</summary>
    public const string MembersManage = "members.manage";

    /// <summary>EN: An owner-only permission. TR: Sadece Sahibe ait bir izin.</summary>
    [OwnerOnly]
    public const string BillingManage = "billing.manage";

    /// <summary>EN: The catalog. TR: Katalog.</summary>
    public static readonly PermissionCatalog Catalog = PermissionCatalog.FromType(typeof(TestPermissions));
}
