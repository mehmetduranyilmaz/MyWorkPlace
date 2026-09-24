namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: Names of the authorization policies shared by the gateway and the services (ADR-006).
/// TR: Gateway ve servislerin paylaştığı yetkilendirme politikalarının adları (ADR-006).
/// </summary>
public static class PolicyNames
{
    /// <summary>
    /// EN: Requires a signed-in user whose company is on the Pro plan.
    /// TR: Firması Pro planda olan, giriş yapmış bir kullanıcı ister.
    /// </summary>
    public const string ProPlan = "pro-plan";
}
