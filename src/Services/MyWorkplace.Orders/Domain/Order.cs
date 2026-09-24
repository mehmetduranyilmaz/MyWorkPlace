using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Orders.Domain;

/// <summary>
/// EN: Where an order is in its life (ADR-024).
/// TR: Bir siparişin yaşamının neresinde olduğu (ADR-024).
/// </summary>
public enum OrderStatus
{
    /// <summary>EN: Being prepared: editable, deletable, no number. TR: Hazırlanıyor: düzenlenebilir, silinebilir, numarasız.</summary>
    Draft,

    /// <summary>EN: Placed: numbered and frozen. TR: Verildi: numaralı ve dondurulmuş.</summary>
    Placed,
}

/// <summary>
/// EN: A company's order (ADR-024). Lines are self-contained (SKU, name, quantity, price as entered) and the totals are
///     computed here, never taken from the client. Placing freezes the order.
/// TR: Bir firmanın siparişi (ADR-024). Satırlar kendi kendine yeterlidir (girildiği haliyle SKU, ad, miktar, fiyat) ve toplamlar
///     burada hesaplanır, asla istemciden alınmaz. Sipariş verilince donar.
/// </summary>
public sealed class Order : BusinessEntity
{
    /// <summary>EN: Max length of <see cref="CustomerName"/>. TR: <see cref="CustomerName"/> için en fazla uzunluk.</summary>
    public const int CustomerNameMaxLength = 200;

    /// <summary>EN: Most lines one order may have. TR: Bir siparişin en fazla satır sayısı.</summary>
    public const int MaxLines = 200;

    /// <summary>EN: The lines, in entry order. TR: Satırlar, giriş sırasıyla.</summary>
    private readonly List<OrderLine> _lines = [];

    /// <summary>EN: Life-cycle state. TR: Yaşam döngüsü durumu.</summary>
    [AuditChanges]
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;

    /// <summary>EN: Per-company number, given when placed. TR: Firmaya özel numara, verilince atanır.</summary>
    public int? Number { get; private set; }

    /// <summary>EN: When it was placed (UTC). TR: Ne zaman verildiği (UTC).</summary>
    public DateTimeOffset? PlacedAt { get; private set; }

    /// <summary>EN: The customer, if any (not validated yet, T-039). TR: Varsa müşteri (henüz doğrulanmaz, T-039).</summary>
    [AuditChanges]
    public Guid? CustomerId { get; private set; }

    /// <summary>
    /// EN: The customer's name at the time of ordering — kept even if the customer is renamed later.
    /// TR: Müşterinin sipariş anındaki adı — müşterinin adı sonradan değişse de korunur.
    /// </summary>
    [AuditChanges]
    public string? CustomerName { get; private set; }

    /// <summary>EN: Sum of the line totals. TR: Satır tutarlarının toplamı.</summary>
    [AuditChanges]
    public decimal Total { get; private set; }

    /// <summary>EN: The lines. TR: Satırlar.</summary>
    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>EN: Whether it can still be changed. TR: Hâlâ değiştirilebilir olup olmadığı.</summary>
    public bool IsDraft => Status == OrderStatus.Draft;

    /// <summary>
    /// EN: Replaces the customer and all lines of a draft (full update) and recomputes the total.
    /// TR: Bir taslağın müşterisini ve tüm satırlarını değiştirir (tam güncelleme) ve toplamı yeniden hesaplar.
    /// </summary>
    /// <param name="customerId">EN: Customer or null. TR: Müşteri veya null.</param>
    /// <param name="customerName">EN: Customer name snapshot or null. TR: Müşteri adı kopyası veya null.</param>
    /// <param name="lines">EN: The new lines (at least one). TR: Yeni satırlar (en az bir).</param>
    public void Update(Guid? customerId, string? customerName, IReadOnlyList<OrderLineValues> lines)
    {
        EnsureDraft();
        if (lines.Count is 0 or > MaxLines)
        {
            throw new ArgumentException($"An order has 1 to {MaxLines} lines.", nameof(lines));
        }

        CustomerId = customerId;
        CustomerName = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();

        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            _lines.Add(OrderLine.Create(i + 1, lines[i]));
        }

        Total = _lines.Sum(line => line.LineTotal);
    }

    /// <summary>
    /// EN: Places the draft with its number; from now on it can't change.
    /// TR: Taslağı numarasıyla verir; bundan sonra değişemez.
    /// </summary>
    /// <param name="number">EN: The next per-company number. TR: Firmaya özel sıradaki numara.</param>
    /// <param name="now">EN: Current time. TR: Şu an.</param>
    public void Place(int number, DateTimeOffset now)
    {
        EnsureDraft();
        Status = OrderStatus.Placed;
        Number = number;
        PlacedAt = now;
    }

    /// <summary>
    /// EN: Guards every change: a placed order is frozen.
    /// TR: Her değişikliği korur: verilmiş sipariş dondurulmuştur.
    /// </summary>
    private void EnsureDraft()
    {
        if (!IsDraft)
        {
            throw new InvalidOperationException("A placed order can't be changed.");
        }
    }
}

