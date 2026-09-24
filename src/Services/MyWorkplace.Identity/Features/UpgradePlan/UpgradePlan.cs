using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Persistence;
using MyWorkplace.Identity.Tokens;

namespace MyWorkplace.Identity.Features.UpgradePlan;

/// <summary>
/// EN: Result of an upgrade: the new plan and a fresh token that already carries it, so Pro modules work immediately.
/// TR: Yükseltmenin sonucu: yeni plan ve onu zaten taşıyan taze bir token; böylece Pro modüller hemen çalışır.
/// </summary>
/// <param name="Plan">EN: The plan now ("pro"). TR: Şu anki plan ("pro").</param>
/// <param name="AccessToken">EN: New access token. TR: Yeni erişim token'ı.</param>
/// <param name="ExpiresIn">EN: Seconds until it expires. TR: Süresinin dolmasına kalan saniye.</param>
public sealed record UpgradePlanResponse(string Plan, string AccessToken, int ExpiresIn);

/// <summary>
/// EN: <c>POST /identity/tenant/upgrade</c> — moves the caller's company to Pro. Idempotent.
/// TR: <c>POST /identity/tenant/upgrade</c> — çağıranın firmasını Pro'ya taşır. İdempotent.
/// </summary>
public static class UpgradePlan
{
    /// <summary>
    /// EN: Maps the endpoint on the /identity group; requires the plan.manage permission.
    /// TR: Uç noktayı /identity grubunda tanımlar; plan.manage izni ister.
    /// </summary>
    /// <param name="group">EN: The /identity group. TR: /identity grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapUpgradePlan(this IEndpointRouteBuilder group) =>
        group.MapPost("/tenant/upgrade", HandleAsync)
            .WithName("UpgradePlan")
            // EN: Only Owners may change the plan (ADR-022). TR: Planı sadece Sahip'ler değiştirebilir (ADR-022).
            .RequireAuthorization(Permissions.PlanManage)
            .WithSummary("EN: Upgrade to Pro | TR: Pro'ya yükselt")
            .WithDescription(
                "EN: Moves your company to the Pro plan and returns a new token that carries it; your previous token " +
                "keeps the old plan until it expires. Calling it again is safe. " +
                "TR: Firmanızı Pro plana taşır ve bu planı taşıyan yeni bir token döner; önceki token'ınız süresi dolana " +
                "kadar eski planı taşır. Tekrar çağırmak güvenlidir.");

    /// <summary>
    /// EN: Handles the upgrade. The company comes from the validated token — never from the request — so a caller
    ///     can only ever upgrade their own company.
    /// TR: Yükseltmeyi işler. Firma isteğin kendisinden değil, doğrulanmış token'dan gelir; böylece çağıran sadece
    ///     kendi firmasını yükseltebilir.
    /// </summary>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="currentUser">EN: The signed-in user. TR: Giriş yapmış kullanıcı.</param>
    /// <param name="tokens">EN: Token issuer. TR: Token üreticisi.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 with a Pro token, or 401. TR: Pro token ile 200 veya 401.</returns>
    public static async Task<Results<Ok<UpgradePlanResponse>, UnauthorizedHttpResult>> HandleAsync(
        IdentityDbContext db,
        ICurrentUser currentUser,
        TokenIssuer tokens,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.TenantId is not { } tenantId)
        {
            return TypedResults.Unauthorized();
        }

        // EN: The tenant filter applies: the user is looked up inside their own company only.
        // TR: Firma filtresi uygulanır: kullanıcı sadece kendi firmasında aranır.
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !await EnsureProAsync(db, tenantId, cancellationToken))
        {
            return TypedResults.Unauthorized();
        }

        var token = tokens.Issue(user, Plan.Pro);
        return TypedResults.Ok(new UpgradePlanResponse(TokenClaims.ProPlan, token.AccessToken, token.ExpiresIn));
    }

    /// <summary>
    /// EN: Sets the company to Pro unless it already is. If another request upgraded it at the same moment, the
    ///     concurrency conflict means "already Pro", not an error.
    /// TR: Firma zaten Pro değilse Pro yapar. Başka bir istek aynı anda yükselttiyse, eşzamanlılık çakışması hata değil
    ///     "zaten Pro" demektir.
    /// </summary>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="tenantId">EN: The caller's company. TR: Çağıranın firması.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: False if the company does not exist. TR: Firma yoksa false.</returns>
    private static async Task<bool> EnsureProAsync(IdentityDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FindForUpdateAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return false;
        }

        if (tenant.Plan == Plan.Pro)
        {
            return true;
        }

        tenant.Plan = Plan.Pro;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // EN: Another upgrade won the race; the only possible change to a tenant here is the same upgrade.
            // TR: Yarışı başka bir yükseltme kazandı; burada bir firmada olabilecek tek değişiklik aynı yükseltmedir.
            db.ChangeTracker.Clear();
        }

        return true;
    }
}
