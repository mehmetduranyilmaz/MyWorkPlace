using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: Offset paging for list endpoints (ADR-016).
/// TR: Liste uç noktaları için offset sayfalama (ADR-016).
/// </summary>
public static class PagingExtensions
{
    /// <summary>
    /// EN: Counts all matches and loads one page, projected to <typeparamref name="TResult"/>. The query must be
    ///     <b>ordered</b> — the parameter type enforces it at compile time — and the order should end with a unique key
    ///     (e.g. <c>.ThenBy(x =&gt; x.Id)</c>), otherwise rows with equal sort values can repeat or vanish across pages.
    /// TR: Tüm eşleşmeleri sayar ve tek bir sayfayı <typeparamref name="TResult"/> tipine yansıtarak yükler. Sorgu
    ///     <b>sıralı</b> olmalıdır — parametre tipi bunu derleme anında zorunlu kılar — ve sıralama benzersiz bir anahtarla
    ///     bitmelidir (ör. <c>.ThenBy(x =&gt; x.Id)</c>); aksi halde sıralama değeri eşit satırlar sayfalar arasında
    ///     tekrar edebilir veya kaybolabilir.
    /// </summary>
    /// <typeparam name="TSource">EN: Entity type. TR: Entity tipi.</typeparam>
    /// <typeparam name="TResult">EN: Item type returned by the API. TR: API'nin döndürdüğü öğe tipi.</typeparam>
    /// <param name="query">EN: Filtered and ordered query. TR: Filtrelenmiş ve sıralanmış sorgu.</param>
    /// <param name="selector">EN: Projection to the API shape. TR: API biçimine yansıtma.</param>
    /// <param name="page">EN: Page parameters. TR: Sayfa parametreleri.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IOrderedQueryable<TSource> query,
        Expression<Func<TSource, TResult>> selector,
        PageQuery page,
        CancellationToken cancellationToken = default)
    {
        var totalCount = await query.CountAsync(cancellationToken);

        // EN: A huge page number would overflow the offset; such a page is simply beyond the end.
        // TR: Çok büyük bir sayfa numarası offset'i taşırır; böyle bir sayfa zaten sonun ötesindedir.
        var offset = (long)(page.PageNumber - 1) * page.Size;
        if (offset >= totalCount)
        {
            return new PagedResult<TResult>([], page.PageNumber, page.Size, totalCount);
        }

        var items = await query
            .Skip((int)offset)
            .Take(page.Size)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, page.PageNumber, page.Size, totalCount);
    }
}
