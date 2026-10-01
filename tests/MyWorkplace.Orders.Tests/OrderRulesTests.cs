using System.Globalization;
using MyWorkplace.Orders.Domain;

namespace MyWorkplace.Orders.Tests;

/// <summary>
/// EN: Order rules as plain domain logic — milliseconds, no database or HTTP (T-046, ADR-029): line totals and their
///     rounding, the total, line limits, input cleaning, and the Draft → Placed → Cancelled life cycle.
/// TR: Saf domain mantığı olarak sipariş kuralları — milisaniyeler; veritabanı veya HTTP yok (T-046, ADR-029): satır tutarları ve
///     yuvarlamaları, toplam, satır sınırları, girdi temizleme ve Draft → Placed → Cancelled yaşam döngüsü.
/// </summary>
public sealed class OrderRulesTests
{
    /// <summary>EN: A fixed moment. TR: Sabit bir an.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2.5", "10.25", "25.63")]
    [InlineData("1", "0.005", "0.01")]
    [InlineData("3", "0.333", "1.00")]
    [InlineData("1", "0", "0")]
    public void LineTotal_IsRoundedToCents_HalfAwayFromZero(string quantity, string unitPrice, string expected)
    {
        // EN: As on an invoice: 25.625 → 25.63 (banker's rounding would give 25.62).
        // TR: Faturadaki gibi: 25,625 → 25,63 (banker yuvarlaması 25,62 verirdi).
        var order = DraftWith(Line(Dec(quantity), Dec(unitPrice)));

        Assert.Equal(Dec(expected), Assert.Single(order.Lines).LineTotal);
    }

    [Fact]
    public void Total_IsTheSumOfTheRoundedLines()
    {
        // EN: Each line is 0.125 → 0.13, so the total is 0.26; rounding only the sum would give 0.25.
        // TR: Her satır 0,125 → 0,13; bu yüzden toplam 0,26'dır; sadece toplamı yuvarlamak 0,25 verirdi.
        var order = DraftWith(Line(0.5m, 0.25m), Line(0.5m, 0.25m));

        Assert.Equal(0.26m, order.Total);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(Order.MaxLines, true)]
    [InlineData(Order.MaxLines + 1, false)]
    public void LineCount_IsOneToTheMaximum(int count, bool allowed)
    {
        var order = new Order();
        var lines = Enumerable.Range(0, count).Select(_ => Line(1m, 1m)).ToList();

        var error = Record.Exception(() => order.Update(null, null, lines));

        Assert.Equal(allowed, error is null);
        Assert.Equal(allowed ? count : 0, order.Lines.Count);
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("-1", "1")]
    [InlineData("1", "-0.01")]
    public void NonPositiveQuantityOrNegativePrice_IsRefused(string quantity, string unitPrice)
    {
        var order = new Order();

        Assert.Throws<ArgumentException>(() => order.Update(null, null, [Line(Dec(quantity), Dec(unitPrice))]));
    }

    [Fact]
    public void Update_TrimsTheText_BlankBecomesNull_AndNumbersTheLines()
    {
        var customerId = Guid.CreateVersion7();
        var order = new Order();

        order.Update(customerId, "  Acme Ltd ", [new(" BOLT-1 ", " Bolt ", 1m, 1m), new("NUT", "Nut", 1m, 1m)]);
        var blank = DraftWith(Line(1m, 1m));
        blank.Update(null, "   ", [Line(1m, 1m)]);

        Assert.Equal((customerId, "Acme Ltd"), (order.CustomerId, order.CustomerName));
        Assert.Equal([(1, "BOLT-1", "Bolt"), (2, "NUT", "Nut")], order.Lines.Select(l => (l.LineNumber, l.Sku, l.Name)));
        Assert.Null(blank.CustomerName);
    }

    [Fact]
    public void Update_ReplacesAllLines_AndRecomputesTheTotal()
    {
        var order = DraftWith(Line(1m, 5m), Line(1m, 5m));

        order.Update(null, null, [Line(2m, 1.5m)]);

        Assert.Equal((1, 3m), (order.Lines.Count, order.Total));
    }

