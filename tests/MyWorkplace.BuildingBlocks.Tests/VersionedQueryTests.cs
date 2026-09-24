using System.Linq.Expressions;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: SingleWithVersionAsync (ADR-021): one module mapping reused for single reads, with the ETag version, in one query.
/// TR: SingleWithVersionAsync (ADR-021): tek modül eşlemesi, ETag sürümüyle birlikte tek sorguda tekil okumalarda kullanılır.
/// </summary>
/// <param name="db">EN: Shared database fixture. TR: Paylaşılan veritabanı fixture'ı.</param>
public sealed class VersionedQueryTests(PostgreSqlFixture db)
{
    /// <summary>EN: A module-style mapping expression. TR: Modül tarzı bir eşleme ifadesi.</summary>
    private static readonly Expression<Func<TestNote, string>> _projection = n => n.Title + " / " + n.Body;

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExistingRow_ReturnsProjectionAndCurrentVersion()
    {
        var owner = TestUser.OfNewTenant();
        var id = await AddNoteAsync(owner);

        await using var context = db.CreateContext(owner);
        var result = await context.Notes.SingleWithVersionAsync(id, _projection, Ct);
        var tracked = await context.Notes.FindForUpdateAsync(id, Ct);

        Assert.NotNull(result);
        Assert.Equal("Title / Body", result.Value);
        Assert.Equal(context.GetVersion(tracked!), result.Version);
        Assert.NotEqual(0u, result.Version);
    }

    [Fact]
    public async Task UnknownId_ReturnsNull()
    {
        await using var context = db.CreateContext(TestUser.OfNewTenant());

        Assert.Null(await context.Notes.SingleWithVersionAsync(Guid.CreateVersion7(), _projection, Ct));
    }

    [Fact]
    public async Task AnotherTenantsRow_ReturnsNull()
    {
        var id = await AddNoteAsync(TestUser.OfNewTenant());

        await using var stranger = db.CreateContext(TestUser.OfNewTenant());

        Assert.Null(await stranger.Notes.SingleWithVersionAsync(id, _projection, Ct));
    }

    [Fact]
    public async Task SoftDeletedRow_ReturnsNull()
    {
        var owner = TestUser.OfNewTenant();
        var id = await AddNoteAsync(owner);
        await using (var context = db.CreateContext(owner))
        {
            context.Notes.Remove((await context.Notes.FindForUpdateAsync(id, Ct))!);
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext(owner);

        Assert.Null(await check.Notes.SingleWithVersionAsync(id, _projection, Ct));
    }

    /// <summary>
    /// EN: Inserts one note for <paramref name="owner"/> and returns its id.
    /// TR: <paramref name="owner"/> için bir not ekler ve kimliğini döner.
    /// </summary>
    /// <param name="owner">EN: Owning user. TR: Sahip kullanıcı.</param>
    /// <returns>EN: The id. TR: Kimlik.</returns>
    private async Task<Guid> AddNoteAsync(TestUser owner)
    {
        await using var context = db.CreateContext(owner);
        var note = new TestNote { Title = "Title", Body = "Body" };
        context.Notes.Add(note);
        await context.SaveChangesAsync(Ct);
        return note.Id;
    }
}
