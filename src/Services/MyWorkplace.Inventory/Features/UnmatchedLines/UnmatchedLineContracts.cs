using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Features.UnmatchedLines;

/// <summary>
/// EN: An unmatched order line as the API returns it (T-042).
/// TR: API'nin döndürdüğü haliyle eşleşmeyen bir sipariş satırı (T-042).
/// </summary>
/// <param name="Id">EN: Entry id. TR: Kayıt kimliği.</param>
/// <param name="OrderId">EN: The order. TR: Sipariş.</param>
/// <param name="OrderNumber">EN: The order's number. TR: Siparişin numarası.</param>
/// <param name="Sku">EN: SKU as ordered. TR: Sipariş edildiği haliyle SKU.</param>
/// <param name="Name">EN: Name as ordered. TR: Sipariş edildiği haliyle ad.</param>
/// <param name="Quantity">EN: Quantity. TR: Miktar.</param>
/// <param name="Status">EN: Review state. TR: İnceleme durumu.</param>
/// <param name="ResolvedStockItemId">EN: The item it was resolved from. TR: Çözüldüğü kalem.</param>
/// <param name="Note">EN: Dismissal note. TR: Yok sayma notu.</param>
/// <param name="CreatedAt">EN: When it was listed. TR: Ne zaman listelendiği.</param>
public sealed record UnmatchedLineResponse(
    Guid Id,
    Guid OrderId,
    int OrderNumber,
    string Sku,
    string Name,
    decimal Quantity,
    UnmatchedLineStatus Status,
    Guid? ResolvedStockItemId,
    string? Note,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// EN: The one mapping from entity to API shape (ADR-021).
    /// TR: Entity'den API biçimine tek eşleme (ADR-021).
    /// </summary>
    public static readonly Expression<Func<UnmatchedOrderLine, UnmatchedLineResponse>> Projection = l =>
        new UnmatchedLineResponse(
            l.Id, l.OrderId, l.OrderNumber, l.Sku, l.Name, l.Quantity, l.Status, l.ResolvedStockItemId, l.Note, l.CreatedAt);

    /// <summary>EN: <see cref="Projection"/>, compiled once. TR: Bir kez derlenmiş <see cref="Projection"/>.</summary>
    private static readonly Func<UnmatchedOrderLine, UnmatchedLineResponse> _map = Projection.Compile();

    /// <summary>
    /// EN: Maps an entry already in memory.
    /// TR: Bellekteki bir kaydı eşler.
    /// </summary>
    /// <param name="line">EN: The entry. TR: Kayıt.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static UnmatchedLineResponse From(UnmatchedOrderLine line) => _map(line);
}

/// <summary>
/// EN: Form for resolving an entry from a stock item.
/// TR: Bir kaydı bir stok kaleminden çözmek için form.
/// </summary>
public sealed record ResolveInput
{
    /// <summary>EN: The item to issue from. TR: Çıkış yapılacak kalem.</summary>
    [Required]
    public Guid? StockItemId { get; init; }
}

/// <summary>
/// EN: Form for dismissing an entry.
/// TR: Bir kaydı yok saymak için form.
/// </summary>
public sealed record DismissInput
{
    /// <summary>EN: Why (optional). TR: Neden (isteğe bağlı).</summary>
    [MaxLength(UnmatchedOrderLine.NoteMaxLength)]
    public string? Note { get; init; }

    /// <summary>
    /// EN: Also ignore this SKU in later orders (e.g. a service).
    /// TR: Bu SKU'yu sonraki siparişlerde de yok say (ör. bir hizmet).
    /// </summary>
    public bool IgnoreSku { get; init; }
}

/// <summary>
/// EN: An ignored SKU as the API returns it.
/// TR: API'nin döndürdüğü haliyle yok sayılan bir SKU.
/// </summary>
/// <param name="Sku">EN: The SKU. TR: SKU.</param>
/// <param name="CreatedAt">EN: Since when. TR: Ne zamandan beri.</param>
public sealed record IgnoredSkuResponse(string Sku, DateTimeOffset CreatedAt);

/// <summary>
/// EN: Shared answers of the review endpoints.
/// TR: İnceleme uç noktalarının ortak cevapları.
/// </summary>
internal static class UnmatchedLineProblems
{
    /// <summary>
    /// EN: 409 for acting on an entry that is not open — resolved, dismissed, or its order cancelled.
    /// TR: Açık olmayan bir kayıt üzerinde işlem için 409 — çözülmüş, yok sayılmış veya siparişi iptal edilmiş.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult NotOpen() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The entry is not open.",
            detail: "It was resolved, dismissed, or its order was cancelled — nothing is issued twice.");

    /// <summary>
    /// EN: 404 for a stock item that doesn't exist (in this company).
    /// TR: (Bu firmada) var olmayan bir stok kalemi için 404.
    /// </summary>
    /// <returns>EN: A 404 ProblemDetails. TR: 404 ProblemDetails.</returns>
    public static ProblemHttpResult StockItemNotFound() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Stock item not found.",
            detail: "Choose one of your stock items to resolve the entry from.");
}
