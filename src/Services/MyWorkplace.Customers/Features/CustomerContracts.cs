using System.ComponentModel.DataAnnotations;
using MyWorkplace.Customers.Domain;

namespace MyWorkplace.Customers.Features;

/// <summary>
/// EN: Customer form for create and (full) update. Validated before the handler runs; failures become a 400 with field errors.
/// TR: Oluşturma ve (tam) güncelleme için müşteri formu. Handler'dan önce doğrulanır; hatalar alan bazlı 400'e dönüşür.
/// </summary>
public sealed record CustomerInput
{
    /// <summary>EN: Person or company name. TR: Kişi veya firma adı.</summary>
    [Required]
    [MaxLength(Customer.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>EN: Email (optional, unique within the company). TR: E-posta (isteğe bağlı, firma içinde benzersiz).</summary>
    [EmailAddress]
    [MaxLength(Customer.EmailMaxLength)]
    public string? Email { get; init; }

    /// <summary>EN: Phone (optional). TR: Telefon (isteğe bağlı).</summary>
    [MaxLength(Customer.PhoneMaxLength)]
    public string? Phone { get; init; }

    /// <summary>EN: Tax or national id number (optional). TR: Vergi veya TC kimlik numarası (isteğe bağlı).</summary>
    [MaxLength(Customer.TaxNumberMaxLength)]
    public string? TaxNumber { get; init; }

    /// <summary>EN: Notes (optional). TR: Notlar (isteğe bağlı).</summary>
    [MaxLength(Customer.NotesMaxLength)]
    public string? Notes { get; init; }
}

/// <summary>
/// EN: What the API returns for a customer. Never the entity itself, so internal fields (TenantId, NormalizedEmail,
///     soft-delete flags) can't leak by accident. The version travels in the ETag header, not in the body.
/// TR: API'nin bir müşteri için döndürdüğü. Asla entity'nin kendisi değil; böylece iç alanlar (TenantId, NormalizedEmail,
///     soft-delete bayrakları) kazayla sızamaz. Sürüm gövdede değil, ETag başlığında taşınır.
/// </summary>
/// <param name="Id">EN: Customer id. TR: Müşteri kimliği.</param>
/// <param name="Name">EN: Name. TR: Ad.</param>
/// <param name="Email">EN: Email. TR: E-posta.</param>
/// <param name="Phone">EN: Phone. TR: Telefon.</param>
/// <param name="TaxNumber">EN: Tax number. TR: Vergi numarası.</param>
/// <param name="Notes">EN: Notes. TR: Notlar.</param>
/// <param name="CreatedAt">EN: Creation time. TR: Oluşturulma zamanı.</param>
/// <param name="UpdatedAt">EN: Last update time. TR: Son güncelleme zamanı.</param>
public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? TaxNumber,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>
    /// EN: Maps a customer to its API shape.
    /// TR: Bir müşteriyi API biçimine çevirir.
    /// </summary>
    /// <param name="customer">EN: The customer. TR: Müşteri.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    public static CustomerResponse From(Customer customer) =>
        new(customer.Id, customer.Name, customer.Email, customer.Phone, customer.TaxNumber, customer.Notes,
            customer.CreatedAt, customer.UpdatedAt);
}

/// <summary>
/// EN: Shared answers of the customer endpoints.
/// TR: Müşteri uç noktalarının ortak cevapları.
/// </summary>
internal static class CustomerProblems
{
    /// <summary>
    /// EN: 409 for an email already used by another live customer of the same company.
    /// TR: Aynı firmanın başka bir canlı müşterisinin kullandığı e-posta için 409.
    /// </summary>
    /// <returns>EN: A 409 ProblemDetails. TR: 409 ProblemDetails.</returns>
    public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult EmailTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Email already used by another customer.",
            detail: "Customer emails must be unique within a company.");
}
