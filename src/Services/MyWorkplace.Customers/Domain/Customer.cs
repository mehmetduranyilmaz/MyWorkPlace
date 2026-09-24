using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Customers.Domain;

/// <summary>
/// EN: A customer of a company. Tenant-owned (isolated per company), audited and soft-deletable (ADR-011, ADR-016).
/// TR: Bir firmanın müşterisi. Firmaya ait (firma bazında izole), denetlenen ve soft-delete edilebilen (ADR-011, ADR-016).
/// </summary>
public sealed class Customer : BusinessEntity
{
    /// <summary>EN: Max length of <see cref="Name"/>. TR: <see cref="Name"/> için en fazla uzunluk.</summary>
    public const int NameMaxLength = 200;

    /// <summary>EN: Max length of <see cref="Email"/> (RFC 5321). TR: <see cref="Email"/> için en fazla uzunluk (RFC 5321).</summary>
    public const int EmailMaxLength = 320;

    /// <summary>EN: Max length of <see cref="Phone"/>. TR: <see cref="Phone"/> için en fazla uzunluk.</summary>
    public const int PhoneMaxLength = 30;

    /// <summary>EN: Max length of <see cref="TaxNumber"/>. TR: <see cref="TaxNumber"/> için en fazla uzunluk.</summary>
    public const int TaxNumberMaxLength = 20;

    /// <summary>EN: Max length of <see cref="Notes"/>. TR: <see cref="Notes"/> için en fazla uzunluk.</summary>
    public const int NotesMaxLength = 2000;

    /// <summary>EN: Person or company name. TR: Kişi veya firma adı.</summary>
    [AuditChanges]
    public string Name { get; private set; } = "";

    /// <summary>EN: Email as entered (optional). TR: Girildiği haliyle e-posta (isteğe bağlı).</summary>
    [AuditChanges]
    public string? Email { get; private set; }

    /// <summary>
    /// EN: Lower-case email for the per-tenant uniqueness check; kept in sync with <see cref="Email"/> by <see cref="Update"/>.
    /// TR: Firma içi benzersizlik kontrolü için küçük harfli e-posta; <see cref="Update"/> ile <see cref="Email"/>'le uyumlu tutulur.
    /// </summary>
    public string? NormalizedEmail { get; private set; }

    /// <summary>EN: Phone number (optional). TR: Telefon numarası (isteğe bağlı).</summary>
    [AuditChanges]
    public string? Phone { get; private set; }

    /// <summary>EN: Tax or national id number (optional). TR: Vergi veya TC kimlik numarası (isteğe bağlı).</summary>
    [AuditChanges]
    public string? TaxNumber { get; private set; }

    /// <summary>EN: Free-text notes; not audited to keep the log readable. TR: Serbest not; günlük okunaklı kalsın diye denetlenmez.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// EN: Sets every editable field at once (full update, ADR-016). Values are trimmed and blanks become null,
    ///     so " " and "" never end up stored as meaningful data.
    /// TR: Düzenlenebilir tüm alanları bir kerede atar (tam güncelleme, ADR-016). Değerler kırpılır, boşlar null olur;
    ///     böylece " " ve "" anlamlı veri gibi saklanmaz.
    /// </summary>
    /// <param name="name">EN: Name. TR: Ad.</param>
    /// <param name="email">EN: Email or null. TR: E-posta veya null.</param>
    /// <param name="phone">EN: Phone or null. TR: Telefon veya null.</param>
    /// <param name="taxNumber">EN: Tax number or null. TR: Vergi numarası veya null.</param>
    /// <param name="notes">EN: Notes or null. TR: Not veya null.</param>
    public void Update(string name, string? email, string? phone, string? taxNumber, string? notes)
    {
        Name = name.Trim();
        Email = Clean(email);
        NormalizedEmail = NormalizeEmail(email);
        Phone = Clean(phone);
        TaxNumber = Clean(taxNumber);
        Notes = Clean(notes);
    }

    /// <summary>
    /// EN: The single email normalization rule, used when saving and when checking uniqueness.
    /// TR: Kaydederken ve benzersizliği kontrol ederken kullanılan tek e-posta normalizasyon kuralı.
    /// </summary>
    /// <param name="email">EN: Email as entered. TR: Girildiği haliyle e-posta.</param>
    /// <returns>EN: Trimmed lower-case email, or null. TR: Kırpılmış küçük harfli e-posta veya null.</returns>
    public static string? NormalizeEmail(string? email) => Clean(email)?.ToLowerInvariant();

    /// <summary>
    /// EN: Trims a value; blank becomes null.
    /// TR: Bir değeri kırpar; boş değer null olur.
    /// </summary>
    /// <param name="value">EN: Value. TR: Değer.</param>
    /// <returns>EN: Cleaned value. TR: Temizlenmiş değer.</returns>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
