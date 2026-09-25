using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.UsersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The unit catalog (T-031, ADR-019) through the gateway: system units every company has, own units a company adds,
///     and the rules that keep stored quantities right — codes and precision freeze once items use a unit.
/// TR: Gateway üzerinden birim kataloğu (T-031, ADR-019): her firmada olan sistem birimleri, firmanın eklediği kendi birimleri ve
///     kayıtlı miktarları doğru tutan kurallar — kalemler bir birimi kullanınca kod ve hassasiyet donar.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class UnitCatalogTests(AppFixture app)
{
    /// <summary>EN: Units address. TR: Birimler adresi.</summary>
    private const string Units = "/inventory/units";

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_ShowsSystemUnitsFirst_ThenOwnUnitsByCode()
    {
        using var client = await CreateProClientAsync(app, Ct);
        await CreateAsync(client, "ZED", 0);
        await CreateAsync(client, "dozen", 0);

        var units = await client.GetFromJsonAsync<JsonElement>(Units, Ct);

        Assert.Equal(
            ["PCS:system", "KG:system", "L:system", "M:system", "BOX:system", "PACK:system", "DOZEN:own", "ZED:own"],
            units.EnumerateArray().Select(u =>
                $"{u.GetProperty("code").GetString()}:{(u.GetProperty("isSystem").GetBoolean() ? "system" : "own")}"));
    }

    [Fact]
    public async Task Create_StoresTheCodeUpperCase_Returns201WithLocationAndETag()
    {
        using var client = await CreateProClientAsync(app, Ct);

        using var response = await client.PostAsJsonAsync(Units, Body("roll", "Roll", 2), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{Units}/ROLL", response.Headers.Location?.OriginalString);
        Assert.NotNull(response.Headers.ETag);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(("ROLL", 2, false), (body.GetProperty("code").GetString(), body.GetProperty("precision").GetInt32(), body.GetProperty("isSystem").GetBoolean()));
    }

    [Theory]
    [InlineData("A B", "Name", 0, "code")]
    [InlineData("ELEVENCHARS", "Name", 0, "code")]
    [InlineData("CODE", null, 0, "name")]
    [InlineData("CODE", "Name", 4, "precision")]
    [InlineData("CODE", "Name", -1, "precision")]
    public async Task Create_InvalidField_Returns400WithFieldError(string code, string? name, int precision, string invalidField)
    {
        using var client = await CreateProClientAsync(app, Ct);

        using var response = await client.PostAsJsonAsync(Units, Body(code, name, precision), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => string.Equals(e.Name, invalidField, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_UsedCode_Returns409_SystemUnitsIncluded_ButAnotherCompanyMayUseIt()
    {
        using var client = await CreateProClientAsync(app, Ct);
        using var otherCompany = await CreateProClientAsync(app, Ct);
        await CreateAsync(client, "DOZEN", 0);

        using var duplicate = await client.PostAsJsonAsync(Units, Body("Dozen", "Again", 0), Ct);
        using var system = await client.PostAsJsonAsync(Units, Body("kg", "My kilogram", 3), Ct);
        using var elsewhere = await otherCompany.PostAsJsonAsync(Units, Body("DOZEN", "Dozen", 0), Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, system.StatusCode);
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task SystemUnit_CanBeRead_ButNotChangedOrDeleted()
    {
        using var client = await CreateProClientAsync(app, Ct);

        using var get = await client.GetAsync($"{Units}/kg", Ct);
        using var update = await client.PutWithIfMatchAsync($"{Units}/KG", Body("KG", "Kilo", 2), "\"1\"", Ct);
        using var delete = await client.DeleteAsync($"{Units}/KG", Ct);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Null(get.Headers.ETag);
        Assert.True((await get.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("isSystem").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task UnusedUnit_FollowsTheReferenceModule_AndItsCodeCanBeUsedAgain()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var etag = await CreateAsync(client, "CASE", 0);

        using var noPrecondition = await client.PutWithIfMatchAsync($"{Units}/CASE", Body("CRATE", "Crate", 1), null, Ct);
        using var update = await client.PutWithIfMatchAsync($"{Units}/case", Body("crate", "Crate", 1), etag, Ct);
        using var stale = await client.PutWithIfMatchAsync($"{Units}/CRATE", Body("CRATE", "Stale", 1), etag, Ct);
        using var oldCode = await client.GetAsync($"{Units}/CASE", Ct);
        using var delete = await client.DeleteAsync($"{Units}/CRATE", Ct);
        using var afterDelete = await client.GetAsync($"{Units}/CRATE", Ct);
        using var again = await client.PostAsJsonAsync(Units, Body("CRATE", "Crate again", 0), Ct);

        Assert.Equal((HttpStatusCode)428, noPrecondition.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("CRATE", (await update.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldCode.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task UnitUsedAsAlternativeUnit_FreezesCodeAndPrecision_ButNotTheName()
    {
        using var client = await CreateProClientAsync(app, Ct);
        var etag = await CreateAsync(client, "DOZEN", 0);
        await CreateItemAsync(client, new { sku = "EGGS-1", name = "Eggs", baseUnit = "PCS", units = new[] { new { unit = "dozen", factor = 12m } } });

        using var precision = await client.PutWithIfMatchAsync($"{Units}/DOZEN", Body("DOZEN", "Dozen", 1), etag, Ct);
        using var code = await client.PutWithIfMatchAsync($"{Units}/DOZEN", Body("DZ", "Dozen", 0), etag, Ct);
        using var delete = await client.DeleteAsync($"{Units}/DOZEN", Ct);
        using var rename = await client.PutWithIfMatchAsync($"{Units}/DOZEN", Body("DOZEN", "Twelve", 0), etag, Ct);

        Assert.Equal(HttpStatusCode.Conflict, precision.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, code.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task UnitUsedAsBaseUnit_CantBeDeleted()
    {
        using var client = await CreateProClientAsync(app, Ct);
        await CreateAsync(client, "ROLL", 0);
        await CreateItemAsync(client, new { sku = "TAPE-1", name = "Tape", baseUnit = "roll" });

        using var delete = await client.DeleteAsync($"{Units}/ROLL", Ct);

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task AnotherCompanysUnit_IsNotFound()
    {
        using var owner = await CreateProClientAsync(app, Ct);
        using var stranger = await CreateProClientAsync(app, Ct);
        var etag = await CreateAsync(owner, "SECRET", 0);

        using var get = await stranger.GetAsync($"{Units}/SECRET", Ct);
        using var update = await stranger.PutWithIfMatchAsync($"{Units}/SECRET", Body("SECRET", "Hijacked", 0), etag, Ct);
        using var delete = await stranger.DeleteAsync($"{Units}/SECRET", Ct);
        using var useIt = await stranger.PostAsJsonAsync("/inventory/items", new { sku = "S-1", name = "S", baseUnit = "SECRET" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, useIt.StatusCode);
    }

    [Fact]
    public async Task Roles_ViewerReads_MemberAdds_OnlyAdminDeletes()
    {
        var (ownerClient, _) = await CreateCompanyAsync(app, Ct);
        using var owner = ownerClient;
        using var upgrade = await UpgradeAsync(owner, Ct);
        upgrade.EnsureSuccessStatusCode();
        using var viewer = await SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await SignInAsNewUserAsync(app, owner, "Member", Ct);
        using var admin = await SignInAsNewUserAsync(app, owner, "Admin", Ct);

        using var viewerList = await viewer.GetAsync(Units, Ct);
        using var viewerCreate = await viewer.PostAsJsonAsync(Units, Body("V", "V", 0), Ct);
        using var memberCreate = await member.PostAsJsonAsync(Units, Body("M1", "M1", 0), Ct);
        using var memberDelete = await member.DeleteAsync($"{Units}/M1", Ct);
        using var adminDelete = await admin.DeleteAsync($"{Units}/M1", Ct);

        Assert.Equal(HttpStatusCode.OK, viewerList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, memberCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, adminDelete.StatusCode);
    }

    [Fact]
    public async Task BasicCompany_Gets403_ThroughGatewayAndDirectly()
    {
        var basicToken = await GetAccessTokenAsync(app, Ct);
        using var viaGateway = app.CreateGatewayClient();
        using var direct = app.CreateDirectServiceClient("inventory");
        Authorize(viaGateway, basicToken);
        Authorize(direct, basicToken);

        using var gatewayResponse = await viaGateway.GetAsync(Units, Ct);
        using var directResponse = await direct.GetAsync(Units, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, gatewayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, directResponse.StatusCode);
    }

    /// <summary>
    /// EN: Adds a unit and returns its ETag; fails the test if that fails.
    /// TR: Bir birim ekler ve ETag'ini döner; başarısızsa testi düşürür.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="code">EN: Code. TR: Kod.</param>
    /// <param name="precision">EN: Precision. TR: Hassasiyet.</param>
    /// <returns>EN: The ETag. TR: ETag.</returns>
    private static async Task<string> CreateAsync(HttpClient client, string code, int precision)
    {
        using var response = await client.PostAsJsonAsync(Units, Body(code, code, precision), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return response.Headers.ETag!.ToString();
    }

    /// <summary>
    /// EN: Creates a stock item from the given body; fails the test if that fails.
    /// TR: Verilen gövdeden bir stok kalemi oluşturur; başarısızsa testi düşürür.
    /// </summary>
    /// <param name="client">EN: Pro client. TR: Pro istemci.</param>
    /// <param name="body">EN: Item body. TR: Kalem gövdesi.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static async Task CreateItemAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/inventory/items", body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// EN: A unit request body.
    /// TR: Bir birim istek gövdesi.
    /// </summary>
    /// <param name="code">EN: Code. TR: Kod.</param>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="precision">EN: Precision. TR: Hassasiyet.</param>
    /// <returns>EN: The body. TR: Gövde.</returns>
    private static object Body(string code, string? name, int precision) => new { code, name, precision };
}
