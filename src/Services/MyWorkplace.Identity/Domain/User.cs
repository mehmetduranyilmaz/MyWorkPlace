using MyWorkplace.BuildingBlocks.Domain;
using MyWorkplace.Contracts.Identity;
using RoleCatalog = MyWorkplace.Contracts.Identity.Roles;

namespace MyWorkplace.Identity.Domain;

/// <summary>
/// EN: A person who signs in. Belongs to exactly one company; the email is unique among live users (ADR-013).
///     Soft-deletable: a removed user can't sign in, but the audit trail still points to them (ADR-022).
/// TR: Giriş yapan kişi. Tam olarak bir firmaya aittir; e-posta canlı kullanıcılar arasında benzersizdir (ADR-013).
///     Soft-delete edilebilir: kaldırılan kullanıcı giriş yapamaz ama denetim kayıtları hâlâ onu gösterir (ADR-022).
/// </summary>
public sealed class User : BusinessEntity
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
    /// EN: Replaces the user's extra permissions; unknown permission names are rejected.
    /// TR: Kullanıcının ek izinlerini değiştirir; bilinmeyen izin adları reddedilir.
    /// </summary>
    /// <param name="permissions">EN: Permission names. TR: İzin adları.</param>
    public void GrantExtraPermissions(IEnumerable<string> permissions)
    {
        var granted = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unknown = granted.Where(permission => !Permissions.All.Contains(permission)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Unknown permission(s): {string.Join(", ", unknown)}.", nameof(permissions));
        }

        ExtraPermissions = granted;
    }

    /// <summary>
    /// EN: Whether this user owns the company.
    /// TR: Bu kullanıcının firmanın sahibi olup olmadığı.
    /// </summary>
    public bool IsOwner => Roles.Contains(RoleCatalog.Owner, StringComparer.Ordinal);

    /// <summary>
    /// EN: The anti-escalation rule (ADR-022), with this user as the one making the change: only an Owner may touch an
    ///     Owner or hand out the Owner role, and nobody may grant a permission they don't hold themselves.
    ///     Taking permissions away is always allowed (the last-Owner rule is checked separately).
    /// TR: Yetki yükseltme kuralı (ADR-022); değişikliği yapan bu kullanıcıdır: bir Sahip'e dokunmayı veya Sahip rolünü
    ///     vermeyi sadece bir Sahip yapabilir ve kimse kendinde olmayan bir izni veremez.
    ///     İzin geri almak her zaman serbesttir (son Sahip kuralı ayrıca kontrol edilir).
    /// </summary>
    /// <param name="target">EN: The user being changed, or null for a new user. TR: Değiştirilen kullanıcı; yeni kullanıcı için null.</param>
    /// <param name="roles">EN: The target's roles after the change. TR: Hedefin değişiklik sonrası rolleri.</param>
    /// <param name="extraPermissions">EN: The target's extra permissions after the change. TR: Hedefin değişiklik sonrası ek izinleri.</param>
    /// <returns>EN: True if the change is allowed. TR: Değişikliğe izin veriliyorsa true.</returns>
    public bool MayAssignAccess(User? target, IEnumerable<string> roles, IEnumerable<string> extraPermissions)
    {
        var newRoles = roles.ToArray();
        if (!IsOwner && (target?.IsOwner == true || newRoles.Contains(RoleCatalog.Owner, StringComparer.Ordinal)))
        {
            return false;
        }

        var before = target?.EffectivePermissions ?? [];
        var own = EffectivePermissions;
        return RoleCatalog.EffectivePermissions(newRoles, extraPermissions)
            .Except(before, StringComparer.Ordinal)
            .All(permission => own.Contains(permission, StringComparer.Ordinal));
    }

    /// <summary>
    /// EN: Whether this user may remove <paramref name="target"/>: only an Owner may remove an Owner (ADR-022).
    /// TR: Bu kullanıcının <paramref name="target"/>'ı kaldırıp kaldıramayacağı: bir Sahip'i sadece bir Sahip kaldırabilir (ADR-022).
    /// </summary>
    /// <param name="target">EN: The user to remove. TR: Kaldırılacak kullanıcı.</param>
    /// <returns>EN: True if allowed. TR: İzin veriliyorsa true.</returns>
    public bool MayRemove(User target) => IsOwner || !target.IsOwner;

    /// <summary>
    /// EN: The single normalization rule for emails, used when saving and when searching.
    /// TR: Kaydederken ve ararken kullanılan tek e-posta normalizasyon kuralı.
    /// </summary>
    /// <param name="email">EN: Email as entered. TR: Girildiği haliyle e-posta.</param>
    /// <returns>EN: Trimmed, lower-case email. TR: Boşlukları kırpılmış, küçük harfli e-posta.</returns>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
