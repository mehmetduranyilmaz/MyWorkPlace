using MyWorkplace.Contracts.Identity;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: The role matrix is derived from permission names (ADR-025). These tests pin the exact result for today's
///     catalog — the same matrix ADR-022 wrote by hand — so the convention can't silently change who may do what.
/// TR: Rol tablosu izin adlarından türetilir (ADR-025). Bu testler bugünkü katalog için sonucu birebir sabitler — ADR-022'nin elle yazdığı
///     tablonun aynısı — böylece kural kimin neyi yapabileceğini sessizce değiştiremez.
/// </summary>
public sealed class RoleMatrixTests
{
    /// <summary>EN: Today's module permissions. TR: Bugünkü modül izinleri.</summary>
    private static readonly string[] _modules =
    [
        "customers.delete", "customers.read", "customers.write",
        "inventory.delete", "inventory.read", "inventory.write",
        "orders.delete", "orders.read", "orders.write",
    ];

    [Fact]
    public void Catalog_IsCollectedFromTheDeclarations()
    {
        Assert.Equal(_modules, Permissions.Modules.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["plan.manage", "settings.manage", "users.manage"],
            Permissions.Administrative.Order(StringComparer.Ordinal));
        Assert.Equal(12, Permissions.All.Count);
    }

    [Fact]
    public void Matrix_IsTheOneAgreedInAdr022()
    {
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), Of(Roles.Owner));
        Assert.Equal(Permissions.All.Where(p => p != "plan.manage").Order(StringComparer.Ordinal), Of(Roles.Admin));
        Assert.Equal(
            ["customers.read", "customers.write", "inventory.read", "inventory.write", "orders.read", "orders.write"],
            Of(Roles.Member));
        Assert.Equal(["customers.read", "inventory.read", "orders.read"], Of(Roles.Viewer));
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
