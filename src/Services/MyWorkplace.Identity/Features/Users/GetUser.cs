using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Features.Users;

/// <summary>
/// EN: <c>GET /identity/users/{id}</c> — reads one user of the caller's company.
/// TR: <c>GET /identity/users/{id}</c> — çağıranın firmasının bir kullanıcısını okur.
/// </summary>
public static class GetUser
{
    /// <summary>
    /// EN: Maps the endpoint on the /identity/users group.
    /// TR: Uç noktayı /identity/users grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity/users group. TR: /identity/users grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapGetUser(this IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetUser")
            .RequireAuthorization(Permissions.UsersManage)
            .WithSummary("EN: Get a user | TR: Kullanıcıyı getir")
            .WithDescription(
                "EN: Returns the user and its version in ETag; send that ETag in If-Match when changing the user's access. " +
                "Users of other companies are reported as not found (404). " +
                "TR: Kullanıcıyı ve sürümünü ETag'de döner; kullanıcının yetkilerini değiştirirken bu ETag'i If-Match ile " +
                "gönderin. Başka firmaların kullanıcıları bulunamadı (404) olarak döner.");

    /// <summary>
    /// EN: Handles the request with an untracked query that projects the row version for the ETag.
    /// TR: İsteği, ETag için satır sürümünü de yansıtan takipsiz bir sorguyla işler.
    /// </summary>
    /// <param name="id">EN: User id. TR: Kullanıcı kimliği.</param>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 or 404. TR: 200 veya 404.</returns>
    public static async Task<Results<Ok<UserResponse>, NotFound>> HandleAsync(
        Guid id,
        IdentityDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var row = await db.Users.SingleWithVersionAsync(id, UserResponse.Projection, cancellationToken);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.SetETag(row.Version);
        return TypedResults.Ok(row.Value);
    }
}
