namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: Why an entered quantity can't be used.
/// TR: Girilen bir miktarın neden kullanılamadığı.
/// </summary>
public enum QuantityProblem
{
    /// <summary>EN: The quantity is fine. TR: Miktar uygun.</summary>
    None,

    /// <summary>EN: More decimals than the entered unit allows (2.5 BOX). TR: Girilen birimin izin verdiğinden fazla ondalık (2,5 BOX).</summary>
    EnteredUnitPrecision,

    /// <summary>
    /// EN: Converted to the base unit it has more decimals than the base unit allows (1 BAG = 2.5 PCS).
    /// TR: Temel birime çevrilince temel birimin izin verdiğinden fazla ondalığı olur (1 BAG = 2,5 PCS).
    /// </summary>
    BaseUnitPrecision,
}

/// <summary>
/// EN: Converts a quantity entered in any of an item's units to its base unit, once, at the edge (ADR-019, ADR-020).
///     Both precisions are checked, so the balance never holds a quantity its unit can't express — and nothing is ever
///     rounded silently.
/// TR: Bir kalemin herhangi bir biriminde girilen miktarı sınırda, bir kez temel birime çevirir (ADR-019, ADR-020). İki hassasiyet de
///     kontrol edilir; böylece bakiye asla biriminin ifade edemeyeceği bir miktar tutmaz — ve hiçbir şey sessizce yuvarlanmaz.
/// </summary>
public static class UnitConversion
{
    /// <summary>
    /// EN: The quantity in the base unit, or the reason it can't be used.
    /// TR: Temel birimdeki miktar veya kullanılamama nedeni.
    /// </summary>
    /// <param name="quantity">EN: Entered quantity (&gt; 0). TR: Girilen miktar (&gt; 0).</param>
    /// <param name="unit">EN: Entered unit. TR: Girilen birim.</param>
    /// <param name="factor">EN: Base units in one entered unit. TR: Girilen birimin bir tanesindeki temel birim sayısı.</param>
    /// <param name="baseUnit">EN: The item's base unit. TR: Kalemin temel birimi.</param>
    /// <returns>EN: Base quantity and problem. TR: Temel miktar ve sorun.</returns>
    public static (decimal BaseQuantity, QuantityProblem Problem) ToBase(
        decimal quantity, UnitDefinition unit, decimal factor, UnitDefinition baseUnit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        if (!unit.Fits(quantity))
        {
            return (0, QuantityProblem.EnteredUnitPrecision);
        }

        var baseQuantity = quantity * factor;
        return baseUnit.Fits(baseQuantity) ? (baseQuantity, QuantityProblem.None) : (0, QuantityProblem.BaseUnitPrecision);
    }
}
