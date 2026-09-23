using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Identity.Domain;

/// <summary>
/// EN: A person who signs in. Belongs to exactly one company; the email is unique across the system (ADR-013).
/// TR: Giriş yapan kişi. Tam olarak bir firmaya aittir; e-posta tüm sistemde benzersizdir (ADR-013).
/// </summary>
public sealed class User : Entity, ITenantOwned, IAuditable
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

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// EN: The single normalization rule for emails, used when saving and when searching.
    /// TR: Kaydederken ve ararken kullanılan tek e-posta normalizasyon kuralı.
    /// </summary>
    /// <param name="email">EN: Email as entered. TR: Girildiği haliyle e-posta.</param>
    /// <returns>EN: Trimmed, lower-case email. TR: Boşlukları kırpılmış, küçük harfli e-posta.</returns>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
