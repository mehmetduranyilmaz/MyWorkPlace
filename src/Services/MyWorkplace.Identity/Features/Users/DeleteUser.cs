using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Features.Users;

/// <summary>
/// EN: <c>DELETE /identity/users/{id}</c> — removes a user (soft delete): they can no longer sign in, but the audit
///     trail keeps pointing to them.
/// TR: <c>DELETE /identity/users/{id}</c> — bir kullanıcıyı kaldırır (soft delete): artık giriş yapamaz, ama denetim
///     kayıtları onu göstermeye devam eder.
/// </summary>
public static class DeleteUser
{
    /// <summary>
    /// EN: Maps the endpoint on the /identity/users group.
    /// TR: Uç noktayı /identity/users grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity/users group. TR: /identity/users grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapDeleteUser(this IEndpointRouteBuilder group) =>
        group.MapDelete("/{id:guid}", HandleAsync)
            .WithName("DeleteUser")
            .RequireAuthorization(Permissions.UsersManage)
            .WithSummary("EN: Remove a user | TR: Kullanıcıyı kaldır")
            .WithDescription(
                "EN: Removes the user: they can no longer sign in and their email can be used again. Only an Owner can " +
                "remove an Owner (403); the last Owner can't be removed (409). A token already issued stays valid until " +
                "it expires (at most 15 minutes). If-Match is not required (ADR-017). " +
                "TR: Kullanıcıyı kaldırır: artık giriş yapamaz ve e-postası tekrar kullanılabilir. Bir Sahip'i sadece bir " +
                "Sahip kaldırabilir (403); son Sahip kaldırılamaz (409). Önceden verilmiş bir token süresi dolana kadar " +
                "(en fazla 15 dakika) geçerli kalır. If-Match gerekmez (ADR-017).");

    /// <summary>
    /// EN: Handles the request inside the company lock; the soft-delete interceptor turns the removal into an update.
    /// TR: İsteği firma kilidi içinde işler; soft-delete interceptor'ı silmeyi güncellemeye çevirir.
    /// </summary>
    /// <param name="id">EN: User id. TR: Kullanıcı kimliği.</param>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="currentUser">EN: The signed-in user. TR: Giriş yapmış kullanıcı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204, 403, 404 or 409. TR: 204, 403, 404 veya 409.</returns>
    public static Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        IdentityDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        UserAdministration.InCompanyLockAsync<Results<NoContent, NotFound, ProblemHttpResult>>(
            db,
            currentUser,
            async ct =>
            {
                var user = await db.Users.FindForUpdateAsync(id, ct);
                if (user is null)
                {
                    return TypedResults.NotFound();
                }

                var caller = await UserAdministration.FindCallerAsync(db, currentUser, ct);
                if (caller is null || !caller.MayRemove(user))
                {
                    return UserProblems.NotAllowed();
                }

                if (user.IsOwner && !await UserAdministration.HasAnotherOwnerAsync(db, user.Id, ct))
                {
                    return UserProblems.LastOwner();
                }

                db.Users.Remove(user);
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            },
            cancellationToken);
}
