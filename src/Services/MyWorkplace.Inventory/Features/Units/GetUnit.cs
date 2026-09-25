using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Units;

/// <summary>
/// EN: <c>GET /inventory/units/{code}</c> — reads one unit; an own unit comes with its ETag for updating.
/// TR: <c>GET /inventory/units/{code}</c> — bir birimi okur; kendi birimi güncelleme için ETag'iyle gelir.
/// </summary>
public static class GetUnit
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/units group.
    /// TR: Uç noktayı /inventory/units grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The units group. TR: Birimler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapGetUnit(this IEndpointRouteBuilder group) =>
        group.MapGet("/{code}", HandleAsync)
            .WithName("GetUnit")
            .RequireAuthorization(Permissions.Inventory.Read)
            .WithSummary("EN: Get a unit | TR: Birimi getir")
            .WithDescription(
                "EN: Returns a unit by code, any case. Your own units carry their version in ETag; system units have none " +
                "because they can't be changed. Other companies' units are reported as not found (404). " +
                "TR: Bir birimi koduyla döner, harf büyüklüğü fark etmez. Kendi birimleriniz sürümünü ETag'de taşır; sistem " +
                "birimleri değiştirilemediği için taşımaz. Başka firmaların birimleri bulunamadı (404) olarak döner.");

    /// <summary>
    /// EN: Handles the request.
    /// TR: İsteği işler.
    /// </summary>
    /// <param name="code">EN: Unit code. TR: Birim kodu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 or 404. TR: 200 veya 404.</returns>
    public static async Task<Results<Ok<UnitResponse>, NotFound>> HandleAsync(
        string code,
        InventoryDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (SystemUnits.Find(code) is { } system)
        {
            return TypedResults.Ok(UnitResponse.From(system));
        }

        var normalized = UnitOfMeasure.NormalizeCode(code);
        var row = await db.Units
            .Where(u => u.Code == normalized)
            .Select(u => new Versioned<UnitResponse>(
                new UnitResponse(u.Code, u.Name, u.Precision, false),
                EF.Property<uint>(u, ServiceDbContext.ConcurrencyTokenProperty)))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.SetETag(row.Version);
        return TypedResults.Ok(row.Value);
    }
}
