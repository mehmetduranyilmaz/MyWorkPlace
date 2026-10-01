using MyWorkplace.Customers.Domain;

namespace MyWorkplace.Customers.Tests;

/// <summary>
/// EN: Customer rules as plain domain logic — milliseconds, no database or HTTP (T-046, ADR-029): input cleaning and the
///     one email normalization used both for saving and for the uniqueness check.
/// TR: Saf domain mantığı olarak müşteri kuralları — milisaniyeler; veritabanı veya HTTP yok (T-046, ADR-029): girdi temizleme ve hem
///     kaydetme hem benzersizlik kontrolü için kullanılan tek e-posta normalizasyonu.
/// </summary>
public sealed class CustomerRulesTests
{
    [Fact]
    public void Update_TrimsEveryField()
    {
        var customer = new Customer();

        customer.Update("  Acme Ltd ", " Info@Acme.Example ", " +90 212 000 00 00 ", " 1234567890 ", "  Pays on time  ");

        Assert.Equal(
            ("Acme Ltd", "Info@Acme.Example", "info@acme.example", "+90 212 000 00 00", "1234567890", "Pays on time"),
            (customer.Name, customer.Email, customer.NormalizedEmail, customer.Phone, customer.TaxNumber, customer.Notes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_BlankOptionalFields_BecomeNull(string? blank)
    {
        var customer = new Customer();
        customer.Update("Before", "a@example.com", "1", "2", "3");

        customer.Update("Acme", blank, blank, blank, blank);

        Assert.Equal(
            ((string?)null, (string?)null, (string?)null, (string?)null, (string?)null),
            (customer.Email, customer.NormalizedEmail, customer.Phone, customer.TaxNumber, customer.Notes));
    }

    [Theory]
    [InlineData(" Info@Acme.Example ", "info@acme.example")]
    [InlineData("INFO@ACME.EXAMPLE", "info@acme.example")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeEmail_TrimsAndLowersCase_BlankIsNull(string? email, string? expected)
    {
        Assert.Equal(expected, Customer.NormalizeEmail(email));
    }

    [Fact]
    public void SavedEmail_MatchesTheUniquenessCheck()
    {
        // EN: The duplicate check compares NormalizeEmail(input) with the stored NormalizedEmail: both must be one rule.
        // TR: Tekrar kontrolü NormalizeEmail(girdi) ile saklanan NormalizedEmail'i karşılaştırır: ikisi tek bir kural olmalıdır.
        var customer = new Customer();

        customer.Update("Acme", " Sales@Acme.Example", null, null, null);

        Assert.Equal(Customer.NormalizeEmail("sales@ACME.example "), customer.NormalizedEmail);
    }
}
