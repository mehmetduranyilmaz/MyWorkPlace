using System.ComponentModel.DataAnnotations;
using MyWorkplace.Identity.Domain;

namespace MyWorkplace.Identity.Features.Register;

/// <summary>
/// EN: Sign-up form. Validated automatically before the handler runs; failures become a 400 with field errors.
/// TR: Kayıt formu. Handler çalışmadan önce otomatik doğrulanır; hatalar alan bazlı bir 400'e dönüşür.
/// </summary>
public sealed record RegisterTenantRequest
{
    /// <summary>EN: Minimum password length (ADR-013). TR: En kısa parola uzunluğu (ADR-013).</summary>
    public const int PasswordMinLength = 8;

    /// <summary>
    /// EN: Name of the new company.
    /// TR: Yeni firmanın adı.
    /// </summary>
    [Required]
    [MaxLength(Tenant.NameMaxLength)]
    public string? CompanyName { get; init; }

    /// <summary>
    /// EN: Email of the first user; used to sign in.
    /// TR: İlk kullanıcının e-postası; girişte kullanılır.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(User.EmailMaxLength)]
    public string? Email { get; init; }

    /// <summary>
    /// EN: Password of the first user.
    /// TR: İlk kullanıcının parolası.
    /// </summary>
    [Required]
    [MinLength(PasswordMinLength)]
    [MaxLength(128)]
    public string? Password { get; init; }
}

/// <summary>
/// EN: Ids of the created company and user.
/// TR: Oluşturulan firmanın ve kullanıcının kimlikleri.
/// </summary>
/// <param name="TenantId">EN: New company id. TR: Yeni firma kimliği.</param>
/// <param name="UserId">EN: New user id. TR: Yeni kullanıcı kimliği.</param>
public sealed record RegisterTenantResponse(Guid TenantId, Guid UserId);
