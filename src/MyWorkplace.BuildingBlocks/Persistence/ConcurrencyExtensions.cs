using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Access to the row version (<c>xmin</c>) of tracked entities, for ETags (ADR-017). For untracked reads, project
///     the version in the query instead: <c>EF.Property&lt;uint&gt;(e, ServiceDbContext.ConcurrencyTokenProperty)</c>.
/// TR: ETag'ler için takip edilen entity'lerin satır sürümüne (<c>xmin</c>) erişim (ADR-017). Takipsiz okumalarda sürüm
///     sorgunun içinde yansıtılır: <c>EF.Property&lt;uint&gt;(e, ServiceDbContext.ConcurrencyTokenProperty)</c>.
/// </summary>
public static class ConcurrencyExtensions
{
    /// <summary>
    /// EN: Current row version of a tracked entity; after SaveChanges it is the new version returned by PostgreSQL.
    /// TR: Takip edilen bir entity'nin güncel satır sürümü; SaveChanges'ten sonra PostgreSQL'in döndürdüğü yeni sürümdür.
    /// </summary>
    /// <param name="db">EN: The context tracking the entity. TR: Entity'yi takip eden context.</param>
    /// <param name="entity">EN: The entity. TR: Entity.</param>
    /// <returns>EN: The version. TR: Sürüm.</returns>
    public static uint GetVersion(this DbContext db, Entity entity) =>
        db.Entry(entity).Property<uint>(ServiceDbContext.ConcurrencyTokenProperty).CurrentValue;

    /// <summary>
    /// EN: Makes the next SaveChanges succeed only if the row still has <paramref name="version"/>
    ///     (<c>UPDATE ... WHERE xmin = @version</c>); otherwise it throws <see cref="DbUpdateConcurrencyException"/>.
    ///     This closes the gap between checking the version and saving.
    /// TR: Bir sonraki SaveChanges'in sadece satır hâlâ <paramref name="version"/> sürümündeyse başarılı olmasını sağlar
    ///     (<c>UPDATE ... WHERE xmin = @version</c>); değilse <see cref="DbUpdateConcurrencyException"/> fırlatır.
    ///     Bu, sürüm kontrolü ile kaydetme arasındaki boşluğu kapatır.
    /// </summary>
    /// <param name="db">EN: The context tracking the entity. TR: Entity'yi takip eden context.</param>
    /// <param name="entity">EN: The entity. TR: Entity.</param>
    /// <param name="version">EN: The version the client edited. TR: İstemcinin düzenlediği sürüm.</param>
    public static void ExpectVersion(this DbContext db, Entity entity, uint version) =>
        db.Entry(entity).Property<uint>(ServiceDbContext.ConcurrencyTokenProperty).OriginalValue = version;
}
