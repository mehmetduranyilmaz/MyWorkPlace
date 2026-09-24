using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Features.Users;

/// <summary>
/// EN: <c>POST /identity/users</c> — adds a user to the caller's company with an initial password.
/// TR: <c>POST /identity/users</c> — çağıranın firmasına ilk parolasıyla bir kullanıcı ekler.
/// </summary>
public static class CreateUser
{
    /// <summary>
    /// EN: Maps the endpoint on the /identity/users group.
    /// TR: Uç noktayı /identity/users grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity/users group. TR: /identity/users grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapCreateUser(this IEndpointRouteBuilder group) =>
        group.MapPost("", HandleAsync)
            .WithName("CreateUser")
            .RequireAuthorization(Permissions.UsersManage)
            .WithSummary("EN: Add a user | TR: Kullanıcı ekle")
            .WithDescription(
                "EN: Adds a user to your company with an initial password; roles default to Member. The email must not " +
                "be in use (409). You can't grant the Owner role unless you are an Owner, nor a permission you don't " +
                "have yourself (403). " +
                "TR: Firmanıza ilk parolasıyla bir kullanıcı ekler; rol verilmezse Member olur. E-posta kullanımda " +
                "olmamalıdır (409). Sahip değilseniz Owner rolünü, kendinizde olmayan bir izni de veremezsiniz (403).")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the input has already been validated against the catalogs.
    /// TR: İsteği işler; girdi kataloglara göre zaten doğrulanmıştır.
    /// </summary>
    /// <param name="input">EN: New user form. TR: Yeni kullanıcı formu.</param>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="currentUser">EN: The signed-in user. TR: Giriş yapmış kullanıcı.</param>
    /// <param name="passwordHasher">EN: Password hasher. TR: Parola hash'leyici.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201, 403 or 409. TR: 201, 403 veya 409.</returns>
    public static async Task<Results<Created<UserResponse>, ProblemHttpResult>> HandleAsync(
        NewUserInput input,
        IdentityDbContext db,
        ICurrentUser currentUser,
        IPasswordHasher<User> passwordHasher,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var extraPermissions = input.ExtraPermissions ?? [];
        var caller = await UserAdministration.FindCallerAsync(db, currentUser, cancellationToken);
        if (caller is null || !caller.MayAssignAccess(null, input.RolesOrDefault, extraPermissions))
        {
            return UserProblems.NotAllowed();
        }

        var normalizedEmail = User.Normalize(input.Email!);

        // EN: Emails are unique system-wide (they identify the company at sign-in), so every company is checked.
        //     Fast, friendly answer; the unique index is the real guarantee.
        // TR: E-postalar tüm sistemde benzersizdir (girişte firmayı belirler), bu yüzden tüm firmalara bakılır.
        //     Hızlı ve anlaşılır cevap; asıl garanti benzersiz indekstir.
        var emailTaken = await db.Users
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (emailTaken)
        {
            return UserProblems.EmailTaken();
        }

        // EN: TenantId is left empty on purpose: the auditing interceptor stamps the caller's company.
        // TR: TenantId bilerek boş bırakılır: denetim interceptor'ı çağıranın firmasını yazar.
        var user = new User { Email = input.Email!.Trim(), NormalizedEmail = normalizedEmail };
        user.PasswordHash = passwordHasher.HashPassword(user, input.Password!);
        user.AssignRoles(input.RolesOrDefault);
        user.GrantExtraPermissions(extraPermissions);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return UserProblems.EmailTaken();
        }

        http.Response.SetETag(db.GetVersion(user));
        return TypedResults.Created($"/identity/users/{user.Id}", UserResponse.From(user));
    }
}
