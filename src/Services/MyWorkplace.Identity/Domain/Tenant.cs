using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.Identity.Domain;

/// <summary>
/// EN: A company using the system. Not <c>ITenantOwned</c>: it <i>is</i> the tenant.
/// TR: Sistemi kullanan bir firma. <c>ITenantOwned</c> değildir: kendisi firmadır.
/// </summary>
public sealed class Tenant : AuditableEntity
{
    /// <summary>EN: Maximum length of <see cref="Name"/>. TR: <see cref="Name"/> için en fazla uzunluk.</summary>
    public const int NameMaxLength = 200;

    /// <summary>
    /// EN: Company name as entered at sign-up.
    /// TR: Kayıtta girildiği haliyle firma adı.
    /// </summary>
    [AuditChanges]
    public required string Name { get; set; }

    /// <summary>
    /// EN: Current plan; every new company starts on Basic.
    /// TR: Mevcut plan; her yeni firma Basic ile başlar.
    /// </summary>
    [AuditChanges]
    public Plan Plan { get; set; } = Plan.Basic;
}
