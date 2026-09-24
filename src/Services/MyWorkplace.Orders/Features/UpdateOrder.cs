using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: <c>PUT /orders/{id}</c> — replaces a draft's customer and lines, protected with If-Match (ADR-017).
/// TR: <c>PUT /orders/{id}</c> — bir taslağın müşterisini ve satırlarını değiştirir; If-Match ile korunur (ADR-017).
/// </summary>
public static class UpdateOrder
{
    /// <summary>
    /// EN: Maps the endpoint on the /orders group.
    /// TR: Uç noktayı /orders grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /orders group. TR: /orders grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapUpdateOrder(this IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}", HandleAsync)
            .WithName("UpdateOrder")
            .RequireAuthorization(Permissions.Orders.Write)
            .WithSummary("EN: Update a draft order | TR: Taslak siparişi güncelle")
            .WithDescription(
                "EN: Replaces the customer and all lines of a draft. Requires If-Match (428 without it, 412 if stale). " +
                "A placed order can't be changed (409). " +
                "TR: Bir taslağın müşterisini ve tüm satırlarını değiştirir. If-Match gerekir (yoksa 428, eskiyse 412). " +
                "Verilmiş sipariş değiştirilemez (409).")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: precondition, lookup, state, version, save.
    /// TR: İsteği işler: ön koşul, arama, durum, sürüm, kaydetme.
    /// </summary>
    /// <param name="id">EN: Order id. TR: Sipariş kimliği.</param>
    /// <param name="input">EN: Order form. TR: Sipariş formu.</param>
    /// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404, 409, 412 or 428. TR: 200, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<OrderResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        OrderInput input,
        OrdersDbContext db,
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

        if (!order.IsDraft)
        {
            return OrderProblems.AlreadyPlaced();
        }

        if (db.GetVersion(order) != expectedVersion)
        {
            return ETags.PreconditionFailed();
        }

        db.ExpectVersion(order, expectedVersion);
        order.Update(input.CustomerId, input.CustomerName, input.LineValues());

        // EN: Changing only lines doesn't touch the order row, so its version wouldn't move and a stale ETag could still
        //     match. Marking one column modified makes every update write the row, i.e. a new version.
        // TR: Sadece satırları değiştirmek sipariş satırına dokunmaz; sürümü ilerlemez ve eskimiş bir ETag hâlâ eşleşebilirdi.
        //     Bir sütunu değişmiş işaretlemek her güncellemenin satırı yazmasını, yani yeni bir sürüm olmasını sağlar.
        db.Entry(order).Property(o => o.Total).IsModified = true;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ETags.PreconditionFailed();
        }

        http.Response.SetETag(db.GetVersion(order));
        return TypedResults.Ok(OrderResponse.From(order));
    }
}
