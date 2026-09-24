using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Domain;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: A read value together with its row version, ready for an ETag (ADR-017).
/// TR: Satır sürümüyle birlikte okunmuş bir değer; ETag için hazır (ADR-017).
/// </summary>
/// <typeparam name="T">EN: Value type. TR: Değer tipi.</typeparam>
/// <param name="Value">EN: The projected value. TR: Yansıtılmış değer.</param>
/// <param name="Version">EN: Row version (<c>xmin</c>). TR: Satır sürümü (<c>xmin</c>).</param>
public sealed record Versioned<T>(T Value, uint Version);

/// <summary>
/// EN: Single reads that reuse a module's one mapping expression (ADR-021).
/// TR: Bir modülün tek eşleme ifadesini yeniden kullanan tekil okumalar (ADR-021).
/// </summary>
public static class VersionedQueryExtensions
{
    /// <summary>
    /// EN: Loads one entity by id, projected with <paramref name="projection"/>, together with its row version — in one
    ///     untracked query. The projection is the same expression lists use, so single reads and lists can't drift.
    ///     Tenant and soft-delete filters apply: another company's or a deleted row returns null.
    /// TR: Bir entity'yi kimliğiyle, <paramref name="projection"/> ile yansıtılmış halde ve satır sürümüyle birlikte yükler —
    ///     tek bir takipsiz sorguda. Yansıtma listelerin kullandığı ifadenin aynısıdır; böylece tekil okumalar ile listeler
    ///     ayrışamaz. Firma ve soft-delete filtreleri uygulanır: başka firmanın veya silinmiş bir satır null döner.
    /// </summary>
    /// <typeparam name="TEntity">EN: Entity type. TR: Entity tipi.</typeparam>
    /// <typeparam name="TResult">EN: API shape. TR: API biçimi.</typeparam>
    /// <param name="query">EN: Entity set or query. TR: Entity kümesi veya sorgu.</param>
    /// <param name="id">EN: Entity id. TR: Entity kimliği.</param>
    /// <param name="projection">EN: The module's mapping expression. TR: Modülün eşleme ifadesi.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: Value and version, or null. TR: Değer ve sürüm veya null.</returns>
    public static Task<Versioned<TResult>?> SingleWithVersionAsync<TEntity, TResult>(
        this IQueryable<TEntity> query,
        Guid id,
        Expression<Func<TEntity, TResult>> projection,
        CancellationToken cancellationToken = default)
        where TEntity : Entity
    {
        // EN: Build e => new Versioned<TResult>(<projection body>, EF.Property<uint>(e, "Version")) from the given
        //     projection, so EF translates everything into one SQL SELECT.
        // TR: Verilen yansıtmadan e => new Versioned<TResult>(<yansıtma gövdesi>, EF.Property<uint>(e, "Version")) ifadesini
        //     kurar; böylece EF her şeyi tek bir SQL SELECT'e çevirir.
        var entity = projection.Parameters[0];
        var version = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(uint)],
            entity,
            Expression.Constant(ServiceDbContext.ConcurrencyTokenProperty));
        var body = Expression.New(
            typeof(Versioned<TResult>).GetConstructor([typeof(TResult), typeof(uint)])!,
            projection.Body,
            version);
        var selector = Expression.Lambda<Func<TEntity, Versioned<TResult>>>(body, entity);

        return query
            .Where(e => e.Id == id)
            .Select(selector)
            .SingleOrDefaultAsync(cancellationToken)!;
    }
}
