namespace MyWorkplace.Identity.Domain;

/// <summary>
/// EN: Subscription plan of a company; decides which modules it can use.
/// TR: Bir firmanın abonelik planı; hangi modülleri kullanabileceğini belirler.
/// </summary>
public enum Plan
{
    /// <summary>EN: Customers, products, orders. TR: Müşteriler, ürünler, siparişler.</summary>
    Basic = 0,

    /// <summary>EN: Basic plus inventory and reporting. TR: Basic'e ek olarak stok ve raporlama.</summary>
    Pro = 1,
}
