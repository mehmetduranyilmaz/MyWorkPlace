using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Domain;

/// <summary>
/// EN: What Inventory has done about one order's stock (T-040).
/// TR: Inventory'nin bir siparişin stoğu için ne yaptığı (T-040).
/// </summary>
public enum OrderStockStatus
{
    /// <summary>EN: Its stock was issued (OrderPlaced). TR: Stoğu çıkıldı (OrderPlaced).</summary>
    Issued,

    /// <summary>EN: Cancelled — returned, or never issued. TR: İptal edildi — geri verildi veya hiç çıkılmadı.</summary>
    Cancelled,
}

/// <summary>
/// EN: One row per order, the meeting point of <c>OrderPlaced</c> and <c>OrderCancelled</c> (ADR-024, T-040). The two
///     events travel on different exchanges, so they may arrive in either order — even at the same moment. Both handlers
///     first claim this row by its key; the database makes the second wait for the first, so they never both act blind.
/// TR: Sipariş başına bir satır; <c>OrderPlaced</c> ve <c>OrderCancelled</c>'ın buluşma noktası (ADR-024, T-040). İki olay farklı
///     exchange'lerden gelir; bu yüzden herhangi bir sırayla — hatta aynı anda — gelebilirler. İki handler da önce bu satırı anahtarıyla
///     talep eder; veritabanı ikincisini birincisini beklemeye zorlar, böylece ikisi asla birbirinden habersiz davranmaz.
/// </summary>
public sealed class OrderStock
{
    /// <summary>EN: The order (primary key). TR: Sipariş (birincil anahtar).</summary>
    public Guid OrderId { get; init; }

    /// <summary>EN: The order's company. TR: Siparişin firması.</summary>
    public Guid TenantId { get; init; }

    /// <summary>EN: Issued or cancelled. TR: Çıkıldı veya iptal edildi.</summary>
    public OrderStockStatus Status { get; init; }

    /// <summary>EN: When the status was set. TR: Durumun ne zaman atandığı.</summary>
    public DateTimeOffset ChangedAt { get; init; }
}

/// <summary>
/// EN: Claims and changes <see cref="OrderStock"/> rows with single atomic statements; used inside the event
///     dispatcher's transaction.
/// TR: <see cref="OrderStock"/> satırlarını tek atomik ifadelerle talep eder ve değiştirir; olay dağıtıcısının transaction'ı içinde kullanılır.
/// </summary>
/// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
/// <param name="time">EN: Clock. TR: Saat.</param>
public sealed class OrderStockClaims(InventoryDbContext db, TimeProvider time)
{
    /// <summary>
    /// EN: Inserts the order's row with <paramref name="status"/> unless it exists. If another transaction is inserting
    ///     the same order, PostgreSQL makes this one wait for it — that is what orders the two handlers. Table and column
    ///     names come from the EF model, never typed by hand (T-048).
    /// TR: Siparişin satırını, yoksa <paramref name="status"/> ile ekler. Başka bir transaction aynı siparişi ekliyorsa PostgreSQL bunu onu
    ///     beklemeye zorlar — iki handler'ı sıraya koyan budur. Tablo ve sütun adları EF modelinden gelir, asla elle yazılmaz (T-048).
    /// </summary>
    /// <param name="orderId">EN: The order. TR: Sipariş.</param>
    /// <param name="tenantId">EN: Its company. TR: Firması.</param>
    /// <param name="status">EN: The status to claim with. TR: Talep edilecek durum.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: True if this call created the row. TR: Satırı bu çağrı oluşturduysa true.</returns>
    public async Task<bool> TryClaimAsync(
        Guid orderId, Guid tenantId, OrderStockStatus status, CancellationToken cancellationToken)
    {
        var entity = db.Model.FindEntityType(typeof(OrderStock))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string Column(string property) => entity.FindProperty(property)!.GetColumnName(table)!;

        var sql =
            $"INSERT INTO \"{table.Name}\" (\"{Column(nameof(OrderStock.OrderId))}\", \"{Column(nameof(OrderStock.TenantId))}\", " +
            $"\"{Column(nameof(OrderStock.Status))}\", \"{Column(nameof(OrderStock.ChangedAt))}\") " +
            $"VALUES ({{0}}, {{1}}, {{2}}, {{3}}) ON CONFLICT (\"{Column(nameof(OrderStock.OrderId))}\") DO NOTHING";

        return await db.Database.ExecuteSqlRawAsync(
            sql, [orderId, tenantId, status.ToString(), time.GetUtcNow()], cancellationToken) == 1;
    }

    /// <summary>
    /// EN: Turns an issued order into a cancelled one, once: of two cancellations only one gets true.
    /// TR: Çıkılmış bir siparişi bir kez iptal edilmişe çevirir: iki iptalden sadece biri true alır.
    /// </summary>
    /// <param name="orderId">EN: The order. TR: Sipariş.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: True if it was issued and is now cancelled. TR: Çıkılmışsa ve şimdi iptal edildiyse true.</returns>
    public async Task<bool> TryCancelIssuedAsync(Guid orderId, CancellationToken cancellationToken) =>
        await db.OrderStocks
            .Where(o => o.OrderId == orderId && o.Status == OrderStockStatus.Issued)
            .ExecuteUpdateAsync(
                set => set.SetProperty(o => o.Status, OrderStockStatus.Cancelled).SetProperty(o => o.ChangedAt, time.GetUtcNow()),
                cancellationToken) == 1;
}
