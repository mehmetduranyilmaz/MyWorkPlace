using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Units;

/// <summary>
/// EN: <c>DELETE /inventory/units/{code}</c> — soft delete of an own unit no stock item uses (ADR-019).
/// TR: <c>DELETE /inventory/units/{code}</c> — hiçbir stok kaleminin kullanmadığı kendi biriminin soft delete'i (ADR-019).
/// </summary>
public static class DeleteUnit
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/units group.
    /// TR: Uç noktayı /inventory/units grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The units group. TR: Birimler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapDeleteUnit(this IEndpointRouteBuilder group) =>
        group.MapDelete("/{code}", HandleAsync)
            .WithName("DeleteUnit")
            .RequireAuthorization(Permissions.Inventory.Delete)
            .WithSummary("EN: Delete a unit | TR: Birimi sil")
            .WithDescription(
                "EN: Deletes one of your units that no stock item uses (409 otherwise); system units can't be deleted (409). " +
                "Its code can be used again. " +
                "TR: Hiçbir stok kaleminin kullanmadığı birimlerinizden birini siler (değilse 409); sistem birimleri silinemez " +
                "(409). Kodu tekrar kullanılabilir.");

    /// <summary>
    /// EN: Handles the request.
    /// TR: İsteği işler.
    /// </summary>
    /// <param name="code">EN: Unit code. TR: Birim kodu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="catalog">EN: Unit catalog. TR: Birim kataloğu.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204, 404 or 409. TR: 204, 404 veya 409.</returns>
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync(
        string code,
        InventoryDbContext db,
        UnitCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (SystemUnits.Find(code) is not null)
        {
            return UnitProblems.SystemUnit();
        }

        var normalized = UnitOfMeasure.NormalizeCode(code);
        var unit = await db.Units.AsTracking().FirstOrDefaultAsync(u => u.Code == normalized, cancellationToken);
        if (unit is null)
        {
            return TypedResults.NotFound();
        }

        if (await catalog.IsInUseAsync(unit.Code, cancellationToken))
        {
            return UnitProblems.InUse();
        }

        db.Units.Remove(unit);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
