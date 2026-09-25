using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MyWorkplace.Abstractions.Identity;

namespace MyWorkplace.BuildingBlocks.Identity;

/// <summary>
/// EN: Reads the current user from the request's validated token. Only authenticated principals count, and malformed
///     claims read as null — so a bad token can never select a tenant (ADR-004, T-008).
/// TR: Aktif kullanıcıyı isteğin doğrulanmış token'ından okur. Sadece kimliği doğrulanmış kullanıcılar sayılır ve bozuk
///     claim'ler null okunur — böylece hatalı bir token asla bir firma seçemez (ADR-004, T-008).
/// </summary>
/// <param name="accessor">EN: Access to the current request. TR: Mevcut isteğe erişim.</param>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId => ReadGuid(TokenClaims.Subject);

    /// <inheritdoc />
    public Guid? TenantId => ReadGuid(TokenClaims.TenantId);

    /// <inheritdoc />
    public string? Plan => AuthenticatedUser?.FindFirst(TokenClaims.Plan)?.Value;

    /// <summary>
    /// EN: The request's user if the token was validated; null outside requests or for anonymous callers.
    /// TR: Token doğrulandıysa isteğin kullanıcısı; istek dışında veya anonim çağıranlar için null.
    /// </summary>
    private ClaimsPrincipal? AuthenticatedUser =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    /// <summary>
    /// EN: Reads a claim as a GUID; missing or malformed values become null.
    /// TR: Bir claim'i GUID olarak okur; eksik veya bozuk değerler null olur.
    /// </summary>
    /// <param name="claimType">EN: Claim name. TR: Claim adı.</param>
    /// <returns>EN: The id or null. TR: Kimlik veya null.</returns>
    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(AuthenticatedUser?.FindFirst(claimType)?.Value, out var id) ? id : null;
}
