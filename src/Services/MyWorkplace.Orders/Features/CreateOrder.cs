using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Domain;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: <c>POST /orders</c> — creates a draft order.
/// TR: <c>POST /orders</c> — taslak bir sipariş oluşturur.
/// </summary>
public static class CreateOrder
{
    /// <summary>
    /// EN: Maps the endpoint on the /orders group.
    /// TR: Uç noktayı /orders grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /orders group. TR: /orders grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapCreateOrder(this IEndpointRouteBuilder group) =>
        group.MapPost("", HandleAsync)
            .WithName("CreateOrder")
            .RequireAuthorization(Permissions.Orders.Write)
            .WithSummary("EN: Create a draft order | TR: Taslak sipariş oluştur")
            .WithDescription(
                "EN: Creates a draft with at least one line. Line totals and the order total are computed by the " +
                "server. Returns the address in Location and the version in ETag. " +
                "TR: En az bir satırlı bir taslak oluşturur. Satır tutarları ve sipariş toplamı sunucuda hesaplanır. " +
                "Adresi Location'da, sürümü ETag'de döner.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the input has already been validated.
    /// TR: İsteği işler; girdi zaten doğrulanmıştır.
    /// </summary>
    /// <param name="input">EN: Order form. TR: Sipariş formu.</param>
    /// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201. TR: 201.</returns>
    public static async Task<Created<OrderResponse>> HandleAsync(
        OrderInput input,
        OrdersDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var order = new Order();
        order.Update(input.CustomerId, input.CustomerName, input.LineValues());

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        http.Response.SetETag(db.GetVersion(order));
        return TypedResults.Created($"/orders/{order.Id}", OrderResponse.From(order));
    }
}
