using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Features.Register;

/// <summary>
/// EN: Sign-up feature: creates a company on the Basic plan together with its first user.
/// TR: Kayıt özelliği: Basic planda bir firmayı ilk kullanıcısıyla birlikte oluşturur.
/// </summary>
public static class RegisterTenant
{
    /// <summary>
    /// EN: Maps <c>POST /register</c> on the given route group.
    /// TR: Verilen rota grubunda <c>POST /register</c> uç noktasını tanımlar.
    /// </summary>
    /// <param name="group">EN: The /identity route group. TR: /identity rota grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapRegisterTenant(this IEndpointRouteBuilder group) =>
        group.MapPost("/register", HandleAsync)
            .WithName("RegisterTenant")
            // EN: Public on purpose: no token exists yet at this point. TR: Bilerek herkese açık: bu noktada henüz token yok.
            .AllowAnonymous()
            .WithSummary("EN: Register a company | TR: Firma kaydı")
            .WithDescription(
                "EN: Creates a company on the Basic plan and its first user. The email must not be registered yet " +
                "(case-insensitive). " +
                "TR: Basic planda bir firma ve ilk kullanıcısını oluşturur. E-posta daha önce kayıtlı olmamalıdır " +
                "(büyük/küçük harf duyarsız).")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the sign-up. The request has already been validated at this point.
    /// TR: Kaydı işler. İstek bu noktada zaten doğrulanmıştır.
    /// </summary>
    /// <param name="request">EN: Sign-up form. TR: Kayıt formu.</param>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="passwordHasher">EN: Password hasher. TR: Parola hash'leyici.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 201 with the new ids, or 409 if the email is taken. TR: Yeni kimliklerle 201; e-posta kullanılıyorsa 409.</returns>
    public static async Task<Results<Created<RegisterTenantResponse>, ProblemHttpResult>> HandleAsync(
        RegisterTenantRequest request,
        IdentityDbContext db,
        IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = User.Normalize(request.Email!);

        // EN: Deliberately cross-tenant: nobody is signed in and the email must be unique system-wide.
        //     This check only gives a fast, friendly answer; the unique index below is the real guarantee.
        // TR: Bilinçli olarak firmalar arası: kimse giriş yapmamış ve e-posta tüm sistemde benzersiz olmalı.
        //     Bu kontrol sadece hızlı ve anlaşılır bir cevap verir; asıl garanti aşağıdaki benzersiz indekstir.
        var emailTaken = await db.Users
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (emailTaken)
        {
            return EmailTaken();
        }

        var tenant = new Tenant { Name = request.CompanyName!.Trim() };
        var user = new User
        {
            Email = request.Email!.Trim(),
            NormalizedEmail = normalizedEmail,
            // EN: Set explicitly: the new user belongs to the tenant being created, not to the (anonymous) caller.
            // TR: Açıkça atanır: yeni kullanıcı, (anonim) çağırana değil, oluşturulan firmaya aittir.
            TenantId = tenant.Id,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);

        db.Tenants.Add(tenant);
        db.Users.Add(user);

        try
        {
            // EN: One SaveChanges = one transaction: either both rows exist or neither.
            // TR: Tek SaveChanges = tek transaction: ya iki satır da oluşur ya hiçbiri.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // EN: Another sign-up with the same email won the race between our check and our insert.
            // TR: Aynı e-postayla yapılan başka bir kayıt, kontrolümüz ile eklememiz arasındaki yarışı kazandı.
            return EmailTaken();
        }

        return TypedResults.Created($"/identity/tenants/{tenant.Id}", new RegisterTenantResponse(tenant.Id, user.Id));
    }

    /// <summary>
    /// EN: The 409 answer for an email that is already registered.
    /// TR: Zaten kayıtlı bir e-posta için 409 cevabı.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    private static ProblemHttpResult EmailTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Email already registered.",
            detail: "Sign in instead, or use another email address.");
}
