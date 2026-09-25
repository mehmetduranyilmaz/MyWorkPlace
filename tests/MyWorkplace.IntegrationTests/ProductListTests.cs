using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.ProductsApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The list standard (ADR-016) on Products, through the gateway (T-013).
/// TR: Liste standardı (ADR-016), Products üzerinde, gateway üzerinden (T-013).
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class ProductListTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_ShowsOnlyOwnProducts_SortedByName_Paged()
    {
        using var mine = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        using var other = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        foreach (var name in new[] { "Charlie", "Alpha", "Echo", "Bravo", "Delta" })
        {
            await CreateNewAsync(mine, name, null, Ct);
        }

        await CreateNewAsync(other, "Not mine", null, Ct);

        var all = await GetPageAsync(mine, "/products");
        var third = await GetPageAsync(mine, "/products?page=3&pageSize=2");

        Assert.Equal(5, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta", "Echo"], Names(all));
        Assert.Equal(["Echo"], Names(third));
    }

    [Fact]
    public async Task List_Search_MatchesNameOrSku_CaseInsensitive()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        await CreateNewAsync(client, "Steel Bolt", "SB-1", Ct);
        await CreateNewAsync(client, "Nut", "BOLT-NUT-1", Ct);
        await CreateNewAsync(client, "Washer", "W-1", Ct);

        var page = await GetPageAsync(client, "/products?search=bolt");

        Assert.Equal(new HashSet<string> { "Steel Bolt", "Nut" }, Names(page).ToHashSet());
    }

    [Fact]
    public async Task List_OutOfRangePageSize_Returns400()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);

        using var response = await client.GetAsync("/products?pageSize=101", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// EN: Gets a list page and asserts success.
    /// TR: Bir liste sayfası alır ve başarıyı doğrular.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="url">EN: List URL. TR: Liste adresi.</param>
    /// <returns>EN: The page body. TR: Sayfa gövdesi.</returns>
    private static async Task<JsonElement> GetPageAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    /// <summary>
    /// EN: Names of the items on a page, in order.
    /// TR: Bir sayfadaki öğelerin adları, sırasıyla.
    /// </summary>
    /// <param name="page">EN: Page body. TR: Sayfa gövdesi.</param>
    /// <returns>EN: The names. TR: Adlar.</returns>
    private static List<string> Names(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!)];
}
