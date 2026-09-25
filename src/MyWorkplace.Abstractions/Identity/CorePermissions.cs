namespace MyWorkplace.Abstractions.Identity;

/// <summary>
/// EN: Permissions the core itself protects its endpoints with. A product's catalog must declare them (usually by
///     assigning these constants), so <see cref="PermissionCatalog.FromType"/> checks that they are present.
/// TR: Çekirdeğin kendi uç noktalarını koruduğu izinler. Bir ürünün kataloğu bunları bildirmek zorundadır (genelde bu sabitleri atayarak);
///     bu yüzden <see cref="PermissionCatalog.FromType"/> var olduklarını kontrol eder.
/// </summary>
public static class CorePermissions
{
    /// <summary>EN: Change the company's settings (ADR-018). TR: Firmanın ayarlarını değiştirme (ADR-018).</summary>
    public const string SettingsManage = "settings.manage";

    /// <summary>EN: Every core permission. TR: Tüm çekirdek izinleri.</summary>
    public static IReadOnlyList<string> All { get; } = [SettingsManage];
}

/// <summary>
/// EN: Marks an administrative permission that only the Owner role gets (e.g. changing the plan); Admin gets every other
///     permission. Replaces the core knowing a product permission by name (ADR-026).
/// TR: Sadece Sahip rolünün aldığı bir yönetim iznini işaretler (ör. planı değiştirmek); Yönetici diğer her izni alır. Çekirdeğin bir ürün
///     iznini adıyla tanımasının yerini alır (ADR-026).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class OwnerOnlyAttribute : Attribute;
