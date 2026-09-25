using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Units;

/// <summary>
/// EN: <c>PUT /inventory/units/{code}</c> — full update of an own unit, protected with If-Match (ADR-017). The name can
///     always change; code and precision only while no stock item uses the unit (ADR-019).
/// TR: <c>PUT /inventory/units/{code}</c> — kendi biriminin tam güncellemesi, If-Match ile korunur (ADR-017). Ad her zaman
///     değişebilir; kod ve hassasiyet sadece hiçbir stok kalemi birimi kullanmıyorken (ADR-019).
/// </summary>
public static class UpdateUnit
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/units group.
    /// TR: Uç noktayı /inventory/units grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The units group. TR: Birimler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapUpdateUnit(this IEndpointRouteBuilder group) =>
        group.MapPut("/{code}", HandleAsync)
            .WithName("UpdateUnit")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Update a unit | TR: Birimi güncelle")
            .WithDescription(
                "EN: Replaces code, name and precision of one of your units. Code and precision can't change while stock " +
                "items use the unit (409); system units can't be changed (409). Requires If-Match: 428 without it, 412 if " +
                "the unit changed meanwhile. " +
                "TR: Birimlerinizden birinin kodunu, adını ve hassasiyetini değiştirir. Stok kalemleri birimi kullanırken kod ve " +
                "hassasiyet değişemez (409); sistem birimleri değiştirilemez (409). If-Match gerekir: yoksa 428, birim bu arada " +
                "değiştiyse 412.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: system unit, precondition, lookup, version, frozen fields, uniqueness, save.
    /// TR: İsteği işler: sistem birimi, ön koşul, arama, sürüm, donmuş alanlar, benzersizlik, kaydetme.
    /// </summary>
    /// <param name="code">EN: Current unit code. TR: Birimin mevcut kodu.</param>
    /// <param name="input">EN: Unit form. TR: Birim formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="catalog">EN: Unit catalog. TR: Birim kataloğu.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404, 409, 412 or 428. TR: 200, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<UnitResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        string code,
        UnitInput input,
        InventoryDbContext db,
        UnitCatalog catalog,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // EN: Never allowed, whatever the precondition says. TR: Ön koşul ne derse desin asla izinli değil.
        if (SystemUnits.Find(code) is not null)
        {
            return UnitProblems.SystemUnit();
        }

        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        var current = UnitOfMeasure.NormalizeCode(code);
        var unit = await db.Units.AsTracking().FirstOrDefaultAsync(u => u.Code == current, cancellationToken);
        if (unit is null)
        {
            return TypedResults.NotFound();
        }

        if (db.GetVersion(unit) != expectedVersion)
        {
            return ETags.PreconditionFailed();
        }

        var newCode = UnitOfMeasure.NormalizeCode(input.Code!);
        var codeChanges = newCode != unit.Code;
        if ((codeChanges || input.Precision != unit.Precision) && await catalog.IsInUseAsync(unit.Code, cancellationToken))
        {
            return UnitProblems.InUse();
        }

        if (codeChanges
            && (SystemUnits.Find(newCode) is not null
                || await db.Units.AnyAsync(u => u.Code == newCode && u.Id != unit.Id, cancellationToken)))
        {
            return UnitProblems.CodeTaken();
        }

        db.ExpectVersion(unit, expectedVersion);
        unit.Update(newCode, input.Name!, input.Precision!.Value);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ETags.PreconditionFailed();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return UnitProblems.CodeTaken();
        }

        http.Response.SetETag(db.GetVersion(unit));
        return TypedResults.Ok(UnitResponse.From(unit.ToDefinition()));
    }
}
