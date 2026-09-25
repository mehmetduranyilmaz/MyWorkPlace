using MyWorkplace.Contracts.Identity;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: The role matrix is derived from permission names (ADR-025). These tests pin the agreed matrix of ADR-022 for
///     the modules that existed when it was written, and the rule for everything else — without listing the whole
///     catalog, so adding a module never has to edit them (found in T-013).
/// TR: Rol tablosu izin adlarından türetilir (ADR-025). Bu testler ADR-022'nin kararlaştırılan tablosunu yazıldığında var olan modüller
///     için ve geri kalan her şey için kuralı sabitler — tüm kataloğu listelemeden; böylece bir modül eklemek onları asla düzenlemek
///     zorunda kalmaz (T-013'te bulundu).
/// </summary>
public sealed class RoleMatrixTests
{
    /// <summary>EN: Module permissions ADR-022 was agreed on. TR: ADR-022'nin üzerinde anlaşıldığı modül izinleri.</summary>
    private static readonly string[] _agreed =
    [
        "customers.delete", "customers.read", "customers.write",
        "inventory.delete", "inventory.read", "inventory.write",
        "orders.delete", "orders.read", "orders.write",
    ];

    [Fact]
    public void Catalog_IsCollectedFromTheDeclarations()
    {
        Assert.Subset(Permissions.Modules.ToHashSet(), _agreed.ToHashSet());
        Assert.Equal(
            ["plan.manage", "settings.manage", "users.manage"],
            Permissions.Administrative.Order(StringComparer.Ordinal));
        Assert.Equal(Permissions.Modules.Count + Permissions.Administrative.Count, Permissions.All.Count);
    }

    [Fact]
    public void Matrix_IsTheOneAgreedInAdr022()
    {
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), Of(Roles.Owner));
        Assert.Equal(Permissions.All.Where(p => p != "plan.manage").Order(StringComparer.Ordinal), Of(Roles.Admin));

        var member = Of(Roles.Member);
        Assert.Subset(member.ToHashSet(), _agreed.Where(p => !p.EndsWith(".delete", StringComparison.Ordinal)).ToHashSet());
        Assert.DoesNotContain(member, p => p.EndsWith(".delete", StringComparison.Ordinal) || Permissions.Administrative.Contains(p));

        var viewer = Of(Roles.Viewer);
        Assert.Subset(viewer.ToHashSet(), _agreed.Where(p => p.EndsWith(".read", StringComparison.Ordinal)).ToHashSet());
        Assert.All(viewer, p => Assert.EndsWith(".read", p, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("products.read", "Owner,Admin,Member,Viewer")]
    [InlineData("products.write", "Owner,Admin,Member")]
    [InlineData("products.delete", "Owner,Admin")]
    [InlineData("products.export", "Owner,Admin")]
    public void NewModulePermission_FollowsItsSuffix(string permission, string expectedRoles)
    {
        // EN: A permission no module declares yet — what a new module would get without touching Roles.
        // TR: Henüz hiçbir modülün bildirmediği bir izin — yeni bir modülün Roles'a dokunmadan alacağı şey.
        var granted = new[] { Roles.Owner, Roles.Admin, Roles.Member, Roles.Viewer }
            .Where(role => Roles.Grants(role, permission));

        Assert.Equal(expectedRoles.Split(','), granted);
    }

    [Fact]
    public void EveryPermission_IsGrantedToSomeone()
    {
        Assert.All(Permissions.All, permission => Assert.Contains(Roles.All, role => Roles.Grants(role, permission)));
    }

    /// <summary>
    /// EN: A role's sorted permissions.
    /// TR: Bir rolün sıralı izinleri.
    /// </summary>
    /// <param name="role">EN: Role name. TR: Rol adı.</param>
    /// <returns>EN: Permissions. TR: İzinler.</returns>
    private static IReadOnlyList<string> Of(string role) => Roles.EffectivePermissions([role], []);
}
