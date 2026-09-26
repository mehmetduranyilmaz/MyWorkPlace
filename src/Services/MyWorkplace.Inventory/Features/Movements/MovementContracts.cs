using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Features.Movements;

/// <summary>
/// EN: Form of a manual stock movement (ADR-020). The quantity is in <see cref="Unit"/>, or in the base unit if none.
/// TR: Elle bir stok hareketinin formu (ADR-020). Miktar <see cref="Unit"/> cinsindendir; birim yoksa temel birimdedir.
/// </summary>
public sealed record MovementInput
{
    /// <summary>EN: Largest quantity of one movement. TR: Bir hareketin en büyük miktarı.</summary>
    public const double MaxQuantity = 1_000_000;

    /// <summary>EN: In or Out. TR: In veya Out.</summary>
    [Required]
    public StockMovementType? Type { get; init; }

    /// <summary>EN: Quantity, greater than zero. TR: Miktar, sıfırdan büyük.</summary>
    [Required]
    [Range(0, MaxQuantity, MinimumIsExclusive = true)]
    public decimal? Quantity { get; init; }

    /// <summary>EN: One of the item's units (optional; the base unit by default). TR: Kalemin birimlerinden biri (isteğe bağlı; varsayılan temel birim).</summary>
    [MaxLength(UnitOfMeasure.CodeMaxLength)]
    [RegularExpression(UnitOfMeasure.CodePattern)]
    public string? Unit { get; init; }

    /// <summary>EN: Optional note, e.g. a delivery note number. TR: İsteğe bağlı not, ör. irsaliye numarası.</summary>
    [MaxLength(StockMovement.NoteMaxLength)]
    public string? Note { get; init; }
}

/// <summary>
/// EN: A movement as the API returns it — one line of an item's stock history.
/// TR: API'nin döndürdüğü haliyle bir hareket — bir kalemin stok geçmişinin bir satırı.
/// </summary>
/// <param name="Id">EN: Movement id. TR: Hareket kimliği.</param>
/// <param name="Type">EN: In or Out. TR: In veya Out.</param>
/// <param name="Quantity">EN: Quantity as entered. TR: Girildiği haliyle miktar.</param>
/// <param name="Unit">EN: Entered unit. TR: Girilen birim.</param>
/// <param name="Factor">EN: Base units in one entered unit. TR: Girilen birimin bir tanesindeki temel birim sayısı.</param>
/// <param name="BaseQuantity">EN: Quantity in the base unit. TR: Temel birimde miktar.</param>
/// <param name="BalanceAfter">EN: The item's balance right after it. TR: Hemen sonrasında kalemin bakiyesi.</param>
/// <param name="Reason">EN: Order or Manual. TR: Order veya Manual.</param>
/// <param name="Note">EN: Note of a manual movement. TR: Elle bir hareketin notu.</param>
/// <param name="OrderNumber">EN: Number of the order behind it. TR: Arkasındaki siparişin numarası.</param>
/// <param name="CausedNegativeStock">EN: An issue that ended below zero. TR: Sıfırın altında biten bir çıkış.</param>
/// <param name="CreatedBy">EN: Who recorded it (none for orders). TR: Kimin kaydettiği (siparişlerde yok).</param>
/// <param name="CreatedAt">EN: When. TR: Ne zaman.</param>
public sealed record MovementResponse(
    Guid Id,
    StockMovementType Type,
    decimal Quantity,
    string Unit,
    decimal Factor,
    decimal BaseQuantity,
    decimal BalanceAfter,
    StockMovementReason Reason,
    string? Note,
    int? OrderNumber,
    bool CausedNegativeStock,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// EN: The one mapping from entity to API shape (ADR-021).
    /// TR: Entity'den API biçimine tek eşleme (ADR-021).
    /// </summary>
    public static readonly Expression<Func<StockMovement, MovementResponse>> Projection = m =>
        new MovementResponse(
            m.Id, m.Type, m.EnteredQuantity, m.UnitCode, m.Factor, m.Quantity, m.BalanceAfter, m.Reason, m.Note,
            m.OrderNumber, m.CausedNegativeStock, m.CreatedBy, m.CreatedAt);

    /// <summary>EN: <see cref="Projection"/>, compiled once. TR: Bir kez derlenmiş <see cref="Projection"/>.</summary>
    private static readonly Func<StockMovement, MovementResponse> _map = Projection.Compile();

    /// <summary>
    /// EN: Maps a movement already in memory.
    /// TR: Bellekteki bir hareketi eşler.
    /// </summary>
    /// <param name="movement">EN: The movement. TR: Hareket.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static MovementResponse From(StockMovement movement) => _map(movement);
}

/// <summary>
/// EN: Answer to recording a movement: the movement and what the UI should tell the user.
/// TR: Bir hareketi kaydetmenin cevabı: hareket ve arayüzün kullanıcıya söylemesi gerekenler.
/// </summary>
/// <param name="Movement">EN: The recorded movement (its balance after included). TR: Kaydedilen hareket (sonraki bakiyesi dahil).</param>
/// <param name="Warnings">EN: E.g. <c>NegativeStock</c> under <c>Warn</c>; empty otherwise. TR: Ör. <c>Warn</c> altında <c>NegativeStock</c>; değilse boş.</param>
public sealed record RecordMovementResponse(MovementResponse Movement, IReadOnlyList<string> Warnings);

/// <summary>
/// EN: Warning codes a movement can come back with.
/// TR: Bir hareketin dönebileceği uyarı kodları.
/// </summary>
public static class MovementWarnings
{
    /// <summary>EN: The balance went below zero (policy <c>Warn</c>). TR: Bakiye sıfırın altına indi (politika <c>Warn</c>).</summary>
    public const string NegativeStock = "NegativeStock";
}

/// <summary>
/// EN: Shared answers of the movement endpoints.
/// TR: Hareket uç noktalarının ortak cevapları.
/// </summary>
internal static class MovementProblems
{
    /// <summary>
    /// EN: 409 for an issue larger than the balance under <c>Block</c>.
    /// TR: <c>Block</c> altında bakiyeden büyük bir çıkış için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult NotEnoughStock() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Not enough stock.",
            detail: "The company's negative stock policy is Block: an issue can't take the balance below zero.");
}
