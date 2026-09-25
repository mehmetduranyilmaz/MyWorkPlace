using MyWorkplace.Abstractions.Identity;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Domain;

namespace MyWorkplace.Identity.Tests;

/// <summary>
/// EN: The anti-escalation rule of ADR-022 as pure domain logic (T-037); the endpoints are covered by integration tests.
/// TR: ADR-022'nin yetki yükseltme kuralı, saf domain mantığı olarak (T-037); uç noktalar entegrasyon testleriyle kapsanır.
/// </summary>
public sealed class UserAccessRulesTests
{
    [Fact]
    public void Owner_MayGrantAnything()
    {
        var owner = UserWith(DefaultRoles.Owner);

        Assert.True(owner.MayAssignAccess(UserWith(DefaultRoles.Member), [DefaultRoles.Owner], [Permissions.PlanManage]));
    }

    [Fact]
    public void Admin_MayNotGrantOwnerRole_NorChangeAnOwner()
    {
        var admin = UserWith(DefaultRoles.Admin);

        Assert.False(admin.MayAssignAccess(UserWith(DefaultRoles.Member), [DefaultRoles.Owner], []));
        Assert.False(admin.MayAssignAccess(UserWith(DefaultRoles.Owner), [DefaultRoles.Viewer], []));
        Assert.False(admin.MayRemove(UserWith(DefaultRoles.Owner)));
    }

    [Fact]
    public void Admin_MayNotGrantAPermissionTheyLack()
    {
        var admin = UserWith(DefaultRoles.Admin);

        Assert.False(admin.MayAssignAccess(UserWith(DefaultRoles.Member), [DefaultRoles.Member], [Permissions.PlanManage]));
    }

    [Fact]
    public void Admin_MayGrantWhatTheyHave_AndTakeAway()
    {
        var admin = UserWith(DefaultRoles.Admin);

        Assert.True(admin.MayAssignAccess(UserWith(DefaultRoles.Member), [DefaultRoles.Admin], []));
        Assert.True(admin.MayAssignAccess(UserWith(DefaultRoles.Admin), [DefaultRoles.Viewer], []));
        Assert.True(admin.MayRemove(UserWith(DefaultRoles.Member)));
    }

    [Fact]
    public void Admin_MayKeepAnExistingGrantTheyLack()
    {
        // EN: An Owner once granted plan.manage; an Admin editing other parts must not be blocked by it.
        // TR: Bir Sahip bir zamanlar plan.manage vermiş; başka kısımları düzenleyen Admin bundan dolayı engellenmemeli.
        var admin = UserWith(DefaultRoles.Admin);
        var target = UserWith(DefaultRoles.Viewer);
        target.GrantExtraPermissions([Permissions.PlanManage]);

        Assert.True(admin.MayAssignAccess(target, [DefaultRoles.Member], [Permissions.PlanManage]));
    }

    [Fact]
    public void GrantExtraPermissions_UnknownName_Throws()
    {
        var user = UserWith(DefaultRoles.Member);

        Assert.Throws<ArgumentException>(() => user.GrantExtraPermissions(["customers.fly"]));
    }

    /// <summary>
    /// EN: A user with the given role.
    /// TR: Verilen role sahip bir kullanıcı.
    /// </summary>
    /// <param name="role">EN: Role name. TR: Rol adı.</param>
    /// <returns>EN: The user. TR: Kullanıcı.</returns>
    private static User UserWith(string role)
    {
        var user = new User { Email = "u@example.com", NormalizedEmail = "u@example.com" };
        user.AssignRoles([role]);
        return user;
    }
}
