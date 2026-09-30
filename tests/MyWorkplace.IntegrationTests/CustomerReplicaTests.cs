using System.Net.Http.Json;
using System.Text.Json;
using static MyWorkplace.IntegrationTests.IdentityApi;
using static MyWorkplace.IntegrationTests.Json;

namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: Orders' customer replica end to end (T-039, ADR-024): Customers publishes, Orders validates drafts against its
///     copy and takes the name from it — never calling Customers. The copy is eventually consistent, so tests wait.
/// TR: Uçtan uca Orders'ın müşteri kopyası (T-039, ADR-024): Customers yayınlar, Orders taslakları kendi kopyasına göre doğrular ve adı ondan
///     alır — Customers'ı hiç çağırmadan. Kopya olaya dayalı olarak tutarlıdır; bu yüzden testler bekler.
/// </summary>
/// <param name="app">EN: The running system. TR: Çalışan sistem.</param>
public sealed class CustomerReplicaTests(AppFixture app)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Customers_PublishesCustomerCreated()
    {
        await using var tap = await EventTap.StartAsync(app, "CustomerCreated", Ct);
        using var client = await CreateSignedInClientAsync(app, Ct);

        var (customerId, _) = await CustomersApi.CreateNewAsync(client, "Acme", email: null, Ct);

        var published = await tap.WaitForAsync(e => Property(e, "customerId")?.GetGuid() == customerId, Ct);
        Assert.Equal("Acme", Property(published, "name")!.Value.GetString());
    }

    [Fact]
    public async Task Draft_TakesTheNameFromCustomers_NotFromTheClient()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (customerId, _) = await CustomersApi.CreateNewAsync(client, "Acme Ltd", email: null, Ct);

        var order = await CreateDraftWhenReplicatedAsync(client, customerId, clientName: "Something else");

        Assert.Equal("Acme Ltd", order.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task RenamedCustomer_NextDraftsGetTheNewName()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (customerId, etag) = await CustomersApi.CreateNewAsync(client, "Old Name", email: null, Ct);
        await CreateDraftWhenReplicatedAsync(client, customerId);

        using var renamed = await CustomersApi.UpdateAsync(client, customerId, new { name = "New Name" }, etag, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        await Eventually.UntilAsync(
            async () =>
            {
                using var draft = await DraftAsync(client, customerId);
                return (await draft.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("customerName").GetString() == "New Name";
            },
            "the rename to reach Orders",
            Ct);
    }

    [Fact]
    public async Task DeletedCustomer_CantBeOnANewDraft()
    {
        using var client = await CreateSignedInClientAsync(app, Ct);
        var (customerId, _) = await CustomersApi.CreateNewAsync(client, "Leaving", email: null, Ct);
        await CreateDraftWhenReplicatedAsync(client, customerId);

        using var deleted = await client.DeleteAsync($"/customers/{customerId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await Eventually.UntilAsync(
            async () =>
            {
                using var draft = await DraftAsync(client, customerId);
                return draft.StatusCode == HttpStatusCode.BadRequest;
            },
            "the deletion to reach Orders",
            Ct);
    }

    [Fact]
    public async Task AnotherCompanysCustomer_IsRefused()
    {
        using var owner = await CreateSignedInClientAsync(app, Ct);
        using var stranger = await CreateSignedInClientAsync(app, Ct);
        var (customerId, _) = await CustomersApi.CreateNewAsync(owner, "Private", email: null, Ct);

        // EN: Wait until it is replicated for its own company, so the refusal below isn't just "not yet".
        // TR: Kendi firması için kopyalanana kadar beklenir; böylece aşağıdaki ret sadece "henüz değil" olmaz.
        await CreateDraftWhenReplicatedAsync(owner, customerId);
        using var draft = await DraftAsync(stranger, customerId);

        Assert.Equal(HttpStatusCode.BadRequest, draft.StatusCode);
        var errors = (await draft.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("CustomerId", out _));
    }

    /// <summary>
    /// EN: Creates a draft for a customer, retrying until the replica knows the customer; returns the order.
    /// TR: Bir müşteri için taslak oluşturur; kopya müşteriyi tanıyana kadar yeniden dener; siparişi döner.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="customerId">EN: The customer. TR: Müşteri.</param>
    /// <param name="clientName">EN: A name the client sends (ignored). TR: İstemcinin gönderdiği ad (yok sayılır).</param>
    /// <returns>EN: The created order. TR: Oluşturulan sipariş.</returns>
    private static async Task<JsonElement> CreateDraftWhenReplicatedAsync(HttpClient client, Guid customerId, string? clientName = null)
    {
        JsonElement order = default;
        await Eventually.UntilAsync(
            async () =>
            {
                using var draft = await DraftAsync(client, customerId, clientName);
                if (draft.StatusCode != HttpStatusCode.Created)
                {
                    return false;
                }

                order = await draft.Content.ReadFromJsonAsync<JsonElement>(Ct);
                return true;
            },
            "the customer to reach Orders",
            Ct);
        return order;
    }

    /// <summary>
    /// EN: Posts a one-line draft for a customer.
    /// TR: Bir müşteri için tek satırlı bir taslak gönderir.
    /// </summary>
    /// <param name="client">EN: Signed-in client. TR: Giriş yapmış istemci.</param>
    /// <param name="customerId">EN: The customer. TR: Müşteri.</param>
    /// <param name="clientName">EN: A name the client sends. TR: İstemcinin gönderdiği ad.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private static Task<HttpResponseMessage> DraftAsync(HttpClient client, Guid customerId, string? clientName = null) =>
        OrdersApi.CreateAsync(client, new
        {
            customerId,
            customerName = clientName,
            lines = new[] { new { sku = "A", name = "A", quantity = 1m, unitPrice = 1m } },
        }, Ct);

}