    [Fact]
    public void Place_GivesTheNumberAndTime_AndFreezesTheOrder()
    {
        var order = DraftWith(Line(1m, 1m));

        order.Place(1001, _now);

        Assert.Equal((OrderStatus.Placed, (int?)1001, (DateTimeOffset?)_now), (order.Status, order.Number, order.PlacedAt));
        Assert.Throws<InvalidOperationException>(() => order.Update(null, null, [Line(1m, 2m)]));
        Assert.Throws<InvalidOperationException>(() => order.Place(1002, _now));
        Assert.Equal((1m, (int?)1001), (order.Total, order.Number));
    }

    [Fact]
    public void Cancel_APlacedOrder_RecordsWhenWhoAndWhy_AndKeepsTheLines()
    {
        var order = DraftWith(Line(2m, 3m));
        order.Place(1001, _now);
        var user = Guid.CreateVersion7();

        order.Cancel("  Customer changed their mind  ", user, _now.AddHours(1));

        Assert.Equal(
            (OrderStatus.Cancelled, (DateTimeOffset?)_now.AddHours(1), (Guid?)user, "Customer changed their mind"),
            (order.Status, order.CancelledAt, order.CancelledBy, order.CancellationReason));
        Assert.Equal((1, 6m, (int?)1001), (order.Lines.Count, order.Total, order.Number));
        Assert.False(order.IsPlaced);
    }

    [Fact]
    public void Cancel_BlankReason_IsStoredAsNull()
    {
        var order = DraftWith(Line(1m, 1m));
        order.Place(1001, _now);

        order.Cancel("   ", userId: null, _now);

        Assert.Null(order.CancellationReason);
    }

    [Fact]
    public void Cancel_OnlyAPlacedOrder_AndOnlyOnce()
    {
        var draft = DraftWith(Line(1m, 1m));
        var cancelled = DraftWith(Line(1m, 1m));
        cancelled.Place(1001, _now);
        cancelled.Cancel(null, null, _now);

        Assert.Throws<InvalidOperationException>(() => draft.Cancel(null, null, _now));
        Assert.Throws<InvalidOperationException>(() => cancelled.Cancel(null, null, _now));
        Assert.Throws<InvalidOperationException>(() => cancelled.Place(1002, _now));
        Assert.Throws<InvalidOperationException>(() => cancelled.Update(null, null, [Line(1m, 1m)]));
        Assert.Equal(OrderStatus.Draft, draft.Status);
    }

    [Fact]
    public void NumberSequence_StartsAt1001_AndNeverRepeats()
    {
        var sequence = new OrderNumberSequence();

        var numbers = Enumerable.Range(0, 3).Select(_ => sequence.Next()).ToList();

        Assert.Equal([1001, 1002, 1003], numbers);
        Assert.Equal(1003, sequence.LastNumber);
    }

    /// <summary>
    /// EN: A draft with the given lines.
    /// TR: Verilen satırlarla bir taslak.
    /// </summary>
    /// <param name="lines">EN: The lines. TR: Satırlar.</param>
    /// <returns>EN: The draft. TR: Taslak.</returns>
    private static Order DraftWith(params OrderLineValues[] lines)
    {
        var order = new Order();
        order.Update(null, null, lines);
        return order;
    }

    /// <summary>
    /// EN: A line with a fixed SKU and name.
    /// TR: Sabit SKU ve adlı bir satır.
    /// </summary>
    /// <param name="quantity">EN: Quantity. TR: Miktar.</param>
    /// <param name="unitPrice">EN: Unit price. TR: Birim fiyat.</param>
    /// <returns>EN: The line values. TR: Satır değerleri.</returns>
    private static OrderLineValues Line(decimal quantity, decimal unitPrice) => new("SKU-1", "Item", quantity, unitPrice);

    /// <summary>
    /// EN: Parses a decimal written with a dot.
    /// TR: Noktayla yazılmış bir ondalığı ayrıştırır.
    /// </summary>
    /// <param name="value">EN: Text. TR: Metin.</param>
    /// <returns>EN: The number. TR: Sayı.</returns>
    private static decimal Dec(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
