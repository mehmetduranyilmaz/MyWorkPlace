using System.ComponentModel.DataAnnotations;

namespace MyWorkplace.BuildingBlocks.Http;

/// <summary>
/// EN: List parameters shared by every module (ADR-016): <c>?page=&amp;pageSize=&amp;search=</c>. Bind with
///     <c>[AsParameters]</c>; out-of-range values are rejected with a 400 before the handler runs.
/// TR: Her modülün paylaştığı liste parametreleri (ADR-016): <c>?page=&amp;pageSize=&amp;search=</c>. <c>[AsParameters]</c> ile
///     bağlanır; aralık dışı değerler handler çalışmadan 400 ile reddedilir.
/// </summary>
public sealed record PageQuery
{
    /// <summary>EN: Page size when none is given. TR: Belirtilmediğinde sayfa boyutu.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>EN: Largest allowed page size, so one request can't pull a whole table. TR: Tek istekle tüm tablo çekilemesin diye izin verilen en büyük sayfa boyutu.</summary>
    public const int MaxPageSize = 100;

    /// <summary>EN: Longest accepted search text. TR: Kabul edilen en uzun arama metni.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>EN: 1-based page number (default 1). TR: 1'den başlayan sayfa numarası (varsayılan 1).</summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    /// <summary>EN: Items per page, 1–100 (default 20). TR: Sayfa başına öğe, 1–100 (varsayılan 20).</summary>
    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    /// <summary>EN: Optional free-text search; each module decides which fields it searches. TR: İsteğe bağlı serbest arama; hangi alanlarda aranacağına her modül karar verir.</summary>
    [MaxLength(SearchMaxLength)]
    public string? Search { get; init; }

    /// <summary>EN: Effective page number. TR: Geçerli sayfa numarası.</summary>
    public int PageNumber => Page ?? 1;

    /// <summary>EN: Effective page size. TR: Geçerli sayfa boyutu.</summary>
    public int Size => PageSize ?? DefaultPageSize;

    /// <summary>EN: Trimmed search text, or null when blank. TR: Kırpılmış arama metni; boşsa null.</summary>
    public string? SearchText => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

/// <summary>
/// EN: One page of a list, in the format every module returns (ADR-016).
/// TR: Bir listenin tek sayfası; her modülün döndürdüğü biçimde (ADR-016).
/// </summary>
/// <typeparam name="T">EN: Item type. TR: Öğe tipi.</typeparam>
/// <param name="Items">EN: Items of this page; empty beyond the last page. TR: Bu sayfanın öğeleri; son sayfanın ötesinde boş.</param>
/// <param name="Page">EN: Page number. TR: Sayfa numarası.</param>
/// <param name="PageSize">EN: Page size. TR: Sayfa boyutu.</param>
/// <param name="TotalCount">EN: Matching items across all pages. TR: Tüm sayfalardaki eşleşen öğe sayısı.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
