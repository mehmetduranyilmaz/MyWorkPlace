using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Customers.Domain;
using MyWorkplace.Customers.Persistence;

namespace MyWorkplace.Customers.Features;

/// <summary>
/// EN: <c>POST /customers</c> — creates a customer for the caller's company.
/// TR: <c>POST /customers</c> — çağıranın firması için bir müşteri oluşturur.
/// </summary>
public static class CreateCustomer
{
    /// <summary>
    /// EN: Maps the endpoint on the /customers group.
    /// TR: Uç noktayı /customers grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /customers group. TR: /customers grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapCreateCustomer(this IEndpointRouteBuilder group) =>
        group.MapPost("", HandleAsync)
            .WithName("CreateCustomer")
            .WithSummary("EN: Create a customer | TR: Müşteri oluştur")
            .WithDescription(
                "EN: Creates a customer for your company. Returns its address in Location and its version in ETag. " +
                "The email, if given, must be unique within your company. " +
                "TR: Firmanız için bir müşteri oluşturur. Adresini Location'da, sürümünü ETag'de döner. " +
                "E-posta verilmişse firmanız içinde benzersiz olmalıdır.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the input has already been validated.
    /// TR: İsteği işler; girdi zaten doğrulanmıştır.
    /// </summary>
    /// <param name="input">EN: Customer form. TR: Müşteri formu.</param>
    /// <param name="db">EN: Customers database. TR: Customers veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201, or 409 for a duplicate email. TR: 201; tekrar eden e-postada 409.</returns>
    public static async Task<Results<Created<CustomerResponse>, ProblemHttpResult>> HandleAsync(
        CustomerInput input,
        CustomersDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var customer = new Customer();
        customer.Update(input.Name!, input.Email, input.Phone, input.TaxNumber, input.Notes);

        // EN: Fast, friendly answer; the filtered unique index is the real guarantee (see the catch below).
        // TR: Hızlı ve anlaşılır cevap; asıl garanti koşullu benzersiz indekstir (aşağıdaki catch'e bakın).
        if (customer.NormalizedEmail is not null
            && await db.Customers.AnyAsync(c => c.NormalizedEmail == customer.NormalizedEmail, cancellationToken))
        {
            return CustomerProblems.EmailTaken();
        }

        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return CustomerProblems.EmailTaken();
        }

        http.Response.SetETag(db.GetVersion(customer));
        return TypedResults.Created($"/customers/{customer.Id}", CustomerResponse.From(customer));
    }
}
