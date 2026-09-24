using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Customers.Persistence;

namespace MyWorkplace.Customers.Features;

/// <summary>
/// EN: <c>PUT /customers/{id}</c> — full update, protected against lost updates with If-Match (ADR-017).
/// TR: <c>PUT /customers/{id}</c> — tam güncelleme; If-Match ile kayıp güncellemelere karşı korunur (ADR-017).
/// </summary>
public static class UpdateCustomer
{
    /// <summary>
    /// EN: Maps the endpoint on the /customers group.
    /// TR: Uç noktayı /customers grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /customers group. TR: /customers grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapUpdateCustomer(this IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}", HandleAsync)
            .WithName("UpdateCustomer")
            .RequireAuthorization(Permissions.Customers.Write)
            .WithSummary("EN: Update a customer | TR: Müşteriyi güncelle")
            .WithDescription(
                "EN: Replaces all fields. Requires If-Match with the ETag you read: 428 without it, 412 if someone " +
                "changed the customer in the meantime (reload and retry). Returns the new ETag. " +
                "TR: Tüm alanları değiştirir. Okuduğunuz ETag ile If-Match gerekir: yoksa 428, siz düzenlerken biri " +
                "müşteriyi değiştirdiyse 412 (yeniden yükleyip tekrar deneyin). Yeni ETag'i döner.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: precondition, lookup, version check, uniqueness, save.
    /// TR: İsteği işler: ön koşul, arama, sürüm kontrolü, benzersizlik, kaydetme.
    /// </summary>
    /// <param name="id">EN: Customer id. TR: Müşteri kimliği.</param>
    /// <param name="input">EN: Customer form. TR: Müşteri formu.</param>
    /// <param name="db">EN: Customers database. TR: Customers veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404, 409, 412 or 428. TR: 200, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<CustomerResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        CustomerInput input,
        CustomersDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        var customer = await db.Customers.FindForUpdateAsync(id, cancellationToken);
        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        // EN: Cheap early answer; ExpectVersion below also covers a change that lands between this check and the save.
        // TR: Ucuz erken cevap; aşağıdaki ExpectVersion, bu kontrol ile kaydetme arasına düşen bir değişikliği de kapsar.
        if (db.GetVersion(customer) != expectedVersion)
        {
            return ETags.PreconditionFailed();
        }

        db.ExpectVersion(customer, expectedVersion);
        customer.Update(input.Name!, input.Email, input.Phone, input.TaxNumber, input.Notes);

        if (customer.NormalizedEmail is not null
            && await db.Customers.AnyAsync(
                c => c.NormalizedEmail == customer.NormalizedEmail && c.Id != customer.Id, cancellationToken))
        {
            return CustomerProblems.EmailTaken();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ETags.PreconditionFailed();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return CustomerProblems.EmailTaken();
        }

        http.Response.SetETag(db.GetVersion(customer));
        return TypedResults.Ok(CustomerResponse.From(customer));
    }
}
