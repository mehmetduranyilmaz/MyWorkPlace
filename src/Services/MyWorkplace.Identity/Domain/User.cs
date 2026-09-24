using MyWorkplace.BuildingBlocks.Domain;
using RoleCatalog = MyWorkplace.Contracts.Identity.Roles;

namespace MyWorkplace.Identity.Domain;

/// <summary>
/// EN: A person who signs in. Belongs to exactly one company; the email is unique across the system (ADR-013).
/// TR: Giriş yapan kişi. Tam olarak bir firmaya aittir; e-posta tüm sistemde benzersizdir (ADR-013).
/// </summary>
public sealed class User : TenantOwnedEntity
{
    /// <summary>EN: Maximum email length (RFC 5321). TR: En fazla e-posta uzunluğu (RFC 5321).</summary>
    public const int EmailMaxLength = 320;

    /// <summary>
    /// EN: Email as the user typed it (trimmed); shown back to the user.
    /// TR: Kullanıcının yazdığı haliyle e-posta (boşlukları kırpılmış); kullanıcıya bu gösterilir.
    /// </summary>
    [AuditChanges]
    public required string Email { get; set; }

    /// <summary>
    /// EN: Lower-case email used for lookups; the unique index is on this column so "A@x.com" equals "a@x.com".
    /// TR: Aramada kullanılan küçük harfli e-posta; benzersiz indeks bu sütundadır, böylece "A@x.com" ile "a@x.com" aynıdır.
    /// </summary>
    public required string NormalizedEmail { get; set; }

    /// <summary>
    /// EN: Salted PBKDF2 hash from <c>PasswordHasher</c>. Never the password itself; never audited.
    /// TR: <c>PasswordHasher</c>'ın ürettiği tuzlu PBKDF2 hash'i. Asla parolanın kendisi değildir; asla denetim günlüğüne yazılmaz.
    /// </summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// EN: Assigned roles (ADR-022). Audited: a role change is a security event.
    /// TR: Atanmış roller (ADR-022). Denetlenir: rol değişikliği bir güvenlik olayıdır.
    /// </summary>
    [AuditChanges]
    public string[] Roles { get; private set; } = [];

    /// <summary>
    /// EN: Permissions granted on top of the roles (grant only, no deny). Audited.
    /// TR: Rollerin üzerine verilen izinler (sadece verme, yasak yok). Denetlenir.
    /// </summary>
    [AuditChanges]
    public string[] ExtraPermissions { get; private set; } = [];

    /// <summary>
    /// EN: What this user may do: the union of the roles' permissions and the extra grants.
    /// TR: Bu kullanıcının neler yapabileceği: rollerin izinleri ile ek izinlerin birleşimi.
    /// </summary>
    public IReadOnlyList<string> EffectivePermissions =>
        RoleCatalog.EffectivePermissions(Roles, ExtraPermissions);

    /// <summary>
    /// EN: Replaces the user's roles; unknown role names are rejected so typos can't be stored.
    /// TR: Kullanıcının rollerini değiştirir; bilinmeyen rol adları reddedilir, böylece yazım hataları saklanamaz.
    /// </summary>
    /// <param name="roles">EN: Role names. TR: Rol adları.</param>
    public void AssignRoles(IEnumerable<string> roles)
    {
        var assigned = roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unknown = assigned.Where(role => !RoleCatalog.All.Contains(role)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Unknown role(s): {string.Join(", ", unknown)}.", nameof(roles));
        }

        Roles = assigned;
    }

    /// <summary>
    /// EN: The single normalization rule for emails, used when saving and when searching.
    /// TR: Kaydederken ve ararken kullanılan tek e-posta normalizasyon kuralı.
    /// </summary>
    /// <param name="email">EN: Email as entered. TR: Girildiği haliyle e-posta.</param>
    /// <returns>EN: Trimmed, lower-case email. TR: Boşlukları kırpılmış, küçük harfli e-posta.</returns>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
