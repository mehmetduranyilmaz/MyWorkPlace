using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Proves the shared persistence conventions (ADR-011) against a real PostgreSQL.
/// TR: Ortak kalıcılık kurallarını (ADR-011) gerçek bir PostgreSQL üzerinde kanıtlar.
/// </summary>
/// <param name="db">EN: Shared database fixture. TR: Paylaşılan veritabanı fixture'ı.</param>
public sealed class PersistenceConventionsTests(PostgreSqlFixture db)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- Tenant isolation

    [Fact]
    public async Task Query_RowsOfAnotherTenant_AreInvisible()
    {
        var owner = TestUser.OfNewTenant();
        var noteId = await AddNoteAsync(owner);

        await using var otherTenant = db.CreateContext(TestUser.OfNewTenant());
        await using var anonymous = db.CreateContext(new TestUser());
        await using var sameTenant = db.CreateContext(owner);

        Assert.False(await otherTenant.Notes.AnyAsync(n => n.Id == noteId, Ct));
        Assert.False(await anonymous.Notes.AnyAsync(n => n.Id == noteId, Ct));
        Assert.True(await sameTenant.Notes.AnyAsync(n => n.Id == noteId, Ct));
    }

    [Fact]
    public async Task Add_WithoutTenantAndAnonymousUser_Throws()
    {
        await using var context = db.CreateContext(new TestUser());
        context.Notes.Add(new TestNote { Title = "orphan" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Add_WithExplicitTenantAndAnonymousUser_UsesThatTenant()
    {
        // EN: The sign-up case: nobody is signed in yet, the new tenant id is given explicitly.
        // TR: Kayıt durumu: henüz kimse giriş yapmamış, yeni firma kimliği açıkça verilir.
        var tenantId = Guid.CreateVersion7();
        await using (var context = db.CreateContext(new TestUser()))
        {
            context.Notes.Add(new TestNote { Title = "first", TenantId = tenantId });
            await context.SaveChangesAsync(Ct);
        }

        await using var tenantContext = db.CreateContext(new TestUser(tenantId));
        Assert.Equal(1, await tenantContext.Notes.CountAsync(Ct));
    }

    [Fact]
    public async Task Update_ChangingTenantId_Throws()
    {
        var owner = TestUser.OfNewTenant();
        var noteId = await AddNoteAsync(owner);

        await using var context = db.CreateContext(owner);
        var note = await context.Notes.FindForUpdateAsync(noteId, Ct);
        note!.TenantId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
    }

    // ---------------------------------------------------------------- Soft delete

    [Fact]
    public async Task Remove_SoftDeletableEntity_HidesRowButKeepsIt()
    {
        var owner = TestUser.OfNewTenant();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var noteId = await AddNoteAsync(owner);

        await using (var context = db.CreateContext(owner, time))
        {
            context.Notes.Remove((await context.Notes.FindForUpdateAsync(noteId, Ct))!);
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext(owner);
        Assert.False(await check.Notes.AnyAsync(n => n.Id == noteId, Ct));

        var kept = await check.Notes.IgnoreQueryFilters([QueryFilters.SoftDelete]).SingleAsync(n => n.Id == noteId, Ct);
        Assert.True(kept.IsDeleted);
        Assert.Equal(time.GetUtcNow(), kept.DeletedAt);
    }

    [Fact]
    public async Task Remove_SoftDeletableOwner_KeepsItsOwnedParts()
    {
        var owner = TestUser.OfNewTenant();
        Guid noteId;
        await using (var context = db.CreateContext(owner))
        {
            var note = new TestNote { Title = "with parts", Items = [new() { Text = "a" }, new() { Text = "b" }] };
            context.Notes.Add(note);
            await context.SaveChangesAsync(Ct);
            noteId = note.Id;
        }

        await using (var context = db.CreateContext(owner))
        {
            context.Notes.Remove((await context.Notes.FindForUpdateAsync(noteId, Ct))!);
            await context.SaveChangesAsync(Ct);
        }

        // EN: The note is kept and so are its parts — the history must not lose an order's lines.
        // TR: Not korunur, parçaları da — geçmiş bir siparişin satırlarını kaybetmemelidir.
        await using var check = db.CreateContext(owner);
        var kept = await check.Notes.IgnoreQueryFilters([QueryFilters.SoftDelete]).SingleAsync(n => n.Id == noteId, Ct);
        Assert.True(kept.IsDeleted);
        Assert.Equal(["a", "b"], kept.Items.Select(i => i.Text).Order());
    }

    // ---------------------------------------------------------------- Audit fields

    [Fact]
    public async Task SaveChanges_FillsCreatedAndUpdatedFields()
    {
        var owner = TestUser.OfNewTenant();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var created = time.GetUtcNow();
        var noteId = await AddNoteAsync(owner, time);

        time.Advance(TimeSpan.FromMinutes(5));
        var editor = owner with { UserId = Guid.CreateVersion7() };
        await using (var context = db.CreateContext(editor, time))
        {
            var note = await context.Notes.FindForUpdateAsync(noteId, Ct);
            note!.Body = "edited";
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext(owner);
        var saved = await check.Notes.SingleAsync(n => n.Id == noteId, Ct);
        Assert.Equal(created, saved.CreatedAt);
        Assert.Equal(owner.UserId, saved.CreatedBy);
        Assert.Equal(time.GetUtcNow(), saved.UpdatedAt);
        Assert.Equal(editor.UserId, saved.UpdatedBy);
    }

    // ---------------------------------------------------------------- Change history

    [Fact]
    public async Task Update_OnlyMarkedPropertiesAreLogged()
    {
        var owner = TestUser.OfNewTenant();
        var noteId = await AddNoteAsync(owner, title: "Old title");

        await using (var context = db.CreateContext(owner))
        {
            var note = await context.Notes.FindForUpdateAsync(noteId, Ct);
            note!.Title = "New title";
            note.Body = "Changed body";
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext(owner);
        var entry = Assert.Single(await check.AuditLog.Where(e => e.EntityId == noteId).ToListAsync(Ct));
        Assert.Equal(nameof(TestNote), entry.EntityType);
        Assert.Equal(nameof(TestNote.Title), entry.Property);
        Assert.Equal("Old title", entry.OldValue);
        Assert.Equal("New title", entry.NewValue);
        Assert.Equal(owner.UserId, entry.ChangedBy);
        Assert.Equal(owner.TenantId, entry.TenantId);
    }

    [Fact]
    public async Task Update_AuditedCollection_IsLoggedByContent()
    {
        var owner = TestUser.OfNewTenant();
        var noteId = await AddNoteAsync(owner);

        // EN: 1) same content in a new array → no row; 2) real change → one readable row.
        // TR: 1) yeni dizide aynı içerik → satır yok; 2) gerçek değişiklik → okunur tek satır.
        await using (var context = db.CreateContext(owner))
        {
            var note = await context.Notes.FindForUpdateAsync(noteId, Ct);
            note!.Tags = [];
            await context.SaveChangesAsync(Ct);
            note.Tags = ["Admin", "Member"];
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext(owner);
        var entry = Assert.Single(await check.AuditLog
            .Where(e => e.EntityId == noteId && e.Property == nameof(TestNote.Tags))
            .ToListAsync(Ct));
        Assert.Equal("", entry.OldValue);
        Assert.Equal("Admin, Member", entry.NewValue);
    }

    // ---------------------------------------------------------------- Concurrency

    [Fact]
    public async Task Update_WithStaleVersion_ThrowsConcurrencyException()
    {
        var owner = TestUser.OfNewTenant();
        var noteId = await AddNoteAsync(owner);

        await using var first = db.CreateContext(owner);
        await using var second = db.CreateContext(owner);
        var firstCopy = await first.Notes.FindForUpdateAsync(noteId, Ct);
        var secondCopy = await second.Notes.FindForUpdateAsync(noteId, Ct);

        firstCopy!.Body = "first wins";
        await first.SaveChangesAsync(Ct);

        secondCopy!.Body = "second is stale";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }

    // ---------------------------------------------------------------- No-tracking reads

    [Fact]
    public async Task Query_ByDefault_IsNotTracked_AndFindForUpdateIs()
    {
        var owner = TestUser.OfNewTenant();
        var noteId = await AddNoteAsync(owner, title: "original");

        await using (var context = db.CreateContext(owner))
        {
            var readOnly = await context.Notes.SingleAsync(n => n.Id == noteId, Ct);
            Assert.Empty(context.ChangeTracker.Entries());

            // EN: The trap ADR-011 warns about: changes to an untracked entity are silently not saved.
            // TR: ADR-011'in uyardığı tuzak: takip edilmeyen entity'deki değişiklikler sessizce kaydedilmez.
            readOnly.Title = "lost";
            Assert.Equal(0, await context.SaveChangesAsync(Ct));

            var tracked = await context.Notes.FindForUpdateAsync(noteId, Ct);
            tracked!.Title = "saved";
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext(owner);
        Assert.Equal("saved", (await check.Notes.SingleAsync(n => n.Id == noteId, Ct)).Title);
    }

    /// <summary>
    /// EN: Inserts a note as <paramref name="user"/> and returns its id.
    /// TR: <paramref name="user"/> adına bir not ekler ve kimliğini döner.
    /// </summary>
    /// <param name="user">EN: Acting user. TR: İşlemi yapan kullanıcı.</param>
    /// <param name="time">EN: Clock. TR: Saat.</param>
    /// <param name="title">EN: Note title. TR: Not başlığı.</param>
    /// <returns>EN: The new note's id. TR: Yeni notun kimliği.</returns>
    private async Task<Guid> AddNoteAsync(TestUser user, TimeProvider? time = null, string title = "note")
    {
        await using var context = db.CreateContext(user, time);
        var note = new TestNote { Title = title, Body = "body" };
        context.Notes.Add(note);
        await context.SaveChangesAsync(Ct);
        return note.Id;
    }
}
