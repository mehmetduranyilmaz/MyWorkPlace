using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.Abstractions.Identity;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Features.Register;

namespace MyWorkplace.Identity.Features.Users;

/// <summary>
/// EN: Roles and extra permissions of a user. Unknown names are rejected with a 400 before the handler runs.
/// TR: Bir kullanıcının rolleri ve ek izinleri. Bilinmeyen adlar handler çalışmadan 400 ile reddedilir.
/// </summary>
public sealed record AccessInput : IValidatableObject
{
    /// <summary>
    /// EN: Role names (Owner, Admin, Member, Viewer). At least one.
    /// TR: Rol adları (Owner, Admin, Member, Viewer). En az bir tane.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string[]? Roles { get; init; }

    /// <summary>
    /// EN: Permissions granted on top of the roles (optional).
    /// TR: Rollerin üzerine verilen izinler (isteğe bağlı).
    /// </summary>
    public string[]? ExtraPermissions { get; init; }

    /// <summary>
    /// EN: Checks the names against the role and permission catalogs (ADR-022).
    /// TR: Adları rol ve izin kataloglarına göre kontrol eder (ADR-022).
    /// </summary>
    /// <param name="validationContext">EN: Validation context. TR: Doğrulama bağlamı.</param>
    /// <returns>EN: One error per field with unknown names. TR: Bilinmeyen ad içeren her alan için bir hata.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var unknownRoles = (Roles ?? []).Where(role => !DefaultRoles.All.Contains(role)).ToArray();
        if (unknownRoles.Length > 0)
        {
            yield return new ValidationResult($"Unknown role(s): {string.Join(", ", unknownRoles)}.", [nameof(Roles)]);
        }

        var unknownPermissions = (ExtraPermissions ?? []).Where(p => !Permissions.Catalog.All.Contains(p)).ToArray();
        if (unknownPermissions.Length > 0)
        {
            yield return new ValidationResult(
                $"Unknown permission(s): {string.Join(", ", unknownPermissions)}.", [nameof(ExtraPermissions)]);
        }
    }
}

/// <summary>
/// EN: Form for adding a user to the caller's company. Roles default to Member when omitted.
/// TR: Çağıranın firmasına kullanıcı ekleme formu. Rol verilmezse Member olur.
/// </summary>
public sealed record NewUserInput : IValidatableObject
{
    /// <summary>EN: Email; used to sign in, unique among live users. TR: E-posta; girişte kullanılır, canlı kullanıcılar arasında benzersizdir.</summary>
    [Required]
    [EmailAddress]
    [MaxLength(User.EmailMaxLength)]
    public string? Email { get; init; }

    /// <summary>EN: Initial password, handed over to the person. TR: Kişiye iletilen ilk parola.</summary>
    [Required]
    [MinLength(RegisterTenantRequest.PasswordMinLength)]
    [MaxLength(128)]
    public string? Password { get; init; }

    /// <summary>EN: Role names (default: Member). TR: Rol adları (varsayılan: Member).</summary>
    [MinLength(1)]
    public string[]? Roles { get; init; }

    /// <summary>EN: Extra permissions (optional). TR: Ek izinler (isteğe bağlı).</summary>
    public string[]? ExtraPermissions { get; init; }

    /// <summary>EN: Roles to assign, with the default applied. TR: Varsayılanı uygulanmış, atanacak roller.</summary>
    public string[] RolesOrDefault => Roles ?? [DefaultRoles.Member];

    /// <summary>
    /// EN: Same catalog check as <see cref="AccessInput"/>.
    /// TR: <see cref="AccessInput"/> ile aynı katalog kontrolü.
    /// </summary>
    /// <param name="validationContext">EN: Validation context. TR: Doğrulama bağlamı.</param>
    /// <returns>EN: Errors for unknown names. TR: Bilinmeyen adlar için hatalar.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        new AccessInput { Roles = RolesOrDefault, ExtraPermissions = ExtraPermissions }.Validate(validationContext);
}

/// <summary>
/// EN: What the API returns for a user. Never the password hash; the version travels in the ETag header.
/// TR: API'nin bir kullanıcı için döndürdüğü. Asla parola hash'i değil; sürüm ETag başlığında taşınır.
/// </summary>
/// <param name="Id">EN: User id. TR: Kullanıcı kimliği.</param>
/// <param name="Email">EN: Email. TR: E-posta.</param>
/// <param name="Roles">EN: Roles. TR: Roller.</param>
/// <param name="ExtraPermissions">EN: Extra permissions. TR: Ek izinler.</param>
/// <param name="CreatedAt">EN: Creation time. TR: Oluşturulma zamanı.</param>
/// <param name="UpdatedAt">EN: Last update time. TR: Son güncelleme zamanı.</param>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string[] Roles,
    string[] ExtraPermissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>
    /// EN: The one mapping from entity to API shape (ADR-021).
    /// TR: Entity'den API biçimine tek eşleme (ADR-021).
    /// </summary>
    public static readonly Expression<Func<User, UserResponse>> Projection = u =>
        new UserResponse(u.Id, u.Email, u.Roles, u.ExtraPermissions, u.CreatedAt, u.UpdatedAt);

    /// <summary>EN: <see cref="Projection"/>, compiled once. TR: Bir kez derlenmiş <see cref="Projection"/>.</summary>
    private static readonly Func<User, UserResponse> _map = Projection.Compile();

    /// <summary>
    /// EN: Maps an entity already in memory.
    /// TR: Bellekteki bir entity'yi eşler.
    /// </summary>
    /// <param name="user">EN: The user. TR: Kullanıcı.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static UserResponse From(User user) => _map(user);
}

/// <summary>
/// EN: Shared answers of the user-management endpoints.
/// TR: Kullanıcı yönetimi uç noktalarının ortak cevapları.
/// </summary>
internal static class UserProblems
{
    /// <summary>
    /// EN: 409 for an email already used by a live user.
    /// TR: Canlı bir kullanıcının kullandığı e-posta için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult EmailTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Email already registered.",
            detail: "Every user needs their own email address.");

    /// <summary>
    /// EN: 409 when a change would leave the company without an Owner.
    /// TR: Bir değişiklik firmayı Sahip'siz bırakacaksa 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static ProblemHttpResult LastOwner() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The company must keep at least one Owner.",
            detail: "Make another user Owner first.");

    /// <summary>
    /// EN: 403 for a change the caller is not allowed to make (anti-escalation rule, ADR-022).
    /// TR: Çağıranın yapmaya yetkisi olmayan bir değişiklik için 403 (yetki yükseltme kuralı, ADR-022).
    /// </summary>
    /// <returns>EN: A 403 ProblemDetails. TR: 403 ProblemDetails.</returns>
    public static ProblemHttpResult NotAllowed() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "You can't make this change.",
            detail: "Only an Owner can change Owners, and nobody can grant a permission they don't have.");
}
