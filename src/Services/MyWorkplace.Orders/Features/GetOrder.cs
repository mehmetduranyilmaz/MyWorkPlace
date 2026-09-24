using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: <c>GET /orders/{id}</c> — reads one order with its lines.
/// TR: <c>GET /orders/{id}</c> — bir siparişi satırlarıyla okur.
/// </summary>
public static class GetOrder
{
    /// <summary>
    /// EN: Maps the endpoint on the /orders group.
    /// TR: Uç noktayı /orders grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /orders group. TR: /orders grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapGetOrder(this IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetOrder")
            .RequireAuthorization(Permissions.Orders.Read)
            .WithSummary("EN: Get an order | TR: Siparişi getir")
            .WithDescription(
                "EN: Returns the order with its lines and its version in ETag; send that ETag in If-Match when " +
                "updating or placing it. Orders of other companies are reported as not found (404). " +
                "TR: Siparişi satırları ve ETag'deki sürümüyle döner; güncellerken veya verirken bu ETag'i If-Match ile " +
                "gönderin. Başka firmaların siparişleri bulunamadı (404) olarak döner.");

    /// <summary>
    /// EN: Handles the request with an untracked query that projects the row version for the ETag.
    /// TR: İsteği, ETag için satır sürümünü de yansıtan takipsiz bir sorguyla işler.
    /// </summary>
    /// <param name="id">EN: Order id. TR: Sipariş kimliği.</param>
    /// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 or 404. TR: 200 veya 404.</returns>
    public static async Task<Results<Ok<OrderResponse>, NotFound>> HandleAsync(
        Guid id,
        OrdersDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var row = await db.Orders.SingleWithVersionAsync(id, OrderResponse.Projection, cancellationToken);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.SetETag(row.Version);
        return TypedResults.Ok(row.Value);
    }
}
