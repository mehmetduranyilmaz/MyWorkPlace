using System.Globalization;
using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Tests;

/// <summary>
/// EN: Unit and alternative-unit rules as plain domain logic — milliseconds, no database or HTTP (T-031, ADR-019).
/// TR: Saf domain mantığı olarak birim ve alternatif birim kuralları — milisaniyeler; veritabanı veya HTTP yok (T-031, ADR-019).
/// </summary>
public sealed class UnitRulesTests
{
    [Fact]
    public void SystemUnits_AreTheAgreedCatalog()
    {
        Assert.Equal(
            [("PCS", 0), ("KG", 3), ("L", 3), ("M", 3), ("BOX", 0), ("PACK", 0)],
            SystemUnits.All.Select(u => (u.Code, u.Precision)));
        Assert.All(SystemUnits.All, u => Assert.True(u.IsSystem));
    }

    [Theory]
    [InlineData("kg", "KG")]
    [InlineData(" Box ", "BOX")]
    [InlineData("DOZEN", null)]
    public void SystemUnits_Find_IgnoresCase(string code, string? expected)
    {
        Assert.Equal(expected, SystemUnits.Find(code)?.Code);
    }

    [Theory]
    [InlineData("PCS", "2", true)]
    [InlineData("PCS", "2.5", false)]
    [InlineData("KG", "2.5", true)]
    [InlineData("KG", "0.125", true)]
    [InlineData("KG", "0.1255", false)]
    public void Fits_ChecksTheUnitsPrecision(string code, string quantity, bool fits)
    {
        Assert.Equal(fits, SystemUnits.Find(code)!.Fits(decimal.Parse(quantity, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void UnitOfMeasure_Update_NormalizesTheCode()
    {
        var unit = new UnitOfMeasure();

        unit.Update(" dozen ", " Dozen ", 0);

        Assert.Equal(("DOZEN", "Dozen", 0), (unit.Code, unit.Name, unit.Precision));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void UnitOfMeasure_PrecisionOutside0To3_Throws(int precision)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitOfMeasure().Update("X", "X", precision));
    }

    [Fact]
    public void StockItem_Update_NormalizesUnitCodes()
    {
        var item = new StockItem();

        item.Update("W-1", "Water", "pcs", [new("box", 24m), new("Pack", 6m)]);

        Assert.Equal("PCS", item.BaseUnit);
        Assert.Equal([("BOX", 24m), ("PACK", 6m)], item.Units.Select(u => (u.UnitCode, u.Factor)));
    }

    [Theory]
    [InlineData("PCS", "BOX", "box")]
    [InlineData("pcs", "PCS", "PACK")]
    public void StockItem_Update_InvalidUnits_Throws_AndChangesNothing(string baseUnit, string first, string second)
    {
        var item = new StockItem();
        item.Update("W-1", "Before", "PCS", []);

        Assert.Throws<ArgumentException>(() => item.Update("W-2", "After", baseUnit, [new(first, 24m), new(second, 6m)]));
        Assert.Equal(("W-1", "Before"), (item.Sku, item.Name));
        Assert.Empty(item.Units);
    }

    [Theory]
    [InlineData(" 8690000000017 ", "8690000000017")]
    [InlineData("abc-1.2", "abc-1.2")]
    [InlineData("ABC-1.2", "ABC-1.2")]
    public void Barcode_IsTrimmed_ButKeepsItsCase(string entered, string stored)
    {
        // EN: Code128 is case-sensitive: "abc" and "ABC" are different labels. TR: Code128 harf duyarlıdır: "abc" ve "ABC" farklı etiketlerdir.
        Assert.Equal(stored, Barcode.Create(Guid.CreateVersion7(), entered, "PCS").Code);
    }

    [Theory]
    [InlineData("24", true)]
    [InlineData("0.000001", true)]
    [InlineData("1000000", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("0.0000001", false)]
    [InlineData("1000000.5", false)]
    public void IsValidFactor_Positive_Bounded_AtMostSixDecimals(string factor, bool valid)
    {
        Assert.Equal(valid, StockItem.IsValidFactor(decimal.Parse(factor, CultureInfo.InvariantCulture)));
    }
}
