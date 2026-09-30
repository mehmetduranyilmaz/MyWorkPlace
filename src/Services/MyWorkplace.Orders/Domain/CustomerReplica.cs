using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MyWorkplace.BuildingBlocks.Domain;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Domain;

/// <summary>
/// EN: Orders' copy of a customer, fed by Customers' events (T-039, ADR-024): enough to check that a draft names a live
///     customer of the company and to take its name — without ever calling Customers (ADR-007). The id is the customer's.
///     <see cref="Removed"/> is deliberately not the soft-delete flag: the replica must still see a removed customer, so a
///     late creation can't bring it back.
/// TR: Orders'ın bir müşterinin kopyası; Customers'ın olaylarıyla beslenir (T-039, ADR-024): bir taslağın firmanın canlı bir müşterisini
///     gösterdiğini kontrol etmeye ve adını almaya yeter — Customers'ı hiç çağırmadan (ADR-007). Kimlik müşterinindir. <see cref="Removed"/>
///     bilerek soft-delete işareti değildir: kopya silinmiş bir müşteriyi yine görmelidir; böylece geç gelen bir oluşturma onu geri getiremez.
/// </summary>
public sealed class CustomerReplica : TenantOwnedEntity
{
    /// <summary>EN: Max length of <see cref="Name"/>. TR: <see cref="Name"/> için en fazla uzunluk.</summary>
    public const int NameMaxLength = 200;

    /// <summary>EN: The customer's name. TR: Müşterinin adı.</summary>
    public string Name { get; init; } = "";

    /// <summary>EN: The customer was deleted. TR: Müşteri silindi.</summary>
    public bool Removed { get; init; }

    /// <summary>EN: Time of the last change applied. TR: Uygulanan son değişikliğin zamanı.</summary>
    public DateTimeOffset ChangedAt { get; init; }
}

/// <summary>
/// EN: Applies customer changes to the replica with one atomic upsert: insert if new, update only if newer. Events may
///     arrive in any order or at once; whichever comes, the replica ends at the newest change (last write wins).
/// TR: Müşteri değişikliklerini kopyaya tek atomik bir upsert ile uygular: yeniyse ekle, sadece daha yeniyse güncelle. Olaylar herhangi bir
///     sırayla veya aynı anda gelebilir; hangisi gelirse gelsin kopya en yeni değişiklikte biter (son yazan kazanır).
/// </summary>
/// <param name="db">EN: Orders database. TR: Orders veritabanı.</param>
/// <param name="time">EN: Clock. TR: Saat.</param>
public sealed class CustomerReplicas(OrdersDbContext db, TimeProvider time)
{
    /// <summary>
    /// EN: Records a customer's state at <paramref name="changedAt"/>, unless a newer one is already there. A removal keeps
    ///     the last known name. Table and column names come from the EF model, never typed by hand (T-048).
    /// TR: Bir müşterinin <paramref name="changedAt"/> anındaki durumunu kaydeder; daha yenisi zaten orada değilse. Silme son bilinen adı
    ///     korur. Tablo ve sütun adları EF modelinden gelir, asla elle yazılmaz (T-048).
    /// </summary>
    /// <param name="tenantId">EN: The company. TR: Firma.</param>
    /// <param name="customerId">EN: The customer. TR: Müşteri.</param>
    /// <param name="name">EN: Name, or null for a removal. TR: Ad; silmede null.</param>
    /// <param name="changedAt">EN: When it changed. TR: Ne zaman değiştiği.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    public Task ApplyAsync(
        Guid tenantId, Guid customerId, string? name, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        var entity = db.Model.FindEntityType(typeof(CustomerReplica))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string C(string property) => $"\"{entity.FindProperty(property)!.GetColumnName(table)}\"";
        var id = C(nameof(CustomerReplica.Id));
        var tenant = C(nameof(CustomerReplica.TenantId));
        var nameColumn = C(nameof(CustomerReplica.Name));
        var removed = C(nameof(CustomerReplica.Removed));
        var changed = C(nameof(CustomerReplica.ChangedAt));
        var created = C(nameof(CustomerReplica.CreatedAt));

        var sql =
            $"INSERT INTO \"{table.Name}\" AS r ({id}, {tenant}, {nameColumn}, {removed}, {changed}, {created}) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}) " +
            $"ON CONFLICT ({id}) DO UPDATE SET " +
            $"{nameColumn} = CASE WHEN EXCLUDED.{removed} THEN r.{nameColumn} ELSE EXCLUDED.{nameColumn} END, " +
            $"{removed} = EXCLUDED.{removed}, {changed} = EXCLUDED.{changed} " +
            $"WHERE r.{changed} < EXCLUDED.{changed}";

        return db.Database.ExecuteSqlRawAsync(
            sql, [customerId, tenantId, name ?? "", name is null, changedAt, time.GetUtcNow()], cancellationToken);
    }
}
