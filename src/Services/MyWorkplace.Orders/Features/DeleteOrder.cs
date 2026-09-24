using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: <c>DELETE /orders/{id}</c> — deletes a draft (soft delete). Placed orders are kept.
/// TR: <c>DELETE /orders/{id}</c> — bir taslağı siler (soft delete). Verilmiş siparişler korunur.
/// </summary>
public static class DeleteOrder
{
    /// <summary>
    /// EN: Maps the endpoint on the /orders group.
    /// TR: Uç noktayı /orders grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /orders group. TR: /orders grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapDeleteOrder(this IEndpointRouteBuilder group) =>
        group.MapDelete("/{id:guid}", HandleAsync)
            .WithName("DeleteOrder")
            .RequireAuthorization(Permissions.Orders.Delete)
            .WithSummary("EN: Delete a draft order | TR: Taslak siparişi sil")
            .WithDescription(
                "EN: Deletes a draft. A placed order can't be deleted (409). If-Match is not required (ADR-017). " +
                "TR: Bir taslağı siler. Verilmiş sipariş silinemez (409). If-Match gerekmez (ADR-017).");

    /// <summary>
    /// EN: Handles the request.
    /// TR: İsteği işler.
    /// </summary>
    /// <param name="id">EN: Order id. TR: Sipariş kimliği.</param>
    /// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204, 404 or 409. TR: 204, 404 veya 409.</returns>
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        OrdersDbContext db,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindForUpdateAsync(id, cancellationToken);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!order.IsDraft)
        {
            return OrderProblems.AlreadyPlaced();
        }

        db.Orders.Remove(order);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
