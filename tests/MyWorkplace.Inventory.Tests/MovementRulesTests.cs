using System.Globalization;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Tests;

/// <summary>
/// EN: Movement rules as plain domain logic (T-030, ADR-019, ADR-020): unit conversion with both precisions, an item's
///     factors, and the negative-stock review flag.
/// TR: Saf domain mantığı olarak hareket kuralları (T-030, ADR-019, ADR-020): iki hassasiyetle birim dönüşümü, bir kalemin katsayıları
///     ve eksi stok inceleme işareti.
/// </summary>
public sealed class MovementRulesTests
{
    /// <summary>EN: A made-up unit that must be whole (precision 0). TR: Tam sayı olması gereken uydurma bir birim (hassasiyet 0).</summary>
    private static readonly UnitDefinition _bag = new("BAG", "Bag", 0, IsSystem: false);

    [Theory]
    [InlineData("BOX", "2", "24", "48")]
    [InlineData("PCS", "7", "1", "7")]
    [InlineData("KG", "0.125", "1", "0.125")]
    public void ToBase_MultipliesByTheFactor(string unit, string quantity, string factor, string expected)
    {
        var (baseQuantity, problem) = UnitConversion.ToBase(
            Dec(quantity), SystemUnits.Find(unit)!, Dec(factor), SystemUnits.Find(unit == "KG" ? "KG" : "PCS")!);

        Assert.Equal((Dec(expected), QuantityProblem.None), (baseQuantity, problem));
    }

    [Fact]
    public void ToBase_TooManyDecimalsForTheEnteredUnit_IsRefused()
    {
        var (_, problem) = UnitConversion.ToBase(2.5m, SystemUnits.Find("BOX")!, 24m, SystemUnits.Find("PCS")!);

        Assert.Equal(QuantityProblem.EnteredUnitPrecision, problem);
    }

    [Theory]
    [InlineData("1", QuantityProblem.BaseUnitPrecision)]
    [InlineData("2", QuantityProblem.None)]
    public void ToBase_TooManyDecimalsOnceConverted_IsRefused(string bags, QuantityProblem expected)
    {
        // EN: 1 BAG = 2.5 PCS: one bag would be 2.5 pieces, two bags are 5.
        // TR: 1 BAG = 2,5 PCS: bir torba 2,5 adet olurdu, iki torba 5'tir.
        var (_, problem) = UnitConversion.ToBase(Dec(bags), _bag, 2.5m, SystemUnits.Find("PCS")!);

        Assert.Equal(expected, problem);
    }

    [Fact]
    public void FactorOf_KnowsTheBaseAndAlternativeUnits_AnyCase()
    {
        var item = new StockItem();
        item.Update("W-1", "Water", "PCS", [new("BOX", 24m)]);

        Assert.Equal((1m, 24m, (decimal?)null), (item.FactorOf("pcs"), item.FactorOf("box"), item.FactorOf("KG")));
    }

    [Theory]
    [InlineData(StockMovementType.Out, "-1", true)]
    [InlineData(StockMovementType.Out, "0", false)]
    [InlineData(StockMovementType.In, "-3", false)]
    public void IsNegativeIssue_OnlyAnIssueEndingBelowZero(StockMovementType type, string balanceAfter, bool flagged)
    {
        Assert.Equal(flagged, StockLedger.IsNegativeIssue(type, Dec(balanceAfter)));
    }

    /// <summary>
    /// EN: Parses a decimal written with a dot.
    /// TR: Noktayla yazılmış bir ondalığı ayrıştırır.
    /// </summary>
    /// <param name="value">EN: Text. TR: Metin.</param>
    /// <returns>EN: The number. TR: Sayı.</returns>
    private static decimal Dec(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
