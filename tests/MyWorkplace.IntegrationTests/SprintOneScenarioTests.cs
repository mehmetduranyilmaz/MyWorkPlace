using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The Sprint 1 goal as one readable story (T-012): "a Basic company can't use Inventory, a Pro company can" —
///     living documentation of what the system does, run against the real system through the gateway.
/// TR: Sprint 1 hedefi tek bir okunur hikâye olarak (T-012): "Basic firma Stok'u kullanamaz, Pro firma kullanabilir" —
///     sistemin ne yaptığının canlı dokümanı; gateway üzerinden gerçek sisteme karşı çalışır.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class SprintOneScenarioTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACompany_StartsOnBasic_UpgradesToPro_AndItsDataStaysItsOwn()
    {
        using var client = app.CreateGatewayClient();

        // 1. EN: Nothing is reachable without a token. TR: Token olmadan hiçbir şeye ulaşılamaz.
        using (var anonymous = await client.GetAsync("/customers", Ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        // 2. EN: A company signs up — on the Basic plan — and signs in. TR: Bir firma kaydolur — Basic planda — ve giriş yapar.
        var email = UniqueEmail();
        await RegisterNewTenantAsync(client, email, Ct);
        using (var login = await LoginAsync(client, email, ValidPassword, Ct))
        {
            Authorize(client, await ReadStringAsync(login, "accessToken"));
        }

        // 3. EN: Basic modules work: it adds a customer. TR: Basic modüller çalışır: bir müşteri ekler.
        using (var customer = await client.PostAsJsonAsync("/customers", new { name = "First customer" }, Ct))
        {
            Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        }

        // 4. EN: Pro modules are refused — by the gateway, before the request reaches Inventory.
        //    TR: Pro modüller reddedilir — istek Inventory'ye ulaşmadan, gateway tarafından.
        using (var refused = await client.GetAsync("/inventory/items", Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        // 5. EN: It upgrades to Pro and immediately receives a token carrying the new plan.
        //    TR: Pro'ya yükseltir ve yeni planı taşıyan token'ı hemen alır.
        using (var upgrade = await UpgradeAsync(client, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, upgrade.StatusCode);
            Authorize(client, await ReadStringAsync(upgrade, "accessToken"));
        }

        // 6. EN: Inventory now works, and the Basic data is still there. TR: Stok artık çalışır ve Basic veriler hâlâ yerinde.
        Guid itemId;
        using (var item = await client.PostAsJsonAsync("/inventory/items", new { sku = "SCN-1", name = "Scenario item", baseUnit = "PCS" }, Ct))
        {
            Assert.Equal(HttpStatusCode.Created, item.StatusCode);
            itemId = (await item.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        }

        Assert.Equal(1, await CountAsync(client, "/customers"));
        Assert.Equal(1, await CountAsync(client, "/inventory/items"));

        // 7. EN: Another Pro company sees none of it: lists are empty and direct access is "not found".
        //    TR: Başka bir Pro firma bunların hiçbirini görmez: listeler boş, doğrudan erişim "bulunamadı".
        using var otherCompany = await CreateProClientAsync(app, Ct);
        Assert.Equal(0, await CountAsync(otherCompany, "/customers"));
        Assert.Equal(0, await CountAsync(otherCompany, "/inventory/items"));
        using (var foreign = await otherCompany.GetAsync($"/inventory/items/{itemId}", Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
    }

    /// <summary>
    /// EN: Reads a string property from a JSON response.
    /// TR: Bir JSON cevabından metin bir alanı okur.
    /// </summary>
    /// <param name="response">EN: The response. TR: Cevap.</param>
    /// <param name="property">EN: Property name. TR: Alan adı.</param>
    /// <returns>EN: The value. TR: Değer.</returns>
    private static async Task<string> ReadStringAsync(HttpResponseMessage response, string property) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty(property).GetString()!;

    /// <summary>
    /// EN: Total number of items a list endpoint reports for the caller.
    /// TR: Bir liste uç noktasının çağıran için bildirdiği toplam öğe sayısı.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="url">EN: List address. TR: Liste adresi.</param>
    /// <returns>EN: The total count. TR: Toplam sayı.</returns>
    private static async Task<int> CountAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url, Ct)).GetProperty("totalCount").GetInt32();
}
