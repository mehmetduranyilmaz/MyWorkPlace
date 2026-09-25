namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: An alternative unit as entered.
/// TR: Girildiği haliyle bir alternatif birim.
/// </summary>
/// <param name="Unit">EN: Unit code. TR: Birim kodu.</param>
/// <param name="Factor">EN: How many base units one of this unit is. TR: Bu birimin bir tanesinin kaç temel birim olduğu.</param>
public sealed record StockItemUnitValues(string Unit, decimal Factor);

/// <summary>
/// EN: An alternative unit of a stock item, stored in <c>stock_item_units</c> and saved with the item (ADR-019):
///     one <see cref="UnitCode"/> is <see cref="Factor"/> base units, e.g. 1 <c>BOX</c> = 24 <c>PCS</c>. A movement in
///     this unit is converted to the base unit once, at the edge (T-030).
/// TR: Bir stok kaleminin alternatif birimi; <c>stock_item_units</c> içinde saklanır ve kalemle birlikte kaydedilir (ADR-019):
///     bir <see cref="UnitCode"/>, <see cref="Factor"/> temel birimdir; ör. 1 <c>BOX</c> = 24 <c>PCS</c>. Bu birimdeki bir hareket
///     sınırda, bir kez temel birime çevrilir (T-030).
/// </summary>
public sealed class StockItemUnit
{
    /// <summary>EN: Row id. TR: Satır kimliği.</summary>
    public Guid Id { get; private init; }

    /// <summary>EN: Upper-case unit code. TR: Büyük harfli birim kodu.</summary>
    public string UnitCode { get; private init; } = "";

    /// <summary>EN: Base units in one of this unit. TR: Bu birimin bir tanesindeki temel birim sayısı.</summary>
    public decimal Factor { get; private init; }

    /// <summary>
    /// EN: Creates the row from checked values (see <see cref="StockItem.Update"/>).
    /// TR: Kontrol edilmiş değerlerden satırı oluşturur (bkz. <see cref="StockItem.Update"/>).
    /// </summary>
    /// <param name="values">EN: Normalized values. TR: Normalleştirilmiş değerler.</param>
    /// <returns>EN: The row. TR: Satır.</returns>
    internal static StockItemUnit Create(StockItemUnitValues values) =>
        new() { Id = Guid.CreateVersion7(), UnitCode = values.Unit, Factor = values.Factor };
}
