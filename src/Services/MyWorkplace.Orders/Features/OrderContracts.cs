using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.Orders.Domain;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: Order form for create and (full) update of a draft. Totals are not part of it: the server computes them.
/// TR: Bir taslağın oluşturulması ve (tam) güncellenmesi için sipariş formu. Toplamlar yoktur: sunucu hesaplar.
/// </summary>
public sealed record OrderInput : IValidatableObject
{
    /// <summary>
    /// EN: Customer (optional): one of the company's live customers, checked against the replica (T-039).
    /// TR: Müşteri (isteğe bağlı): firmanın canlı müşterilerinden biri; kopyaya göre kontrol edilir (T-039).
    /// </summary>
    public Guid? CustomerId { get; init; }

    /// <summary>
    /// EN: Ignored since T-039: the name is taken from the customer replica. Still accepted, so older clients don't break.
    /// TR: T-039'dan beri yok sayılır: ad müşteri kopyasından alınır. Eski istemciler bozulmasın diye hâlâ kabul edilir.
    /// </summary>
    public string? CustomerName { get; init; }

    /// <summary>
    /// EN: The lines (1–200). A <c>List</c>, not an array: the validation source generator checks the elements of a
    ///     list but silently skips those of an array (found in T-014).
    /// TR: Satırlar (1–200). Dizi değil <c>List</c>: doğrulama kaynak üreteci bir listenin öğelerini kontrol eder, bir dizininkileri
    ///     ise sessizce atlar (T-014'te bulundu).
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(Order.MaxLines)]
    public List<OrderLineInput>? Lines { get; init; }

    /// <summary>
    /// EN: Cross-field rules of the lines.
    /// TR: Satırların alanlar arası kuralları.
    /// </summary>
    /// <param name="validationContext">EN: Validation context. TR: Doğrulama bağlamı.</param>
    /// <returns>EN: Errors. TR: Hatalar.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // EN: More decimals than the columns hold would be rounded silently by the database, and the stored price would
        //     no longer match the line total computed from it. Reject instead of guessing.
        // TR: Sütunların tuttuğundan fazla ondalık veritabanında sessizce yuvarlanır ve saklanan fiyat, ondan hesaplanan satır
        //     tutarını artık tutmazdı. Tahmin etmek yerine reddedilir.
        foreach (var (line, index) in (Lines ?? []).Select((line, index) => (line, index)))
        {
            if (line.Quantity is { } quantity && decimal.Round(quantity, 3) != quantity)
            {
                yield return new ValidationResult("At most 3 decimals.", [$"{nameof(Lines)}[{index}].{nameof(line.Quantity)}"]);
            }

            if (line.UnitPrice is { } price && decimal.Round(price, 2) != price)
            {
                yield return new ValidationResult("At most 2 decimals.", [$"{nameof(Lines)}[{index}].{nameof(line.UnitPrice)}"]);
            }
        }
    }

    /// <summary>
    /// EN: The lines as domain values.
    /// TR: Domain değerleri olarak satırlar.
    /// </summary>
    /// <returns>EN: Line values. TR: Satır değerleri.</returns>
    public IReadOnlyList<OrderLineValues> LineValues() =>
        [.. Lines!.Select(l => new OrderLineValues(l.Sku!, l.Name!, l.Quantity!.Value, l.UnitPrice!.Value))];
}

/// <summary>
/// EN: One line of the order form.
/// TR: Sipariş formunun bir satırı.
/// </summary>
public sealed record OrderLineInput
{
    /// <summary>EN: Stock keeping unit. TR: Stok kodu.</summary>
    [Required]
    [MaxLength(OrderLine.SkuMaxLength)]
    public string? Sku { get; init; }

