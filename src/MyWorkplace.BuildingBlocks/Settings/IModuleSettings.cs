namespace MyWorkplace.BuildingBlocks.Settings;

/// <summary>
/// EN: A module's settings class (ADR-018): a plain class whose property initializers are the code defaults.
///     Adding a setting = adding a property with a default; no migration. DataAnnotations on the properties are
///     checked on save.
/// TR: Bir modülün ayar sınıfı (ADR-018): alan başlangıç değerleri koddaki varsayılanlar olan düz bir sınıf.
///     Ayar eklemek = varsayılanıyla bir alan eklemek; migration yok. Alanlardaki DataAnnotations kaydederken kontrol edilir.
/// </summary>
public interface IModuleSettings
{
    /// <summary>
    /// EN: Key of the module's row in <c>tenant_settings</c>, e.g. "inventory". Never change it once released.
    /// TR: Modülün <c>tenant_settings</c> içindeki satır anahtarı, ör. "inventory". Yayınlandıktan sonra asla değiştirilmez.
    /// </summary>
    static abstract string Module { get; }
}
