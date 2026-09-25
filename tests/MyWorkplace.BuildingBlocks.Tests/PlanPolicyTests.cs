using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWorkplace.Abstractions.Identity;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Plan policies by convention (ADR-006, ADR-026): <c>plan:&lt;name&gt;</c> needs no registration and works without a
///     permission catalog (the gateway). Uses made-up plan names — the core knows no product's plans.
/// TR: Kurala dayalı plan politikaları (ADR-006, ADR-026): <c>plan:&lt;ad&gt;</c> kayıt gerektirmez ve izin kataloğu olmadan da
///     (gateway) çalışır. Uydurma plan adları kullanır — çekirdek hiçbir ürünün planlarını bilmez.
/// </summary>
public sealed class PlanPolicyTests : IAsyncDisposable
{
    /// <summary>EN: Services with convention policies and no catalog. TR: Kurala dayalı politikalar olan, kataloğu olmayan servisler.</summary>
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddAuthorization()
        .AddConventionPolicies()
        .BuildServiceProvider();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task UserOnThePlan_IsAllowed()
    {
        var result = await AuthorizeAsync(User(plan: "gold"), PlanPolicy.For("gold"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UserOnAnotherPlan_IsRefused()
    {
        var result = await AuthorizeAsync(User(plan: "silver"), PlanPolicy.For("gold"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AnonymousCaller_IsRefused()
    {
        var result = await AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), PlanPolicy.For("gold"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UnknownPlanName_RefusesEveryone_FailsClosed()
    {
        // EN: "plan:glod" is a typo; no token carries that plan, so nobody gets in.
        // TR: "plan:glod" bir yazım hatası; hiçbir token bu planı taşımaz, bu yüzden kimse giremez.
        var result = await AuthorizeAsync(User(plan: "gold"), "plan:glod");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PrefixWithoutAName_IsNoPolicy()
    {
        var policies = _services.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.Null(await policies.GetPolicyAsync(PlanPolicy.Prefix));
    }

    [Fact]
    public void PolicyName_FollowsTheConvention()
    {
        Assert.Equal("plan:gold", PlanPolicy.For("gold"));
        Assert.Throws<ArgumentException>(() => PlanPolicy.For(" "));
    }

    /// <summary>
    /// EN: Evaluates the named policy for the user.
    /// TR: Adı verilen politikayı kullanıcı için değerlendirir.
    /// </summary>
    /// <param name="user">EN: The user. TR: Kullanıcı.</param>
    /// <param name="policyName">EN: Policy name. TR: Politika adı.</param>
    /// <returns>EN: The authorization result. TR: Yetkilendirme sonucu.</returns>
    private Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string policyName) =>
        _services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, resource: null, policyName);

    /// <summary>
    /// EN: A signed-in user of a company on the given plan.
    /// TR: Verilen plandaki bir firmanın giriş yapmış kullanıcısı.
    /// </summary>
    /// <param name="plan">EN: Plan claim value. TR: Plan claim değeri.</param>
    /// <returns>EN: The user. TR: Kullanıcı.</returns>
    private static ClaimsPrincipal User(string plan) =>
        new(new ClaimsIdentity([new Claim(TokenClaims.Plan, plan)], authenticationType: "Test"));
}
