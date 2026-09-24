using System.Net.Http.Json;
using static MyWorkplace.IntegrationTests.CustomersApi;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.UsersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The default roles of ADR-022 against real endpoints: each role is refused exactly what its row in the matrix
///     does not contain (T-037). The first user is always Owner, so these need a second user.
/// TR: ADR-022'nin varsayılan rolleri gerçek uç noktalara karşı: her rol, tablodaki satırında olmayan şey için reddedilir
///     (T-037). İlk kullanıcı hep Sahip olduğundan bunlar ikinci bir kullanıcı gerektirir.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class RoleRestrictionTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Member_CanWrite_ButCannotDeleteOrManageUsers()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (customerId, _) = await CreateNewAsync(owner, "Acme", email: null, Ct);
        using var member = await SignInAsNewUserAsync(app, owner, "Member", Ct);

        using var create = await CustomersApi.CreateAsync(member, new { name = "Globex" }, Ct);
        using var delete = await member.DeleteAsync($"/customers/{customerId}", Ct);
        using var users = await member.GetAsync("/identity/users", Ct);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanRead_ButCannotCreate()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);

        using var list = await viewer.GetAsync("/customers", Ct);
        using var create = await CustomersApi.CreateAsync(viewer, new { name = "Globex" }, Ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Admin_CanDelete_ButCannotChangeThePlan()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (customerId, _) = await CreateNewAsync(owner, "Acme", email: null, Ct);
        using var admin = await SignInAsNewUserAsync(app, owner, "Admin", Ct);

        using var delete = await admin.DeleteAsync($"/customers/{customerId}", Ct);
        using var upgrade = await UpgradeAsync(admin, Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, upgrade.StatusCode);
    }

    [Fact]
    public async Task ProModule_RefusesAViewerWrite_EvenOnPro()
    {
        // EN: Plan and permission are separate checks: Pro opens the module, the role still limits what you do in it.
        // TR: Plan ve izin ayrı kontrollerdir: Pro modülü açar, rol yine de içinde ne yapabileceğinizi sınırlar.
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        using var upgrade = await UpgradeAsync(owner, Ct);
        upgrade.EnsureSuccessStatusCode();
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);

        using var list = await viewer.GetAsync("/inventory/items", Ct);
        using var create = await viewer.PostAsJsonAsync(
            "/inventory/items", new { sku = "SKU-1", name = "Bolt", baseUnit = "PCS" }, Ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }
}
