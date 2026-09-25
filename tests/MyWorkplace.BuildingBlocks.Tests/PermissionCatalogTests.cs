using MyWorkplace.Abstractions.Identity;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: The generic permission catalog and role convention (ADR-025, ADR-026), on a made-up product's catalog.
/// TR: Genel izin kataloğu ve rol kuralı (ADR-025, ADR-026), uydurma bir ürünün kataloğu üzerinde.
/// </summary>
public sealed class PermissionCatalogTests
{
    /// <summary>EN: The test catalog. TR: Test kataloğu.</summary>
    private static PermissionCatalog Catalog => TestPermissions.Catalog;

    [Fact]
    public void FromType_SortsModuleAdministrativeAndOwnerOnlyPermissions()
    {
        Assert.Equal(
            ["widgets.delete", "widgets.export", "widgets.read", "widgets.write"],
            Catalog.Modules.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["billing.manage", "members.manage", "settings.manage"],
            Catalog.Administrative.Order(StringComparer.Ordinal));
        Assert.Equal(["billing.manage"], Catalog.OwnerOnly);
        Assert.Equal(7, Catalog.All.Count);
    }

    [Theory]
    [InlineData("widgets.read", "Owner,Admin,Member,Viewer")]
    [InlineData("widgets.write", "Owner,Admin,Member")]
    [InlineData("widgets.delete", "Owner,Admin")]
    [InlineData("widgets.export", "Owner,Admin")]
    [InlineData("members.manage", "Owner,Admin")]
    [InlineData("billing.manage", "Owner")]
    public void Grants_FollowsTheConvention(string permission, string expectedRoles)
    {
        var granted = DefaultRoles.All.Where(role => Catalog.Grants(role, permission));

        Assert.Equal(expectedRoles.Split(','), granted);
    }

    [Fact]
    public void EffectivePermissions_AddExtrasAndIgnoreUnknownNames()
    {
        var permissions = Catalog.EffectivePermissions([DefaultRoles.Viewer, "NoSuchRole"], ["widgets.delete", "made.up"]);

        Assert.Equal(["widgets.delete", "widgets.read"], permissions);
    }

    [Fact]
    public void FromType_WithoutTheCorePermissions_Fails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => PermissionCatalog.FromType(typeof(IncompleteCatalog)));

        Assert.Contains(CorePermissions.SettingsManage, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// EN: A catalog that forgot the core's own permission.
    /// TR: Çekirdeğin kendi iznini unutan bir katalog.
    /// </summary>
    private static class IncompleteCatalog
    {
        /// <summary>EN: A module. TR: Bir modül.</summary>
        public static class Things
        {
            /// <summary>EN: Read. TR: Okuma.</summary>
            public const string Read = "things.read";
        }
    }
}
