using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: <c>POST /orders/{id}/cancel</c> — cancels a placed order and publishes <c>OrderCancelled</c> in the same
///     transaction, so Inventory returns its stock (ADR-024, T-040).
/// TR: <c>POST /orders/{id}/cancel</c> — verilmiş bir siparişi iptal eder ve <c>OrderCancelled</c>'ı aynı transaction'da yayınlar; böylece
///     Inventory stoğunu geri verir (ADR-024, T-040).
/// </summary>
public static class CancelOrder
{
    /// <summary>
    /// EN: Maps the endpoint on the /orders group.
    /// TR: Uç noktayı /orders grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /orders group. TR: /orders grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapCancelOrder(this IEndpointRouteBuilder group) =>
        group.MapPost("/{id:guid}/cancel", HandleAsync)
            .WithName("CancelOrder")
            .RequireAuthorization(Permissions.Orders.Cancel)
            .WithSummary("EN: Cancel an order | TR: Siparişi iptal et")
            .WithDescription(
                "EN: Cancels a placed order, as a whole, with an optional reason; the stock it took comes back. Requires " +
                "If-Match (428 / 412). A draft (delete it instead) or a cancelled order → 409. Owner and Admin only. " +
                "TR: Verilmiş bir siparişi bütün olarak, isteğe bağlı bir nedenle iptal eder; aldığı stok geri gelir. If-Match gerekir " +
                "(428 / 412). Taslak (onun yerine silin) veya iptal edilmiş sipariş → 409. Sadece Owner ve Admin.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request.
    /// TR: İsteği işler.
    /// </summary>
    /// <param name="id">EN: Order id. TR: Sipariş kimliği.</param>
    /// <param name="input">EN: Cancel form. TR: İptal formu.</param>
    /// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
    /// <param name="outbox">EN: Event outbox. TR: Olay outbox'ı.</param>
    /// <param name="currentUser">EN: Who cancels. TR: İptal eden.</param>
    /// <param name="time">EN: Clock. TR: Saat.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404, 409, 412 or 428. TR: 200, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<OrderResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        CancelOrderInput input,
        OrdersDbContext db,
        IEventOutbox outbox,
        ICurrentUser currentUser,
        TimeProvider time,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        var order = await db.Orders.FindForUpdateAsync(id, cancellationToken);
        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!order.IsPlaced)
        {
            return OrderProblems.NotCancellable(order.Status);
        }

        if (db.GetVersion(order) != expectedVersion)
        {
            return ETags.PreconditionFailed();
        }

        db.ExpectVersion(order, expectedVersion);
        order.Cancel(input.Reason, currentUser.UserId, time.GetUtcNow());
        await outbox.AddAsync(new OrderCancelled { TenantId = order.TenantId, OrderId = order.Id, Number = order.Number!.Value });

        try
        {
            await outbox.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ETags.PreconditionFailed();
        }

        http.Response.SetETag(db.GetVersion(order));
        return TypedResults.Ok(OrderResponse.From(order));
    }
}
