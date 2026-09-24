using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Features.Users;

/// <summary>
/// EN: <c>GET /identity/users</c> — one page of the caller's company users, optionally searched (ADR-016).
/// TR: <c>GET /identity/users</c> — çağıranın firma kullanıcılarından bir sayfa, isteğe bağlı aramayla (ADR-016).
/// </summary>
public static class ListUsers
{
    /// <summary>
    /// EN: Maps the endpoint on the /identity/users group.
    /// TR: Uç noktayı /identity/users grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity/users group. TR: /identity/users grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapListUsers(this IEndpointRouteBuilder group) =>
        group.MapGet("", HandleAsync)
            .WithName("ListUsers")
            .RequireAuthorization(Permissions.UsersManage)
            .WithSummary("EN: List users | TR: Kullanıcıları listele")
            .WithDescription(
                "EN: Returns one page of your company's users, sorted by email. page ≥ 1 (default 1), pageSize 1–100 " +
                "(default 20). search matches the email, case-insensitive. " +
                "TR: Firmanızın kullanıcılarından e-postaya göre sıralı bir sayfa döner. page ≥ 1 (varsayılan 1), " +
                "pageSize 1–100 (varsayılan 20). search, e-postada büyük/küçük harf duyarsız eşleşir.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the tenant and soft-delete filters apply.
    /// TR: İsteği işler; firma ve soft-delete filtreleri uygulanır.
    /// </summary>
    /// <param name="page">EN: Page and search parameters. TR: Sayfa ve arama parametreleri.</param>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    public static async Task<Ok<PagedResult<UserResponse>>> HandleAsync(
        [AsParameters] PageQuery page,
        IdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var users = db.Users.AsQueryable();

        if (page.SearchText is { } text)
        {
            var pattern = SearchPattern.Contains(text);
            users = users.Where(u => EF.Functions.ILike(u.Email, pattern, SearchPattern.EscapeCharacter));
        }

        var result = await users
            .OrderBy(u => u.NormalizedEmail)
            .ThenBy(u => u.Id)
            .ToPagedResultAsync(UserResponse.Projection, page, cancellationToken);

        return TypedResults.Ok(result);
    }
}
