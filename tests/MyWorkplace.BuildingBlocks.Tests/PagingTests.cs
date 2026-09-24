using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: The shared paging and search helpers (T-028) against a real PostgreSQL.
/// TR: Ortak sayfalama ve arama yardımcıları (T-028), gerçek bir PostgreSQL üzerinde.
/// </summary>
/// <param name="db">EN: Shared database fixture. TR: Paylaşılan veritabanı fixture'ı.</param>
public sealed class PagingTests(PostgreSqlFixture db)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(3, 10, 5)]
    [InlineData(4, 10, 0)]
    public async Task Page_ReturnsTheRightSliceAndTotal(int page, int pageSize, int expectedItems)
    {
        var owner = TestUser.OfNewTenant();
        await AddNotesAsync(owner, Enumerable.Range(1, 25).Select(i => $"Note {i:D2}"));

        var result = await PageAsync(owner, new PageQuery { Page = page, PageSize = pageSize });

        Assert.Equal(expectedItems, result.Items.Count);
        Assert.Equal(25, result.TotalCount);
        if (expectedItems > 0)
        {
            Assert.Equal($"Note {((page - 1) * pageSize) + 1:D2}", result.Items[0]);
        }
    }

    [Fact]
    public async Task Pages_WithEqualSortValues_NeitherRepeatNorLoseRows()
    {
        // EN: Every title is the same, so only the id tie-breaker makes the order stable.
        // TR: Tüm başlıklar aynı; sırayı kararlı yapan sadece kimlik (tie-breaker).
        var owner = TestUser.OfNewTenant();
        var ids = await AddNotesAsync(owner, Enumerable.Repeat("Same", 12));

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            await using var context = db.CreateContext(owner);
            var result = await context.Notes
                .OrderBy(n => n.Title)
                .ThenBy(n => n.Id)
                .ToPagedResultAsync(n => n.Id, new PageQuery { Page = page, PageSize = 5 }, Ct);
            seen.AddRange(result.Items);
        }

        Assert.Equal(ids.Order(), seen.Order());
    }

    [Fact]
    public async Task HugePageNumber_IsEmpty_NotAnOverflow()
    {
        var owner = TestUser.OfNewTenant();
        await AddNotesAsync(owner, ["Only"]);

        var result = await PageAsync(owner, new PageQuery { Page = int.MaxValue, PageSize = 100 });

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Theory]
    [InlineData("100%", "100% cotton")]
    [InlineData("a_b", "a_b")]
    [InlineData("C:\\temp", "C:\\temp")]
    public async Task SearchPattern_MatchesWildcardCharactersLiterally(string search, string expectedMatch)
    {
        var owner = TestUser.OfNewTenant();
        await AddNotesAsync(owner, ["100% cotton", "100 cotton", "a_b", "axb", "C:\\temp", "C:temp"]);
        var pattern = SearchPattern.Contains(search);

        await using var context = db.CreateContext(owner);
        var matches = await context.Notes
            .Where(n => EF.Functions.ILike(n.Title, pattern, SearchPattern.EscapeCharacter))
            .Select(n => n.Title)
            .ToListAsync(Ct);

        Assert.Equal([expectedMatch], matches);
    }

    /// <summary>
    /// EN: Inserts notes with the given titles for <paramref name="owner"/>.
    /// TR: <paramref name="owner"/> için verilen başlıklarla notlar ekler.
    /// </summary>
    /// <param name="owner">EN: Owning user. TR: Sahip kullanıcı.</param>
    /// <param name="titles">EN: Titles. TR: Başlıklar.</param>
    /// <returns>EN: The new ids. TR: Yeni kimlikler.</returns>
    private async Task<List<Guid>> AddNotesAsync(TestUser owner, IEnumerable<string> titles)
    {
        await using var context = db.CreateContext(owner);
        var notes = titles.Select(title => new TestNote { Title = title }).ToList();
        context.Notes.AddRange(notes);
        await context.SaveChangesAsync(Ct);
        return [.. notes.Select(n => n.Id)];
    }

    /// <summary>
    /// EN: Pages the owner's notes by title, returning titles.
    /// TR: Sahibin notlarını başlığa göre sayfalar ve başlıkları döner.
    /// </summary>
    /// <param name="owner">EN: Owning user. TR: Sahip kullanıcı.</param>
    /// <param name="page">EN: Page parameters. TR: Sayfa parametreleri.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    private async Task<PagedResult<string>> PageAsync(TestUser owner, PageQuery page)
    {
        await using var context = db.CreateContext(owner);
        return await context.Notes
            .OrderBy(n => n.Title)
            .ThenBy(n => n.Id)
            .ToPagedResultAsync(n => n.Title, page, Ct);
    }
}
