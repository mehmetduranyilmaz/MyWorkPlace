using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: <c>GET /orders</c> — one page of the caller's orders, newest first (ADR-016).
/// TR: <c>GET /orders</c> — çağıranın siparişlerinden bir sayfa, en yeni önce (ADR-016).
/// </summary>
public static class ListOrders
{
    /// <summary>
    /// EN: Maps the endpoint on the /orders group.
    /// TR: Uç noktayı /orders grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /orders group. TR: /orders grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapListOrders(this IEndpointRouteBuilder group) =>
        group.MapGet("", HandleAsync)
            .WithName("ListOrders")
            .RequireAuthorization(Permissions.Orders.Read)
            .WithSummary("EN: List orders | TR: Siparişleri listele")
            .WithDescription(
                "EN: Returns one page of your orders, newest first, without lines. search matches the customer name " +
                "(case-insensitive) or, if it is a number, the order number. " +
                "TR: Siparişlerinizden en yeni önce, satırsız bir sayfa döner. search, müşteri adında (büyük/küçük harf " +
                "duyarsız) ya da sayıysa sipariş numarasında eşleşir.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the tenant and soft-delete filters apply.
    /// TR: İsteği işler; firma ve soft-delete filtreleri uygulanır.
    /// </summary>
    /// <param name="page">EN: Page and search parameters. TR: Sayfa ve arama parametreleri.</param>
    /// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    public static async Task<Ok<PagedResult<OrderSummaryResponse>>> HandleAsync(
        [AsParameters] PageQuery page,
        OrdersDbContext db,
        CancellationToken cancellationToken)
    {
        var orders = db.Orders.AsQueryable();

        if (page.SearchText is { } text)
        {
            var pattern = SearchPattern.Contains(text);
            orders = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? orders.Where(o => o.Number == number
                    || EF.Functions.ILike(o.CustomerName!, pattern, SearchPattern.EscapeCharacter))
                : orders.Where(o => EF.Functions.ILike(o.CustomerName!, pattern, SearchPattern.EscapeCharacter));
        }

        var result = await orders
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .ToPagedResultAsync(OrderSummaryResponse.Projection, page, cancellationToken);

        return TypedResults.Ok(result);
    }
}
