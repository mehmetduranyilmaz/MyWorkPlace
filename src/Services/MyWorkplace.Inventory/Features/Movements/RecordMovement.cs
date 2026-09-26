using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Settings;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.Movements;

/// <summary>
/// EN: <c>POST /inventory/items/{id}/movements</c> — records goods coming in or going out by hand (ADR-020). The quantity
///     is converted to the base unit once, here; the company's negative stock policy decides an issue beyond the balance.
/// TR: <c>POST /inventory/items/{id}/movements</c> — giren veya çıkan malı elle kaydeder (ADR-020). Miktar burada, bir kez temel birime
///     çevrilir; bakiyeyi aşan bir çıkışa firmanın eksi stok politikası karar verir.
/// </summary>
public static class RecordMovement
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapRecordMovement(this IEndpointRouteBuilder group) =>
        group.MapPost("/{id:guid}/movements", HandleAsync)
            .WithName("RecordMovement")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Record a stock movement | TR: Stok hareketi kaydet")
            .WithDescription(
                "EN: Records an In or Out of quantity in one of the item's units (the base unit by default). The quantity must " +
                "fit the unit's decimals, and converted, the base unit's (400 otherwise). Under the negative stock policy " +
                "Block an Out beyond the balance is refused (409); under Warn it is applied and warnings contains " +
                "NegativeStock. Movements are never changed: correct a mistake with an opposite movement. " +
                "TR: Kalemin birimlerinden birinde (varsayılan temel birim) bir In veya Out kaydeder. Miktar birimin ondalığına ve " +
                "çevrildiğinde temel birimin ondalığına uymalıdır (değilse 400). Eksi stok politikası Block iken bakiyeyi aşan bir Out " +
                "reddedilir (409); Warn iken uygulanır ve warnings NegativeStock içerir. Hareketler asla değiştirilmez: bir hatayı ters " +
                "yönde bir hareketle düzeltin.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request: item, unit, precision, policy, then the balance change and its movement in one transaction.
    /// TR: İsteği işler: kalem, birim, hassasiyet, politika; sonra bakiye değişikliği ve hareketi tek bir transaction içinde.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="input">EN: Movement form. TR: Hareket formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="catalog">EN: Unit catalog. TR: Birim kataloğu.</param>
    /// <param name="ledger">EN: Stock ledger. TR: Stok defteri.</param>
    /// <param name="settings">EN: The company's inventory settings. TR: Firmanın stok ayarları.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201, 400, 404 or 409. TR: 201, 400, 404 veya 409.</returns>
    public static async Task<Results<Created<RecordMovementResponse>, NotFound, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        MovementInput input,
        InventoryDbContext db,
        UnitCatalog catalog,
        StockLedger ledger,
        ITenantSettings<InventorySettings> settings,
        CancellationToken cancellationToken)
    {
        var item = await db.StockItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var unitCode = input.Unit is null ? item.BaseUnit : UnitOfMeasure.NormalizeCode(input.Unit);
        var unit = await catalog.FindAsync(unitCode, cancellationToken);
        var baseUnit = await catalog.FindAsync(item.BaseUnit, cancellationToken);
        if (item.FactorOf(unitCode) is not { } factor || unit is null || baseUnit is null)
        {
            return Invalid(nameof(MovementInput.Unit), "The item has no such unit: use its base unit or one of its alternative units.");
        }

        var (baseQuantity, problem) = UnitConversion.ToBase(input.Quantity!.Value, unit, factor, baseUnit);
        switch (problem)
        {
            case QuantityProblem.EnteredUnitPrecision:
                return Invalid(nameof(MovementInput.Quantity), $"{unit.Code} allows {unit.Precision} decimals.");
            case QuantityProblem.BaseUnitPrecision:
                return Invalid(
                    nameof(MovementInput.Quantity),
                    $"Converted to {baseUnit.Code} (× {factor}) it has more than {baseUnit.Precision} decimals.");
        }

        var policy = (await settings.GetAsync(cancellationToken)).NegativeStockPolicy;
        var movement = new ManualMovement(
            input.Type!.Value, input.Quantity.Value, unitCode, factor, baseQuantity,
            string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim());

        // EN: The balance change and its movement commit together or not at all; a transient failure reruns the whole
        //     unit from a clean state (the same pattern as the event dispatcher).
        // TR: Bakiye değişikliği ve hareketi birlikte kaydedilir ya da hiç kaydedilmez; geçici bir hata tüm birimi temiz bir durumdan
        //     yeniden çalıştırır (olay dağıtıcısıyla aynı kalıp).
        var (outcome, recorded) = await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var result = await ledger.RecordManualAsync(id, movement, policy, ct);
                if (result.Outcome == ManualMovementOutcome.Recorded)
                {
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }

                return result;
            },
            cancellationToken);

        return outcome switch
        {
            ManualMovementOutcome.NotEnoughStock => MovementProblems.NotEnoughStock(),
            ManualMovementOutcome.ItemNotFound => TypedResults.NotFound(),
            _ => TypedResults.Created(
                (string?)null,
                new RecordMovementResponse(
                    MovementResponse.From(recorded!),
                    policy == NegativeStockPolicy.Warn && recorded!.CausedNegativeStock ? [MovementWarnings.NegativeStock] : [])),
        };
    }

    /// <summary>
    /// EN: A 400 for one field, shaped like the automatic validation errors.
    /// TR: Otomatik doğrulama hatalarıyla aynı biçimde, tek bir alan için 400.
    /// </summary>
    /// <param name="field">EN: Field name. TR: Alan adı.</param>
    /// <param name="message">EN: Error message. TR: Hata mesajı.</param>
    /// <returns>EN: The 400. TR: 400.</returns>
    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
