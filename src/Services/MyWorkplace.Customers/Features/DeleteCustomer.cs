using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Customers.Persistence;

namespace MyWorkplace.Customers.Features;

/// <summary>
/// EN: <c>DELETE /customers/{id}</c> — soft delete: the row is kept (history, audit) but hidden everywhere.
/// TR: <c>DELETE /customers/{id}</c> — soft delete: satır saklanır (geçmiş, denetim) ama her yerde gizlenir.
/// </summary>
public static class DeleteCustomer
{
    /// <summary>
    /// EN: Maps the endpoint on the /customers group.
    /// TR: Uç noktayı /customers grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /customers group. TR: /customers grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapDeleteCustomer(this IEndpointRouteBuilder group) =>
        group.MapDelete("/{id:guid}", HandleAsync)
            .WithName("DeleteCustomer")
            .WithSummary("EN: Delete a customer | TR: Müşteriyi sil")
            .WithDescription(
                "EN: Deletes the customer. The record is kept for history but no longer appears anywhere, and its " +
                "email can be used again. If-Match is not required (ADR-017). " +
                "TR: Müşteriyi siler. Kayıt geçmiş için saklanır ama artık hiçbir yerde görünmez ve e-postası tekrar " +
                "kullanılabilir. If-Match gerekmez (ADR-017).");

    /// <summary>
    /// EN: Handles the request; the soft-delete interceptor turns the removal into an update.
    /// TR: İsteği işler; soft-delete interceptor'ı silme işlemini güncellemeye çevirir.
    /// </summary>
    /// <param name="id">EN: Customer id. TR: Müşteri kimliği.</param>
    /// <param name="db">EN: Customers database. TR: Customers veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204 or 404. TR: 204 veya 404.</returns>
    public static async Task<Results<NoContent, NotFound>> HandleAsync(
        Guid id,
        CustomersDbContext db,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FindForUpdateAsync(id, cancellationToken);
        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
