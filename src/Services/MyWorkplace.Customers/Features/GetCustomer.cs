using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Customers.Persistence;

namespace MyWorkplace.Customers.Features;

/// <summary>
/// EN: <c>GET /customers/{id}</c> — reads one customer of the caller's company.
/// TR: <c>GET /customers/{id}</c> — çağıranın firmasının bir müşterisini okur.
/// </summary>
public static class GetCustomer
{
    /// <summary>
    /// EN: Maps the endpoint on the /customers group.
    /// TR: Uç noktayı /customers grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /customers group. TR: /customers grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapGetCustomer(this IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetCustomer")
            .WithSummary("EN: Get a customer | TR: Müşteriyi getir")
            .WithDescription(
                "EN: Returns the customer and its version in ETag; send that ETag in If-Match when updating. " +
                "Customers of other companies are reported as not found (404). " +
                "TR: Müşteriyi ve sürümünü ETag'de döner; güncellerken bu ETag'i If-Match ile gönderin. " +
                "Başka firmaların müşterileri bulunamadı (404) olarak döner.");

    /// <summary>
    /// EN: Handles the request with an untracked query that projects the row version for the ETag.
    /// TR: İsteği, ETag için satır sürümünü de yansıtan takipsiz bir sorguyla işler.
    /// </summary>
    /// <param name="id">EN: Customer id. TR: Müşteri kimliği.</param>
    /// <param name="db">EN: Customers database. TR: Customers veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 or 404. TR: 200 veya 404.</returns>
    public static async Task<Results<Ok<CustomerResponse>, NotFound>> HandleAsync(
        Guid id,
        CustomersDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // EN: The tenant and soft-delete filters apply, so another company's or a deleted customer is simply not found.
        // TR: Firma ve soft-delete filtreleri uygulanır; başka firmanın veya silinmiş bir müşteri kısaca bulunamaz.
        var row = await db.Customers
            .Where(c => c.Id == id)
            .Select(c => new
            {
                Customer = new CustomerResponse(c.Id, c.Name, c.Email, c.Phone, c.TaxNumber, c.Notes, c.CreatedAt, c.UpdatedAt),
                Version = EF.Property<uint>(c, ServiceDbContext.ConcurrencyTokenProperty),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.SetETag(row.Version);
        return TypedResults.Ok(row.Customer);
    }
}
