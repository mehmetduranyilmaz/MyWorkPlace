using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static MyWorkplace.IntegrationTests.ProductsApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The Products module through the gateway (T-013) — the same behavior the reference module proves, for a module
///     built only by following the guide.
/// TR: Gateway üzerinden Products modülü (T-013) — referans modülün kanıtladığı davranışın aynısı; sadece rehber izlenerek yazılmış
///     bir modül için.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class ProductTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- Create

    [Fact]
    public async Task Create_ValidProduct_Returns201WithLocationAndETag()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);

        using var response = await CreateAsync(
            client, new { sku = " bolt-m8 ", name = "  Bolt M8  ", price = 1.25m, description = " Zinc " }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/products/{id}", response.Headers.Location?.OriginalString);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal(("bolt-m8", "Bolt M8", 1.25m, "Zinc"), (
            body.GetProperty("sku").GetString(), body.GetProperty("name").GetString(),
            body.GetProperty("price").GetDecimal(), body.GetProperty("description").GetString()));
        Assert.False(body.TryGetProperty("tenantId", out _), "Internal fields must not leak into the API.");
        Assert.False(body.TryGetProperty("normalizedSku", out _), "Internal fields must not leak into the API.");
    }

    [Theory]
    [InlineData(null, "Bolt", "1", "sku")]
    [InlineData("51", "Bolt", "1", "sku")]
    [InlineData("B-1", null, "1", "name")]
    [InlineData("B-1", "201", "1", "name")]
    [InlineData("B-1", "Bolt", null, "price")]
    [InlineData("B-1", "Bolt", "-1", "price")]
    [InlineData("B-1", "Bolt", "1.005", "price")]
    public async Task Create_InvalidField_Returns400WithFieldError(
        string? sku, string? name, string? price, string invalidField)
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);

        // EN: A whole number for sku / name means "a string of this many characters" — one over the limit.
        // TR: sku / ad için tam sayı "bu kadar karakterlik bir metin" demek — sınırın bir fazlası.
        using var response = await CreateAsync(client, new
        {
            sku = Expand(sku),
            name = Expand(name),
            price = price is null ? (decimal?)null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => string.Equals(e.Name, invalidField, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- Read, update, delete

    [Fact]
    public async Task Get_OwnProduct_Returns200WithSameETag()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateNewAsync(client, "Readable", null, Ct);

        using var response = await client.GetAsync($"/products/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(etag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task Update_WithCurrentETag_Returns200AndNewETag()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var sku = UniqueSku();
        var (id, etag) = await CreateNewAsync(client, "Old Name", sku, Ct);

        using var response = await UpdateAsync(client, id, new { sku, name = "New Name", price = 2.5m }, etag, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(etag, response.Headers.ETag?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(("New Name", 2.5m), (body.GetProperty("name").GetString(), body.GetProperty("price").GetDecimal()));
    }

    [Fact]
    public async Task Update_WithStaleOrMissingETag_Returns412Or428()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var sku = UniqueSku();
        var (id, etag) = await CreateNewAsync(client, "Shared", sku, Ct);
        using var first = await UpdateAsync(client, id, new { sku, name = "First wins", price = 1m }, etag, Ct);

        using var stale = await UpdateAsync(client, id, new { sku, name = "Second is stale", price = 1m }, etag, Ct);
        using var missing = await UpdateAsync(client, id, new { sku, name = "Blind write", price = 1m }, ifMatch: null, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
    }

    [Fact]
    public async Task Delete_ThenGet_Returns204Then404()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, _) = await CreateNewAsync(client, "Short-lived", null, Ct);

        using var delete = await client.DeleteAsync($"/products/{id}", Ct);
        using var get = await client.GetAsync($"/products/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---------------------------------------------------------------- Tenant isolation and permissions

    [Fact]
    public async Task AnotherTenant_CannotGetUpdateOrDelete_Gets404()
    {
        using var owner = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        using var stranger = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var sku = UniqueSku();
        var (id, etag) = await CreateNewAsync(owner, "Private", sku, Ct);

        using var get = await stranger.GetAsync($"/products/{id}", Ct);
        using var update = await UpdateAsync(stranger, id, new { sku, name = "Hijacked", price = 0m }, etag, Ct);
        using var delete = await stranger.DeleteAsync($"/products/{id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanReadButNotCreate_Member_CannotDelete()
    {
        // EN: products.* was only declared; the roles picked it up by its suffix (ADR-025).
        // TR: products.* sadece bildirildi; roller onu sonekinden aldı (ADR-025).
        var (owner, _) = await UsersApi.CreateCompanyAsync(app, Ct);
        var (id, _) = await CreateNewAsync(owner, "Guarded", null, Ct);
        using var viewer = await UsersApi.SignInAsNewUserAsync(app, owner, "Viewer", Ct);
        using var member = await UsersApi.SignInAsNewUserAsync(app, owner, "Member", Ct);

        using var viewerRead = await viewer.GetAsync($"/products/{id}", Ct);
        using var viewerCreate = await CreateAsync(viewer, new { sku = UniqueSku(), name = "No", price = 1m }, Ct);
        using var memberCreate = await CreateAsync(member, new { sku = UniqueSku(), name = "Yes", price = 1m }, Ct);
        using var memberDelete = await member.DeleteAsync($"/products/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, viewerRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, memberCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberDelete.StatusCode);
    }

    // ---------------------------------------------------------------- SKU uniqueness (ADR-016)

    [Fact]
    public async Task Create_SameSkuInSameTenantAnyCase_Returns409()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var sku = UniqueSku();
        await CreateNewAsync(client, "First", sku.ToLowerInvariant(), Ct);

        using var duplicate = await CreateAsync(client, new { sku = sku.ToUpperInvariant(), name = "Second", price = 1m }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Create_SameSkuInAnotherTenant_OrAfterDelete_IsAllowed()
    {
        using var first = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        using var second = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var sku = UniqueSku();
        var (id, _) = await CreateNewAsync(first, "Tenant A's", sku, Ct);

        using var otherTenant = await CreateAsync(second, new { sku, name = "Tenant B's", price = 1m }, Ct);
        using var delete = await first.DeleteAsync($"/products/{id}", Ct);
        using var reused = await CreateAsync(first, new { sku, name = "Back again", price = 1m }, Ct);

        Assert.Equal(HttpStatusCode.Created, otherTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
    }

    // ---------------------------------------------------------------- Change history (ADR-011)

    [Fact]
    public async Task Update_AuditedFields_WriteChangeHistory()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var sku = UniqueSku();
        var (id, etag) = await CreateNewAsync(client, "Before", sku, Ct);
        using var update = await UpdateAsync(
            client, id, new { sku, name = "Before", price = 12.5m, description = "not audited" }, etag, Ct);

        await using var connection = new NpgsqlConnection(await app.App.GetConnectionStringAsync("products-db", Ct));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "select property, old_value, new_value from audit_log where entity_id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(Ct);

        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal(("Price", "9.99", "12.5"), (reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        Assert.False(await reader.ReadAsync(Ct), "Only audited properties that changed may appear in the change history.");
    }

    /// <summary>
    /// EN: Turns a whole-number string like "51" into a 51-character value; other values pass through.
    /// TR: "51" gibi tam sayı bir metni 51 karakterlik bir değere çevirir; diğer değerler olduğu gibi geçer.
    /// </summary>
    /// <param name="value">EN: Value or length. TR: Değer veya uzunluk.</param>
    /// <returns>EN: The value. TR: Değer.</returns>
    private static string? Expand(string? value) =>
        int.TryParse(value, out var length) ? new string('x', length) : value;
}
