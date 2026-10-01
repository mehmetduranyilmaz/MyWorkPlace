using Microsoft.EntityFrameworkCore;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Tests;

/// <summary>
/// EN: One fresh company on the test database, with the steps handler tests share (T-046): create an item with a
///     balance, run work the way the event dispatcher does, read the balance and the movements.
/// TR: Test veritabanında yeni bir firma ve handler testlerinin paylaştığı adımlar (T-046): bakiyeli bir kalem oluşturmak, işi olay
///     dağıtıcısının yaptığı gibi çalıştırmak, bakiyeyi ve hareketleri okumak.
/// </summary>
/// <param name="database">EN: The test database. TR: Test veritabanı.</param>
internal sealed class InventoryData(InventoryDatabase database)
{
    /// <summary>EN: The company these steps act for. TR: Bu adımların adına çalıştığı firma.</summary>
    public Guid Tenant { get; } = Guid.CreateVersion7();

    /// <summary>
    /// EN: A new context acting for <see cref="Tenant"/>.
    /// TR: <see cref="Tenant"/> adına çalışan yeni bir context.
    /// </summary>
    /// <returns>EN: The context. TR: Context.</returns>
    public InventoryDbContext Context() => database.Create(Tenant);

    /// <summary>
    /// EN: Creates a stock item (base unit PCS) and, if the balance isn't zero, receives it as a manual movement.
    /// TR: Bir stok kalemi (temel birim PCS) oluşturur ve bakiye sıfır değilse onu elle bir giriş hareketi olarak alır.
    /// </summary>
    /// <param name="sku">EN: SKU (also the name). TR: SKU (aynı zamanda ad).</param>
    /// <param name="balance">EN: Starting balance. TR: Başlangıç bakiyesi.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Item id. TR: Kalem kimliği.</returns>
    public async Task<Guid> CreateItemAsync(string sku, decimal balance, CancellationToken ct)
    {
        var item = new StockItem();
        item.Update(sku, sku, SystemUnits.Piece, []);
        await using (var db = Context())
        {
            db.StockItems.Add(item);
            await db.SaveChangesAsync(ct);
        }

        if (balance != 0)
        {
            await InTransactionAsync(
                db => new StockLedger(db).RecordManualAsync(item.Id, Receipt(balance), NegativeStockPolicy.Block, ct),
                ct);
        }

        return item.Id;
    }

    /// <summary>
    /// EN: Runs a unit of work in its own context and transaction, then saves and commits — as the dispatcher does.
    /// TR: Bir iş birimini kendi context'i ve transaction'ında çalıştırır, sonra kaydeder ve onaylar — dağıtıcının yaptığı gibi.
    /// </summary>
    /// <param name="work">EN: The work. TR: İş.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public async Task InTransactionAsync(Func<InventoryDbContext, Task> work, CancellationToken ct)
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await work(db);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// EN: An item's balance.
    /// TR: Bir kalemin bakiyesi.
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The balance. TR: Bakiye.</returns>
    public async Task<decimal> BalanceAsync(Guid itemId, CancellationToken ct)
    {
        await using var db = Context();
        return await db.StockItems.Where(i => i.Id == itemId).Select(i => i.Quantity).SingleAsync(ct);
    }

    /// <summary>
    /// EN: An item's movements, oldest first.
    /// TR: Bir kalemin hareketleri, en eski önce.
    /// </summary>
    /// <param name="itemId">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The movements. TR: Hareketler.</returns>
    public async Task<List<StockMovement>> MovementsAsync(Guid itemId, CancellationToken ct)
    {
        await using var db = Context();
        return await db.StockMovements.Where(m => m.StockItemId == itemId).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(ct);
    }

    /// <summary>
    /// EN: A checked manual receipt in pieces.
    /// TR: Adet cinsinden kontrol edilmiş elle bir giriş.
    /// </summary>
    /// <param name="quantity">EN: Quantity. TR: Miktar.</param>
    /// <returns>EN: The movement. TR: Hareket.</returns>
    public static ManualMovement Receipt(decimal quantity) =>
        new(StockMovementType.In, quantity, SystemUnits.Piece, 1m, quantity, null);

    /// <summary>
    /// EN: A checked manual issue in pieces.
    /// TR: Adet cinsinden kontrol edilmiş elle bir çıkış.
    /// </summary>
    /// <param name="quantity">EN: Quantity. TR: Miktar.</param>
    /// <returns>EN: The movement. TR: Hareket.</returns>
    public static ManualMovement Issue(decimal quantity) =>
        new(StockMovementType.Out, quantity, SystemUnits.Piece, 1m, quantity, null);
}
