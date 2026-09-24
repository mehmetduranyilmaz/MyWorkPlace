using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.CustomersApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The list standard (T-028, ADR-016) on Customers, through the gateway.
/// TR: Liste standardı (T-028, ADR-016), Customers üzerinde, gateway üzerinden.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class CustomerListTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_ShowsAndCountsOnlyOwnCustomers_SortedByName()
    {
        using var mine = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        using var other = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        await CreateNewAsync(mine, "Charlie", null, Ct);
        await CreateNewAsync(mine, "Alpha", null, Ct);
        await CreateNewAsync(mine, "Bravo", null, Ct);
        await CreateNewAsync(other, "Not mine", null, Ct);

        var page = await GetPageAsync(mine, "/customers");

        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Alpha", "Bravo", "Charlie"], Names(page));
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task List_SecondPage_ReturnsTheRemainder()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
        {
            await CreateNewAsync(client, name, null, Ct);
        }

        var page = await GetPageAsync(client, "/customers?page=3&pageSize=2");

        Assert.Equal(["E"], Names(page));
        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_Search_IsCaseInsensitiveAndPartial()
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);
        await CreateNewAsync(client, "Alpha Trade", null, Ct);
        await CreateNewAsync(client, "Beta Market", null, Ct);
        await CreateNewAsync(client, "alphabet soup", null, Ct);
        await CreateNewAsync(client, "Gamma", "contact@alpha-mail.test", Ct);

        var page = await GetPageAsync(client, "/customers?search=ALPHA");

        // EN: This test is about *which* customers match, so it compares sets; ordering has its own test above.
        // TR: Bu test *hangi* müşterilerin eşleştiğiyle ilgili, bu yüzden kümeleri karşılaştırır; sıralamanın kendi testi yukarıda.
        Assert.Equal(
            new HashSet<string> { "Alpha Trade", "alphabet soup", "Gamma" },
            Names(page).ToHashSet());
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_OutOfRangeParameters_Return400(string query)
    {
        using var client = await IdentityApi.CreateSignedInClientAsync(app, Ct);

        using var response = await client.GetAsync($"/customers?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
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
