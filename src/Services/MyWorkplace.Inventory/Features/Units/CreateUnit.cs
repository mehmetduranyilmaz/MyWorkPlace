using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Units;

/// <summary>
/// EN: <c>POST /inventory/units</c> — adds one of the company's own units (ADR-019).
/// TR: <c>POST /inventory/units</c> — firmanın kendi birimlerinden birini ekler (ADR-019).
/// </summary>
public static class CreateUnit
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/units group.
    /// TR: Uç noktayı /inventory/units grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The units group. TR: Birimler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapCreateUnit(this IEndpointRouteBuilder group) =>
        group.MapPost("", HandleAsync)
            .WithName("CreateUnit")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Add a unit | TR: Birim ekle")
            .WithDescription(
                "EN: Adds a unit to your catalog. The code (letters, digits, '-', '_'; stored upper-case) must not be used " +
                "by a system unit or another of your units (409). Precision is the number of decimals a quantity may have, 0–3. " +
                "TR: Kataloğunuza bir birim ekler. Kod (harf, rakam, '-', '_'; büyük harfle saklanır) bir sistem birimi veya " +
                "başka bir biriminiz tarafından kullanılmamalıdır (409). Hassasiyet, bir miktarın alabileceği ondalık hane sayısıdır, 0–3.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the input has already been validated.
    /// TR: İsteği işler; girdi zaten doğrulanmıştır.
    /// </summary>
    /// <param name="input">EN: Unit form. TR: Birim formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201, or 409 for a used code. TR: 201; kullanılan kodda 409.</returns>
    public static async Task<Results<Created<UnitResponse>, ProblemHttpResult>> HandleAsync(
        UnitInput input,
        InventoryDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var unit = new UnitOfMeasure();
        unit.Update(input.Code!, input.Name!, input.Precision!.Value);

        if (SystemUnits.Find(unit.Code) is not null
            || await db.Units.AnyAsync(u => u.Code == unit.Code, cancellationToken))
        {
            return UnitProblems.CodeTaken();
        }

        db.Units.Add(unit);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return UnitProblems.CodeTaken();
        }

        http.Response.SetETag(db.GetVersion(unit));
        return TypedResults.Created($"/inventory/units/{unit.Code}", UnitResponse.From(unit.ToDefinition()));
    }
}
