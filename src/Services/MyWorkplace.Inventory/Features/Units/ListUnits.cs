using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Features.Units;

/// <summary>
/// EN: <c>GET /inventory/units</c> — the company's unit catalog: system units and its own (ADR-019).
/// TR: <c>GET /inventory/units</c> — firmanın birim kataloğu: sistem birimleri ve kendi birimleri (ADR-019).
/// </summary>
public static class ListUnits
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/units group.
    /// TR: Uç noktayı /inventory/units grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The units group. TR: Birimler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapListUnits(this IEndpointRouteBuilder group) =>
        group.MapGet("", HandleAsync)
            .WithName("ListUnits")
            .RequireAuthorization(Permissions.Inventory.Read)
            .WithSummary("EN: List units | TR: Birimleri listele")
            .WithDescription(
                "EN: Returns every unit you can use: system units first (isSystem = true), then your own by code. " +
                "Not paged — a catalog stays small. " +
                "TR: Kullanabileceğiniz tüm birimleri döner: önce sistem birimleri (isSystem = true), sonra koda göre kendi " +
                "birimleriniz. Sayfalı değildir — bir katalog küçük kalır.");

    /// <summary>
    /// EN: Handles the request.
    /// TR: İsteği işler.
    /// </summary>
    /// <param name="catalog">EN: Unit catalog. TR: Birim kataloğu.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The units. TR: Birimler.</returns>
    public static async Task<Ok<List<UnitResponse>>> HandleAsync(UnitCatalog catalog, CancellationToken cancellationToken)
    {
        var units = await catalog.ListAsync(cancellationToken);
        return TypedResults.Ok(units.Select(UnitResponse.From).ToList());
    }
}
