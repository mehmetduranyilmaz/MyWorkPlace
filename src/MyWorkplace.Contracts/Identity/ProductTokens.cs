namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: This product's token values: its plan names and the token issuer and audience. The claim <i>names</i> are generic
///     and live in <c>MyWorkplace.Abstractions</c>. T-054 turns these into configuration (ADR-026).
/// TR: Bu ürünün token değerleri: plan adları ile token'ın issuer ve audience'ı. Claim <i>adları</i> geneldir ve
///     <c>MyWorkplace.Abstractions</c> içinde durur. T-054 bunları yapılandırmaya çevirir (ADR-026).
/// </summary>
public static class ProductTokens
{
    /// <summary>EN: Plan claim value for Basic companies. TR: Basic firmalar için plan claim değeri.</summary>
    public const string BasicPlan = "basic";

    /// <summary>EN: Plan claim value for Pro companies. TR: Pro firmalar için plan claim değeri.</summary>
    public const string ProPlan = "pro";

    /// <summary>EN: Token issuer (<c>iss</c>). TR: Token'ı üreten (<c>iss</c>).</summary>
    public const string Issuer = "myworkplace-identity";

    /// <summary>EN: Token audience (<c>aud</c>). TR: Token'ın hedef kitlesi (<c>aud</c>).</summary>
    public const string Audience = "myworkplace-api";
}