    /// <summary>EN: Item name. TR: Kalem adı.</summary>
    [Required]
    [MaxLength(OrderLine.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>EN: Quantity, greater than zero. TR: Miktar, sıfırdan büyük.</summary>
    [Required]
    [Range(0, 1_000_000_000, MinimumIsExclusive = true)]
    public decimal? Quantity { get; init; }

    /// <summary>EN: Unit price, zero or more. TR: Birim fiyat, sıfır veya fazlası.</summary>
    [Required]
    [Range(0, 1_000_000_000)]
    public decimal? UnitPrice { get; init; }
}

/// <summary>
/// EN: One line as the API returns it.
/// TR: API'nin döndürdüğü haliyle bir satır.
/// </summary>
/// <param name="LineNumber">EN: Position. TR: Sıra.</param>
/// <param name="Sku">EN: Stock keeping unit. TR: Stok kodu.</param>
/// <param name="Name">EN: Item name. TR: Kalem adı.</param>
/// <param name="Quantity">EN: Quantity. TR: Miktar.</param>
/// <param name="UnitPrice">EN: Unit price. TR: Birim fiyat.</param>
/// <param name="LineTotal">EN: Line total. TR: Satır tutarı.</param>
public sealed record OrderLineResponse(
    int LineNumber, string Sku, string Name, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>
/// EN: An order with its lines. The version travels in the ETag header.
/// TR: Satırlarıyla bir sipariş. Sürüm ETag başlığında taşınır.
/// </summary>
/// <param name="Id">EN: Order id. TR: Sipariş kimliği.</param>
/// <param name="Status">EN: Draft or Placed. TR: Draft veya Placed.</param>
/// <param name="Number">EN: Number once placed. TR: Verildikten sonra numara.</param>
/// <param name="CustomerId">EN: Customer. TR: Müşteri.</param>
/// <param name="CustomerName">EN: Customer name snapshot. TR: Müşteri adı kopyası.</param>
/// <param name="Total">EN: Order total. TR: Sipariş toplamı.</param>
/// <param name="PlacedAt">EN: When placed. TR: Ne zaman verildiği.</param>
/// <param name="CreatedAt">EN: Creation time. TR: Oluşturulma zamanı.</param>
/// <param name="UpdatedAt">EN: Last update time. TR: Son güncelleme zamanı.</param>
/// <param name="Lines">EN: The lines. TR: Satırlar.</param>
/// <param name="CancelledAt">EN: When cancelled (T-040). TR: Ne zaman iptal edildiği (T-040).</param>
/// <param name="CancelledBy">EN: Who cancelled it. TR: Kimin iptal ettiği.</param>
/// <param name="CancellationReason">EN: Why, if given. TR: Verildiyse nedeni.</param>
public sealed record OrderResponse(
    Guid Id,
    OrderStatus Status,
    int? Number,
    Guid? CustomerId,
    string? CustomerName,
    decimal Total,
    DateTimeOffset? PlacedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset? CancelledAt,
    Guid? CancelledBy,
    string? CancellationReason)
{
    /// <summary>
    /// EN: The one mapping from entity to API shape (ADR-021).
    /// TR: Entity'den API biçimine tek eşleme (ADR-021).
    /// </summary>
    public static readonly Expression<Func<Order, OrderResponse>> Projection = o =>
        new OrderResponse(
            o.Id, o.Status, o.Number, o.CustomerId, o.CustomerName, o.Total, o.PlacedAt, o.CreatedAt, o.UpdatedAt,
            o.Lines.OrderBy(l => l.LineNumber)
                .Select(l => new OrderLineResponse(l.LineNumber, l.Sku, l.Name, l.Quantity, l.UnitPrice, l.LineTotal))
                .ToList(),
            o.CancelledAt, o.CancelledBy, o.CancellationReason);

    /// <summary>EN: <see cref="Projection"/>, compiled once. TR: Bir kez derlenmiş <see cref="Projection"/>.</summary>
    private static readonly Func<Order, OrderResponse> _map = Projection.Compile();

    /// <summary>
    /// EN: Maps an entity already in memory.
    /// TR: Bellekteki bir entity'yi eşler.
    /// </summary>
    /// <param name="order">EN: The order. TR: Sipariş.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static OrderResponse From(Order order) => _map(order);
}

/// <summary>
/// EN: An order in a list: no lines, to keep pages light.
/// TR: Listedeki bir sipariş: sayfalar hafif kalsın diye satırsız.
/// </summary>
/// <param name="Id">EN: Order id. TR: Sipariş kimliği.</param>
/// <param name="Status">EN: Draft or Placed. TR: Draft veya Placed.</param>
/// <param name="Number">EN: Number once placed. TR: Verildikten sonra numara.</param>
/// <param name="CustomerName">EN: Customer name snapshot. TR: Müşteri adı kopyası.</param>
/// <param name="Total">EN: Order total. TR: Sipariş toplamı.</param>
/// <param name="CreatedAt">EN: Creation time. TR: Oluşturulma zamanı.</param>
public sealed record OrderSummaryResponse(
    Guid Id, OrderStatus Status, int? Number, string? CustomerName, decimal Total, DateTimeOffset CreatedAt)
{
    /// <summary>EN: List mapping. TR: Liste eşlemesi.</summary>
    public static readonly Expression<Func<Order, OrderSummaryResponse>> Projection = o =>
        new OrderSummaryResponse(o.Id, o.Status, o.Number, o.CustomerName, o.Total, o.CreatedAt);
}

/// <summary>
/// EN: Shared answers of the order endpoints.
/// TR: Sipariş uç noktalarının ortak cevapları.
/// </summary>
internal static class OrderProblems
{
    /// <summary>
    /// EN: 409 for any change to a placed order.
    /// TR: Verilmiş bir siparişteki her değişiklik için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult AlreadyPlaced() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The order has already been placed.",
            detail: "A placed order can't be changed, deleted or placed again.");

    /// <summary>
    /// EN: 409 for cancelling an order that is not placed: a draft (delete it instead) or an already cancelled order.
    /// TR: Verilmemiş bir siparişi iptal etmek için 409: bir taslak (onun yerine silin) veya zaten iptal edilmiş bir sipariş.
    /// </summary>
    /// <param name="status">EN: The order's status. TR: Siparişin durumu.</param>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult NotCancellable(OrderStatus status) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: status == OrderStatus.Draft ? "A draft can't be cancelled." : "The order is already cancelled.",
            detail: status == OrderStatus.Draft
                ? "Only a placed order is cancelled; delete a draft instead."
                : "A cancelled order is final.");
}

/// <summary>
/// EN: Resolves a draft's customer against the replica (T-039).
/// TR: Bir taslağın müşterisini kopyaya göre çözer (T-039).
/// </summary>
internal static class OrderCustomer
{
    /// <summary>
    /// EN: The name to keep for <paramref name="customerId"/>, or a 400 if the company has no such live customer (unknown,
    ///     deleted, another company's — or created a moment ago and not replicated yet: the client retries).
    /// TR: <paramref name="customerId"/> için saklanacak ad; firmanın böyle canlı bir müşterisi yoksa 400 (bilinmeyen, silinmiş, başka firmanın
    ///     — veya az önce oluşturulmuş ve henüz kopyalanmamış: istemci yeniden dener).
    /// </summary>
    /// <param name="customerId">EN: The customer, or null. TR: Müşteri veya null.</param>
    /// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The name (null without a customer) or the 400. TR: Ad (müşteri yoksa null) veya 400.</returns>
    public static async Task<(string? Name, ValidationProblem? Problem)> ResolveAsync(
        Guid? customerId, OrdersDbContext db, CancellationToken cancellationToken)
    {
        if (customerId is not { } id)
        {
            return (null, null);
        }

        var name = await db.CustomerReplicas
            .Where(c => c.Id == id && !c.Removed)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return name is not null
            ? (name, null)
            : (null, TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(OrderInput.CustomerId)] = ["No such customer in your company (or it was deleted)."],
            }));
    }
}

/// <summary>
/// EN: Form for cancelling a placed order (T-040).
/// TR: Verilmiş bir siparişi iptal etmek için form (T-040).
/// </summary>
public sealed record CancelOrderInput
{
    /// <summary>EN: Why it is cancelled (optional). TR: Neden iptal edildiği (isteğe bağlı).</summary>
    [MaxLength(Order.CancellationReasonMaxLength)]
    public string? Reason { get; init; }
}
