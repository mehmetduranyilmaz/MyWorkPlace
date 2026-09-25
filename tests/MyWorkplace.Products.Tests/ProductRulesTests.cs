using MyWorkplace.Products.Domain;

namespace MyWorkplace.Products.Tests;

/// <summary>
/// EN: Product rules as plain domain logic — milliseconds, no database or HTTP (T-013, test pyramid T-046).
/// TR: Saf domain mantığı olarak ürün kuralları — milisaniyeler; veritabanı veya HTTP yok (T-013, test piramidi T-046).
/// </summary>
public sealed class ProductRulesTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("9.99", true)]
    [InlineData("10.50", true)]
    [InlineData("-0.01", false)]
    [InlineData("1.005", false)]
    public void IsValidPrice_NotNegative_AtMostTwoDecimals(string price, bool valid)
    {
        Assert.Equal(valid, Product.IsValidPrice(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Update_TrimsAndNormalizes()
    {
        var product = new Product();

        product.Update("  bolt-m8 ", "  Bolt  ", 1.25m, "   ");

        Assert.Equal(("bolt-m8", "BOLT-M8", "Bolt", 1.25m, (string?)null),
            (product.Sku, product.NormalizedSku, product.Name, product.Price, product.Description));
    }

    [Fact]
    public void Update_InvalidPrice_Throws_AndChangesNothing()
    {
        var product = new Product();
        product.Update("A-1", "Before", 1m, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => product.Update("B-2", "After", 1.005m, null));
        Assert.Equal(("A-1", "Before"), (product.Sku, product.Name));
    }
}
