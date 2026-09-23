using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Persistence;
using MyWorkplace.Identity.Tokens;

namespace MyWorkplace.Identity.Features.Login;

/// <summary>
/// EN: Sign-in form.
/// TR: Giriş formu.
/// </summary>
public sealed record LoginRequest
{
    /// <summary>EN: Email used at sign-up (any letter case). TR: Kayıtta kullanılan e-posta (harf büyüklüğü fark etmez).</summary>
    [Required]
    [MaxLength(User.EmailMaxLength)]
    public string? Email { get; init; }

    /// <summary>EN: Password. TR: Parola.</summary>
    [Required]
    [MaxLength(128)]
    public string? Password { get; init; }
}

/// <summary>
/// EN: Sign-in feature: exchanges email and password for an access token.
/// TR: Giriş özelliği: e-posta ve parolayı bir erişim token'ıyla değiştirir.
/// </summary>
public static class Login
{
    /// <summary>
    /// EN: Hash of a random password, verified when the email is unknown so both failure paths take the same time.
    /// TR: Rastgele bir parolanın hash'i; e-posta bilinmediğinde doğrulanır, böylece iki hata yolu da aynı sürede biter.
    /// </summary>
    private static readonly string _timingDecoyHash =
        new PasswordHasher<User>().HashPassword(null!, Guid.NewGuid().ToString());

    /// <summary>
    /// EN: Maps <c>POST /login</c> on the given route group.
    /// TR: Verilen rota grubunda <c>POST /login</c> uç noktasını tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity route group. TR: /identity rota grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapLogin(this IEndpointRouteBuilder group) =>
        group.MapPost("/login", HandleAsync)
            .WithName("Login")
            .WithSummary("EN: Sign in | TR: Giriş yap")
            .WithDescription(
                "EN: Returns an RS256-signed access token valid for 15 minutes. A wrong email and a wrong password " +
                "return the same 401 on purpose, so the response never reveals whether an email is registered. " +
                "TR: 15 dakika geçerli, RS256 ile imzalı bir erişim token'ı döner. Yanlış e-posta ve yanlış parola " +
                "bilerek aynı 401'i döner; böylece cevap bir e-postanın kayıtlı olup olmadığını asla ele vermez.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Verifies the credentials and issues a token.
    /// TR: Kimlik bilgilerini doğrular ve token üretir.
    /// </summary>
    /// <param name="request">EN: Sign-in form. TR: Giriş formu.</param>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="passwordHasher">EN: Password hasher. TR: Parola hash'leyici.</param>
    /// <param name="tokens">EN: Token issuer. TR: Token üreticisi.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 with a token, or 401. TR: Token ile 200 veya 401.</returns>
    public static async Task<Results<Ok<IssuedToken>, ProblemHttpResult>> HandleAsync(
        LoginRequest request,
        IdentityDbContext db,
        IPasswordHasher<User> passwordHasher,
        TokenIssuer tokens,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = User.Normalize(request.Email!);

        // EN: Cross-tenant on purpose: nobody is signed in yet, and the email identifies the tenant.
        // TR: Bilinçli olarak firmalar arası: henüz kimse giriş yapmamış ve firmayı e-posta belirler.
        var match = await db.Users
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Join(db.Tenants, u => u.TenantId, t => t.Id, (user, tenant) => new { User = user, tenant.Plan })
            .SingleOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            // EN: Do the same expensive work as a real check, so timing can't tell "unknown email" from "wrong password".
            // TR: Gerçek kontrolle aynı pahalı işi yap; böylece süre "bilinmeyen e-posta" ile "yanlış parola"yı ayırt ettirmez.
            passwordHasher.VerifyHashedPassword(null!, _timingDecoyHash, request.Password!);
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(match.User, match.User.PasswordHash, request.Password!);
        if (verification == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await RehashAsync(db, passwordHasher, match.User.Id, request.Password!, cancellationToken);
        }

        return TypedResults.Ok(tokens.Issue(match.User, match.Plan));
    }

    /// <summary>
    /// EN: Upgrades a hash made with older hasher settings, transparently, while the plain password is at hand.
    /// TR: Eski hasher ayarlarıyla üretilmiş bir hash'i, düz parola elimizdeyken, fark ettirmeden yükseltir.
    /// </summary>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="passwordHasher">EN: Password hasher. TR: Parola hash'leyici.</param>
    /// <param name="userId">EN: User to update. TR: Güncellenecek kullanıcı.</param>
    /// <param name="password">EN: Verified password. TR: Doğrulanmış parola.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static async Task RehashAsync(
        IdentityDbContext db,
        IPasswordHasher<User> passwordHasher,
        Guid userId,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AsTracking()
            .SingleAsync(u => u.Id == userId, cancellationToken);
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// EN: The single 401 answer for every kind of failed sign-in.
    /// TR: Her türlü başarısız giriş için tek 401 cevabı.
    /// </summary>
    /// <returns>EN: A 401 ProblemDetails. TR: 401 ProblemDetails.</returns>
    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid email or password.");
}
