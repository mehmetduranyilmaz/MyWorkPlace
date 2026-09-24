using MyWorkplace.BuildingBlocks.Settings;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: What a stock issue does when it exceeds the balance (ADR-020).
/// TR: Bakiyeyi aşan bir stok çıkışında ne olacağı (ADR-020).
/// </summary>
public enum NegativeStockPolicy
{
    /// <summary>EN: Refuse the issue (409). TR: Çıkışı reddet (409).</summary>
    Block,

    /// <summary>EN: Allow a negative balance. TR: Eksi bakiyeye izin ver.</summary>
    Allow,

    /// <summary>EN: Allow it and return a warning. TR: İzin ver ve uyarı dön.</summary>
    Warn,
}

/// <summary>
/// EN: Inventory's per-company settings (ADR-018). The initializers are the defaults every new company starts with.
/// TR: Inventory'nin firma bazında ayarları (ADR-018). Başlangıç değerleri her yeni firmanın başladığı varsayılanlardır.
/// </summary>
public sealed class InventorySettings : IModuleSettings
{
    /// <inheritdoc />
    public static string Module => "inventory";

    /// <summary>
    /// EN: Negative stock policy; <see cref="NegativeStockPolicy.Block"/> by default, the safe choice.
    /// TR: Eksi stok politikası; varsayılan olarak güvenli seçim olan <see cref="NegativeStockPolicy.Block"/>.
    /// </summary>
    public NegativeStockPolicy NegativeStockPolicy { get; init; } = NegativeStockPolicy.Block;
}
