namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: Unit codes available until the per-tenant unit catalog arrives (T-031, ADR-019).
/// TR: Firma bazlı birim kataloğu gelene kadar (T-031, ADR-019) kullanılabilen birim kodları.
/// </summary>
public static class DefaultUnits
{
    /// <summary>EN: Piece. TR: Adet.</summary>
    public const string Piece = "PCS";

    /// <summary>EN: Kilogram. TR: Kilogram.</summary>
    public const string Kilogram = "KG";

    /// <summary>EN: Litre. TR: Litre.</summary>
    public const string Litre = "L";

    /// <summary>EN: Metre. TR: Metre.</summary>
    public const string Metre = "M";

    /// <summary>EN: Box. TR: Koli.</summary>
    public const string Box = "BOX";

    /// <summary>EN: Pack. TR: Paket.</summary>
    public const string Pack = "PACK";

    /// <summary>EN: Longest unit code. TR: En uzun birim kodu.</summary>
    public const int CodeMaxLength = 10;
}