/// <summary>
/// EN: Values of a line as entered.
/// TR: Bir satırın girildiği haliyle değerleri.
/// </summary>
/// <param name="Sku">EN: Stock keeping unit. TR: Stok kodu.</param>
/// <param name="Name">EN: Item name. TR: Kalem adı.</param>
/// <param name="Quantity">EN: Quantity (&gt; 0). TR: Miktar (&gt; 0).</param>
/// <param name="UnitPrice">EN: Unit price (≥ 0). TR: Birim fiyat (≥ 0).</param>
public sealed record OrderLineValues(string Sku, string Name, decimal Quantity, decimal UnitPrice);

/// <summary>
/// EN: One line of an order, stored in <c>order_lines</c>. Its total is computed once, when the line is created.
/// TR: Bir siparişin bir satırı; <c>order_lines</c> içinde saklanır. Tutarı bir kez, satır oluşturulurken hesaplanır.
/// </summary>
public sealed class OrderLine
{
    /// <summary>EN: Max length of <see cref="Sku"/>. TR: <see cref="Sku"/> için en fazla uzunluk.</summary>
    public const int SkuMaxLength = 50;

    /// <summary>EN: Max length of <see cref="Name"/>. TR: <see cref="Name"/> için en fazla uzunluk.</summary>
    public const int NameMaxLength = 200;

    /// <summary>EN: Line id. TR: Satır kimliği.</summary>
    public Guid Id { get; private init; }

    /// <summary>EN: 1-based position in the order. TR: Siparişteki 1'den başlayan sırası.</summary>
    public int LineNumber { get; private init; }

    /// <summary>EN: Stock keeping unit as entered. TR: Girildiği haliyle stok kodu.</summary>
    public string Sku { get; private init; } = "";

    /// <summary>EN: Item name as entered. TR: Girildiği haliyle kalem adı.</summary>
    public string Name { get; private init; } = "";

    /// <summary>EN: Quantity. TR: Miktar.</summary>
    public decimal Quantity { get; private init; }

    /// <summary>EN: Unit price. TR: Birim fiyat.</summary>
    public decimal UnitPrice { get; private init; }

    /// <summary>EN: Quantity × unit price, rounded to cents. TR: Miktar × birim fiyat, kuruşa yuvarlanmış.</summary>
    public decimal LineTotal { get; private init; }

    /// <summary>
    /// EN: Creates a line and computes its total (rounded half away from zero, as on an invoice).
    /// TR: Bir satır oluşturur ve tutarını hesaplar (faturadaki gibi, yarım değer sıfırdan uzağa yuvarlanır).
    /// </summary>
    /// <param name="lineNumber">EN: Position. TR: Sıra.</param>
    /// <param name="values">EN: Values as entered. TR: Girildiği haliyle değerler.</param>
    /// <returns>EN: The line. TR: Satır.</returns>
    internal static OrderLine Create(int lineNumber, OrderLineValues values)
    {
        if (values.Quantity <= 0 || values.UnitPrice < 0)
        {
            throw new ArgumentException("Quantity must be positive and the price not negative.", nameof(values));
        }

        return new OrderLine
        {
            Id = Guid.CreateVersion7(),
            LineNumber = lineNumber,
            Sku = values.Sku.Trim(),
            Name = values.Name.Trim(),
            Quantity = values.Quantity,
            UnitPrice = values.UnitPrice,
            LineTotal = Math.Round(values.Quantity * values.UnitPrice, 2, MidpointRounding.AwayFromZero),
        };
    }
}

/// <summary>
/// EN: The last order number a company used. One row per company; placing an order increments it in the same
///     transaction, so numbers never repeat and a failed placement leaves no gap (ADR-024).
/// TR: Bir firmanın kullandığı son sipariş numarası. Firma başına bir satır; sipariş verme onu aynı transaction'da artırır; böylece
///     numaralar asla tekrar etmez ve başarısız bir sipariş verme boşluk bırakmaz (ADR-024).
/// </summary>
public sealed class OrderNumberSequence : TenantOwnedEntity
{
    /// <summary>EN: The first number a company gets. TR: Bir firmanın aldığı ilk numara.</summary>
    public const int FirstNumber = 1001;

    /// <summary>EN: Last number handed out. TR: Verilen son numara.</summary>
    public int LastNumber { get; private set; } = FirstNumber - 1;

    /// <summary>
    /// EN: Takes the next number.
    /// TR: Sıradaki numarayı alır.
    /// </summary>
    /// <returns>EN: The number. TR: Numara.</returns>
    public int Next() => ++LastNumber;
}
