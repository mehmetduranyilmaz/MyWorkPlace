using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.UsersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: User management through the gateway (T-037, ADR-022): adding users, changing access, removing users, and the
///     rules that protect a company from losing its Owner or from privilege escalation.
/// TR: Gateway üzerinden kullanıcı yönetimi (T-037, ADR-022): kullanıcı ekleme, yetki değiştirme, kaldırma ve firmayı
///     Sahip'siz kalmaktan veya yetki yükseltmeden koruyan kurallar.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class UserManagementTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- Adding users

    [Fact]
    public async Task AddUser_WithoutRoles_IsMember_AndCanSignIn()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var email = UniqueEmail();

        using var response = await CreateAsync(owner, new { email, password = ValidPassword }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal($"/identity/users/{body.GetProperty("id").GetGuid()}", response.Headers.Location!.ToString());
        Assert.Equal(["Member"], body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.False(body.TryGetProperty("passwordHash", out _));

        using var member = await SignInAsync(app, email, Ct);
        Assert.Equal(
            ["customers.read", "customers.write", "inventory.read", "inventory.write"],
            PermissionsOf(member));
    }

    [Fact]
    public async Task AddUser_EmailAlreadyRegistered_Returns409()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (_, email) = await AddUserAsync(owner, "Member", Ct);

        using var response = await CreateAsync(owner, new { email = email.ToUpperInvariant(), password = ValidPassword }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddUser_UnknownRoleOrPermission_Returns400()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);

        using var unknownRole = await CreateAsync(
            owner, new { email = UniqueEmail(), password = ValidPassword, roles = new[] { "Boss" } }, Ct);
        using var unknownPermission = await CreateAsync(
            owner, new { email = UniqueEmail(), password = ValidPassword, extraPermissions = new[] { "customers.fly" } }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownPermission.StatusCode);
    }

    // ---------------------------------------------------------------- Reading users

    [Fact]
    public async Task ListUsers_ShowsOnlyTheCompanysUsers_AndSearchesByEmail()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (_, memberEmail) = await AddUserAsync(owner, "Member", Ct);
        var (otherOwner, _) = await CreateCompanyAsync(app, Ct);
        await AddUserAsync(otherOwner, "Member", Ct);

        var all = await owner.GetFromJsonAsync<JsonElement>("/identity/users", Ct);
        var found = await owner.GetFromJsonAsync<JsonElement>($"/identity/users?search={memberEmail[..12]}", Ct);

        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(memberEmail, item.GetProperty("email").GetString());
    }

    [Fact]
    public async Task GetUser_OfAnotherCompany_Returns404()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (otherOwner, _) = await CreateCompanyAsync(app, Ct);
        var (otherId, _) = await AddUserAsync(otherOwner, "Member", Ct);

        using var response = await owner.GetAsync($"/identity/users/{otherId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- Changing access

    [Fact]
    public async Task UpdateAccess_NewRolesAndExtras_AreInTheNextToken()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (id, email) = await AddUserAsync(owner, "Member", Ct);

        using var response = await UpdateAccessAsync(
            owner, id, ["Viewer"], ["customers.delete"], await GetETagAsync(owner, id, Ct), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var user = await SignInAsync(app, email, Ct);
        Assert.Equal(["customers.delete", "customers.read", "inventory.read"], PermissionsOf(user));
    }

    [Fact]
    public async Task UpdateAccess_WithoutOrWithStaleIfMatch_IsRejected()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (id, _) = await AddUserAsync(owner, "Member", Ct);
        var etag = await GetETagAsync(owner, id, Ct);
        using var first = await UpdateAccessAsync(owner, id, ["Viewer"], [], etag, Ct);

        using var missing = await UpdateAccessAsync(owner, id, ["Admin"], [], ifMatch: null, Ct);
        using var stale = await UpdateAccessAsync(owner, id, ["Admin"], [], etag, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    // ---------------------------------------------------------------- The last Owner

    [Fact]
    public async Task LastOwner_CannotBeDemotedOrRemoved_UntilAnotherOwnerExists()
    {
        var (owner, ownerId) = await CreateCompanyAsync(app, Ct);

        using var demote = await UpdateAccessAsync(owner, ownerId, ["Admin"], [], await GetETagAsync(owner, ownerId, Ct), Ct);
        using var remove = await owner.DeleteAsync($"/identity/users/{ownerId}", Ct);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);

        await AddUserAsync(owner, "Owner", Ct);
        using var demoteNow = await UpdateAccessAsync(
            owner, ownerId, ["Admin"], [], await GetETagAsync(owner, ownerId, Ct), Ct);
        Assert.Equal(HttpStatusCode.OK, demoteNow.StatusCode);
    }

    [Fact]
    public async Task TwoOwners_DemotingEachOtherAtOnce_LeaveExactlyOneOwner()
    {
        var (ownerA, idA) = await CreateCompanyAsync(app, Ct);
        var (idB, emailB) = await AddUserAsync(ownerA, "Owner", Ct);
        using var ownerB = await SignInAsync(app, emailB, Ct);
        var etagA = await GetETagAsync(ownerA, idA, Ct);
        var etagB = await GetETagAsync(ownerA, idB, Ct);

        // EN: Without the company lock both would see "another Owner exists" and both would succeed.
        // TR: Firma kilidi olmasa ikisi de "başka Sahip var" görür ve ikisi de başarılı olurdu.
        var responses = await Task.WhenAll(
            UpdateAccessAsync(ownerA, idB, ["Admin"], [], etagB, Ct),
            UpdateAccessAsync(ownerB, idA, ["Admin"], [], etagA, Ct));

        Assert.Single(responses, r => r.IsSuccessStatusCode);
        var users = await ownerA.GetFromJsonAsync<JsonElement>("/identity/users", Ct);
        Assert.Single(
            users.GetProperty("items").EnumerateArray(),
            u => u.GetProperty("roles").EnumerateArray().Any(r => r.GetString() == "Owner"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ---------------------------------------------------------------- Anti-escalation

    [Fact]
    public async Task Admin_CannotEscalate_ButCanManageMembers()
    {
        var (owner, ownerId) = await CreateCompanyAsync(app, Ct);
        using var admin = await SignInAsNewUserAsync(app, owner, "Admin", Ct);

        using var grantOwner = await CreateAsync(
            admin, new { email = UniqueEmail(), password = ValidPassword, roles = new[] { "Owner" } }, Ct);
        using var grantPlan = await CreateAsync(
            admin, new { email = UniqueEmail(), password = ValidPassword, extraPermissions = new[] { "plan.manage" } }, Ct);
        using var demoteOwner = await UpdateAccessAsync(
            admin, ownerId, ["Viewer"], [], await GetETagAsync(admin, ownerId, Ct), Ct);
        using var removeOwner = await admin.DeleteAsync($"/identity/users/{ownerId}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, grantOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grantPlan.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, demoteOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removeOwner.StatusCode);

        var (memberId, _) = await AddUserAsync(admin, "Member", Ct);
        using var removeMember = await admin.DeleteAsync($"/identity/users/{memberId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removeMember.StatusCode);
    }

    // ---------------------------------------------------------------- Removing users

    [Fact]
    public async Task RemovedUser_CannotSignIn_AndTheEmailCanBeUsedAgain()
    {
        var (owner, _) = await CreateCompanyAsync(app, Ct);
        var (id, email) = await AddUserAsync(owner, "Member", Ct);

        using var remove = await owner.DeleteAsync($"/identity/users/{id}", Ct);
        using var anonymous = app.CreateGatewayClient();
        using var login = await LoginAsync(anonymous, email, ValidPassword, Ct);
        using var get = await owner.GetAsync($"/identity/users/{id}", Ct);
        using var again = await CreateAsync(owner, new { email, password = ValidPassword }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    /// <summary>
    /// EN: The sorted <c>perm</c> claims of the token a client sends.
    /// TR: Bir istemcinin gönderdiği token'daki sıralı <c>perm</c> claim'leri.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <returns>EN: Permission names. TR: İzin adları.</returns>
    private static IEnumerable<string> PermissionsOf(HttpClient client) =>
        new JsonWebTokenHandler()
            .ReadJsonWebToken(client.DefaultRequestHeaders.Authorization!.Parameter)
            .Claims.Where(c => c.Type == "perm")
            .Select(c => c.Value)
            .Order(StringComparer.Ordinal);
}
