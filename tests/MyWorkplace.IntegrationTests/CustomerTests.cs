using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static MyWorkplace.IntegrationTests.CustomersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The reference module (T-009) through the gateway: every status code of ADR-016 / ADR-017, tenant isolation
///     and change history — the behavior every later module must copy.
/// TR: Gateway üzerinden referans modül (T-009): ADR-016 / ADR-017'nin her durum kodu, firma izolasyonu ve
///     değişiklik geçmişi — sonraki her modülün kopyalaması gereken davranış.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class CustomerTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- Create

    [Fact]
    public async Task Create_ValidCustomer_Returns201WithLocationAndETag()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);

        using var response = await CreateAsync(client, new { name = "  Acme Ltd  ", email = "Info@Acme.test", phone = "0212 000 00 00" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/customers/{id}", response.Headers.Location?.OriginalString);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("Acme Ltd", body.GetProperty("name").GetString());
        Assert.False(body.TryGetProperty("tenantId", out _), "Internal fields must not leak into the API.");
    }

    [Theory]
    [InlineData(null, null, null, null, "name")]
    [InlineData("201", null, null, null, "name")]
    [InlineData("Acme", "not-an-email", null, null, "email")]
    [InlineData("Acme", null, "31", null, "phone")]
    [InlineData("Acme", null, null, "21", "taxNumber")]
    public async Task Create_InvalidField_Returns400WithFieldError(
        string? name, string? email, string? phone, string? taxNumber, string invalidField)
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);

        // EN: A number means "a string of this many characters" — one over the limit.
        // TR: Sayı "bu kadar karakterlik bir metin" demek — sınırın bir fazlası.
        using var response = await CreateAsync(client, new
        {
            name = Expand(name),
            email,
            phone = Expand(phone),
            taxNumber = Expand(taxNumber),
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => string.Equals(e.Name, invalidField, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- Read

    [Fact]
    public async Task Get_OwnCustomer_Returns200WithSameETag()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateNewAsync(client, "Readable Co", null, Ct);

        using var response = await client.GetAsync($"/customers/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(etag, response.Headers.ETag?.ToString());
    }

    // ---------------------------------------------------------------- Update (ADR-017)

    [Fact]
    public async Task Update_WithCurrentETag_Returns200AndNewETag()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateNewAsync(client, "Old Name", null, Ct);

        using var response = await UpdateAsync(client, id, new { name = "New Name" }, etag, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(etag, response.Headers.ETag?.ToString());
        Assert.Equal("New Name", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_WithStaleETag_Returns412()
    {
        // EN: Two users read the same version; the first saves, the second must not overwrite that change.
        // TR: İki kullanıcı aynı sürümü okur; ilki kaydeder, ikincisi o değişikliği ezmemelidir.
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateNewAsync(client, "Shared", null, Ct);
        using var first = await UpdateAsync(client, id, new { name = "First wins" }, etag, Ct);

        using var second = await UpdateAsync(client, id, new { name = "Second is stale" }, etag, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutIfMatch_Returns428()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, _) = await CreateNewAsync(client, "No Precondition", null, Ct);

        using var response = await UpdateAsync(client, id, new { name = "Blind write" }, ifMatch: null, Ct);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public async Task Delete_ThenGet_Returns204Then404()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, _) = await CreateNewAsync(client, "Short-lived", null, Ct);

        using var delete = await client.DeleteAsync($"/customers/{id}", Ct);
        using var get = await client.GetAsync($"/customers/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---------------------------------------------------------------- Tenant isolation (ADR-016: 404, never 403)

    [Fact]
    public async Task AnotherTenant_CannotGetUpdateOrDelete_Gets404()
    {
        using var owner = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        using var stranger = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateNewAsync(owner, "Private Co", null, Ct);

        using var get = await stranger.GetAsync($"/customers/{id}", Ct);
        using var update = await UpdateAsync(stranger, id, new { name = "Hijacked" }, etag, Ct);
        using var delete = await stranger.DeleteAsync($"/customers/{id}", Ct);
        using var ownerView = await owner.GetAsync($"/customers/{id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal("Private Co", (await ownerView.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("name").GetString());
    }

    // ---------------------------------------------------------------- Email uniqueness (ADR-016)

    [Fact]
    public async Task Create_SameEmailInSameTenantAnyCase_Returns409()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var email = IdentityApi.UniqueEmail();
        await CreateNewAsync(client, "First", email, Ct);

        using var duplicate = await CreateAsync(client, new { name = "Second", email = email.ToUpperInvariant() }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Create_SameEmailInAnotherTenant_IsAllowed()
    {
        using var first = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        using var second = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var email = IdentityApi.UniqueEmail();
        await CreateNewAsync(first, "Tenant A's customer", email, Ct);

        using var response = await CreateAsync(second, new { name = "Tenant B's customer", email }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmailOfDeletedCustomer_CanBeReused()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var email = IdentityApi.UniqueEmail();
        var (id, _) = await CreateNewAsync(client, "Gone", email, Ct);
        using var delete = await client.DeleteAsync($"/customers/{id}", Ct);

        using var response = await CreateAsync(client, new { name = "Back again", email }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---------------------------------------------------------------- Change history (ADR-011)

    [Fact]
    public async Task Update_AuditedField_WritesChangeHistory()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        var (id, etag) = await CreateNewAsync(client, "Before", null, Ct);
        using var update = await UpdateAsync(client, id, new { name = "After", notes = "not audited" }, etag, Ct);

        var connectionString = await app.App.GetConnectionStringAsync("customers-db", Ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "select property, old_value, new_value from audit_log where entity_id = @id order by changed_at", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(Ct);

        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal(("Name", "Before", "After"), (reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        Assert.False(await reader.ReadAsync(Ct), "Only audited properties may appear in the change history.");
    }

    /// <summary>
    /// EN: Turns a numeric string like "31" into a 31-character value; other values pass through.
    /// TR: "31" gibi sayısal bir metni 31 karakterlik bir değere çevirir; diğer değerler olduğu gibi geçer.
    /// </summary>
    /// <param name="value">EN: Value or length. TR: Değer veya uzunluk.</param>
    /// <returns>EN: The value. TR: Değer.</returns>
    private static string? Expand(string? value) =>
        int.TryParse(value, out var length) ? new string('x', length) : value;
}
