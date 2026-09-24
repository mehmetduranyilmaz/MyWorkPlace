using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.BuildingBlocks.Settings;

/// <summary>
/// EN: One company's settings of one module, stored as a JSON document (ADR-018). Present in every service's
///     database, like the audit log; a row exists only once a company saved its settings.
/// TR: Bir firmanın bir modüle ait ayarları; JSON doküman olarak saklanır (ADR-018). Denetim günlüğü gibi her servisin
///     veritabanında bulunur; satır ancak firma ayarlarını kaydettiğinde oluşur.
/// </summary>
public sealed class TenantSetting : TenantOwnedEntity
{
    /// <summary>EN: Maximum module key length. TR: En fazla modül anahtarı uzunluğu.</summary>
    public const int ModuleMaxLength = 50;

    /// <summary>EN: Module key (see <see cref="IModuleSettings.Module"/>). TR: Modül anahtarı (bkz. <see cref="IModuleSettings.Module"/>).</summary>
    public required string Module { get; init; }

    /// <summary>
    /// EN: The settings as JSON (<c>jsonb</c>). Audited: "who changed the negative stock policy?" must be answerable.
    /// TR: JSON olarak ayarlar (<c>jsonb</c>). Denetlenir: "eksi stok politikasını kim değiştirdi?" cevaplanabilmelidir.
    /// </summary>
    [AuditChanges]
    public required string Values { get; set; }
}
