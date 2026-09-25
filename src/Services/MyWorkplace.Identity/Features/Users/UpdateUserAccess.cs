using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.Abstractions.Identity;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Features.Users;

/// <summary>
/// EN: <c>PUT /identity/users/{id}/access</c> — replaces a user's roles and extra permissions (ADR-022), protected
///     with If-Match (ADR-017).
/// TR: <c>PUT /identity/users/{id}/access</c> — bir kullanıcının rollerini ve ek izinlerini değiştirir (ADR-022);
///     If-Match ile korunur (ADR-017).
/// </summary>
public static class UpdateUserAccess
{
    /// <summary>
    /// EN: Maps the endpoint on the /identity/users group.
    /// TR: Uç noktayı /identity/users grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity/users group. TR: /identity/users grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapUpdateUserAccess(this IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}/access", HandleAsync)
            .WithName("UpdateUserAccess")
            .RequireAuthorization(Permissions.UsersManage)
            .WithSummary("EN: Change a user's access | TR: Kullanıcının yetkilerini değiştir")
            .WithDescription(
                "EN: Replaces the user's roles and extra permissions. Requires If-Match (428 without it, 412 if stale). " +
                "Only an Owner can change an Owner or grant the Owner role, and nobody can grant a permission they " +
                "don't have (403). The last Owner can't be demoted (409). The user's current token keeps its old " +
                "permissions until it expires (at most 15 minutes). " +
                "TR: Kullanıcının rollerini ve ek izinlerini değiştirir. If-Match gerekir (yoksa 428, eskiyse 412). " +
                "Bir Sahip'i değiştirmeyi veya Owner rolünü vermeyi sadece bir Sahip yapabilir; kimse kendinde olmayan " +
                "bir izni veremez (403). Son Sahip'in rolü düşürülemez (409). Kullanıcının mevcut token'ı süresi dolana " +
                "kadar (en fazla 15 dakika) eski izinleri taşır.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request inside the company lock: precondition, lookup, version, anti-escalation, last Owner, save.
    /// TR: İsteği firma kilidi içinde işler: ön koşul, arama, sürüm, yetki yükseltme, son Sahip, kaydetme.
    /// </summary>
    /// <param name="id">EN: User id. TR: Kullanıcı kimliği.</param>
    /// <param name="input">EN: New roles and extra permissions. TR: Yeni roller ve ek izinler.</param>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="currentUser">EN: The signed-in user. TR: Giriş yapmış kullanıcı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 403, 404, 409, 412 or 428. TR: 200, 403, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<UserResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        AccessInput input,
        IdentityDbContext db,
        ICurrentUser currentUser,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        var roles = input.Roles!;
        var extraPermissions = input.ExtraPermissions ?? [];

        return await UserAdministration.InCompanyLockAsync<Results<Ok<UserResponse>, NotFound, ProblemHttpResult>>(
            db,
            currentUser,
            async ct =>
            {
                var user = await db.Users.FindForUpdateAsync(id, ct);
                if (user is null)
                {
                    return TypedResults.NotFound();
                }

                if (db.GetVersion(user) != expectedVersion)
                {
                    return ETags.PreconditionFailed();
                }

                var caller = await UserAdministration.FindCallerAsync(db, currentUser, ct);
                if (caller is null || !caller.MayAssignAccess(user, roles, extraPermissions))
                {
                    return UserProblems.NotAllowed();
                }

                if (user.IsOwner
                    && !roles.Contains(DefaultRoles.Owner, StringComparer.Ordinal)
                    && !await UserAdministration.HasAnotherOwnerAsync(db, user.Id, ct))
                {
                    return UserProblems.LastOwner();
                }

                db.ExpectVersion(user, expectedVersion);
                user.AssignRoles(roles);
                user.GrantExtraPermissions(extraPermissions);

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ETags.PreconditionFailed();
                }

                http.Response.SetETag(db.GetVersion(user));
                return TypedResults.Ok(UserResponse.From(user));
            },
            cancellationToken);
    }
}
