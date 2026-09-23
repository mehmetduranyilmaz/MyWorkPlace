namespace MyWorkplace.BuildingBlocks.Domain;

/// <summary>
/// EN: Records who created and last changed an entity, and when. Filled by the auditing interceptor — never by hand.
/// TR: Bir entity'yi kimin, ne zaman oluşturduğunu ve en son değiştirdiğini kaydeder. Elle değil, denetim interceptor'ı doldurur.
/// </summary>
public interface IAuditable
{
    /// <summary>
    /// EN: Creation time (UTC).
    /// TR: Oluşturulma zamanı (UTC).
    /// </summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// EN: Creating user; null for system or anonymous actions such as sign-up.
    /// TR: Oluşturan kullanıcı; kayıt gibi sistem veya anonim işlemlerde null.
    /// </summary>
    Guid? CreatedBy { get; set; }

    /// <summary>
    /// EN: Last update time (UTC); null until the first update.
    /// TR: Son güncelleme zamanı (UTC); ilk güncellemeye kadar null.
    /// </summary>
    DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// EN: User who made the last update.
    /// TR: Son güncellemeyi yapan kullanıcı.
    /// </summary>
    Guid? UpdatedBy { get; set; }
}
