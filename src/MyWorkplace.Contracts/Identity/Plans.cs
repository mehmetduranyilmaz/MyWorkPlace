using MyWorkplace.Abstractions.Identity;

namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: This product's plans: the values of the <c>plan</c> claim and the policies requiring them (ADR-006, ADR-026).
///     The core only knows the <c>plan:&lt;name&gt;</c> convention; the names live here.
/// TR: Bu ürünün planları: <c>plan</c> claim'inin değerleri ve onları isteyen politikalar (ADR-006, ADR-026).
///     Çekirdek sadece <c>plan:&lt;ad&gt;</c> kuralını bilir; adlar burada durur.
/// </summary>
public static class Plans
{
    /// <summary>EN: Plan claim value for Basic companies. TR: Basic firmalar için plan claim değeri.</summary>
    public const string Basic = "basic";

    /// <summary>EN: Plan claim value for Pro companies. TR: Pro firmalar için plan claim değeri.</summary>
    public const string Pro = "pro";

    /// <summary>
    /// EN: Policy requiring a company on the Pro plan (<c>plan:pro</c>); the gateway route uses the same name.
    /// TR: Firmanın Pro planda olmasını isteyen politika (<c>plan:pro</c>); gateway rotası da aynı adı kullanır.
    /// </summary>
    public const string ProPolicy = PlanPolicy.Prefix + Pro;
}
