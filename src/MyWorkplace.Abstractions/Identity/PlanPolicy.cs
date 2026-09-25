namespace MyWorkplace.Abstractions.Identity;

/// <summary>
/// EN: Naming convention of plan policies (ADR-006, ADR-026): <c>plan:&lt;name&gt;</c> means "a signed-in user whose token
///     has <c>plan = &lt;name&gt;</c>". The core knows the concept of a plan, not the plan names — those belong to the product.
/// TR: Plan politikalarının adlandırma kuralı (ADR-006, ADR-026): <c>plan:&lt;ad&gt;</c>, "token'ında <c>plan = &lt;ad&gt;</c> olan
///     giriş yapmış kullanıcı" demektir. Çekirdek plan kavramını bilir, plan adlarını bilmez — onlar ürüne aittir.
/// </summary>
public static class PlanPolicy
{
    /// <summary>EN: Prefix of every plan policy name. TR: Her plan politikası adının öneki.</summary>
    public const string Prefix = "plan:";

    /// <summary>
    /// EN: The policy name that requires the given plan, e.g. <c>For("gold")</c> is <c>plan:gold</c>.
    /// TR: Verilen planı isteyen politika adı; örneğin <c>For("gold")</c>, <c>plan:gold</c> olur.
    /// </summary>
    /// <param name="plan">EN: The plan claim value. TR: Plan claim değeri.</param>
    /// <returns>EN: The policy name. TR: Politika adı.</returns>
    public static string For(string plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        return Prefix + plan;
    }
}
