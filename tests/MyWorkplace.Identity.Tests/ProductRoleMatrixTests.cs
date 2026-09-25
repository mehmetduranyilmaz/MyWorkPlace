using MyWorkplace.Abstractions.Identity;
using MyWorkplace.Contracts.Identity;

namespace MyWorkplace.Identity.Tests;

/// <summary>
/// EN: This product's role matrix (ADR-022) as the convention derives it from its catalog (ADR-025, ADR-026). Pins the
///     matrix agreed for the modules that existed then, and the rule for everything else — without listing the whole
///     catalog, so adding a module never has to edit it (T-013).
/// TR: Bu ürünün rol tablosu (ADR-022); kuralın onu ürün kataloğundan türettiği haliyle (ADR-025, ADR-026). O zaman var olan modüller için
///     kararlaştırılan tabloyu ve geri kalan her şey için kuralı sabitler — tüm kataloğu listelemeden; böylece bir modül eklemek onu asla
///     düzenlemek zorunda kalmaz (T-013).
/// </summary>
public sealed class ProductRoleMatrixTests
{
    /// <summary>EN: Module permissions ADR-022 was agreed on. TR: ADR-022'nin üzerinde anlaşıldığı modül izinleri.</summary>
    private static readonly string[] _agreed =
    [
        "customers.delete", "customers.read", "customers.write",
        "inventory.delete", "inventory.read", "inventory.write",
        "orders.delete", "orders.read", "orders.write",
    ];

    /// <summary>EN: The product's catalog. TR: Ürünün kataloğu.</summary>
    private static PermissionCatalog Catalog => Permissions.Catalog;

    [Fact]
    public void Catalog_HoldsTheAgreedAndAdministrativePermissions()
    {
        Assert.Subset(Catalog.Modules.ToHashSet(), _agreed.ToHashSet());
        Assert.Equal(["plan.manage", "settings.manage", "users.manage"], Catalog.Administrative.Order(StringComparer.Ordinal));
        Assert.Equal(["plan.manage"], Catalog.OwnerOnly);
    }

    [Fact]
    public void Matrix_IsTheOneAgreedInAdr022()
    {
        Assert.Equal(Catalog.All.Order(StringComparer.Ordinal), Of(DefaultRoles.Owner));
        Assert.Equal(Catalog.All.Where(p => p != "plan.manage").Order(StringComparer.Ordinal), Of(DefaultRoles.Admin));

        var member = Of(DefaultRoles.Member);
        Assert.Subset(member.ToHashSet(), _agreed.Where(p => !p.EndsWith(".delete", StringComparison.Ordinal)).ToHashSet());
        Assert.DoesNotContain(member, p => p.EndsWith(".delete", StringComparison.Ordinal) || Catalog.Administrative.Contains(p));

        var viewer = Of(DefaultRoles.Viewer);
        Assert.Subset(viewer.ToHashSet(), _agreed.Where(p => p.EndsWith(".read", StringComparison.Ordinal)).ToHashSet());
        Assert.All(viewer, p => Assert.EndsWith(".read", p, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryPermission_IsGrantedToSomeone()
    {
        Assert.All(Catalog.All, permission => Assert.Contains(DefaultRoles.All, role => Catalog.Grants(role, permission)));
    }

    /// <summary>
    /// EN: A role's sorted permissions.
    /// TR: Bir rolün sıralı izinleri.
    /// </summary>
    /// <param name="role">EN: Role name. TR: Rol adı.</param>
    /// <returns>EN: Permissions. TR: İzinler.</returns>
    private static IReadOnlyList<string> Of(string role) => Catalog.EffectivePermissions([role], []);
}
