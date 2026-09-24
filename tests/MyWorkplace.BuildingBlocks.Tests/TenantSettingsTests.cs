using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Settings;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Mode of <see cref="TestSettings"/>. TR: <see cref="TestSettings"/> modu.
/// </summary>
public enum TestMode
{
    /// <summary>EN: Default. TR: Varsayılan.</summary>
    Strict,

    /// <summary>EN: Alternative. TR: Alternatif.</summary>
    Lenient,
}

/// <summary>
/// EN: A module's settings class as a module would write it.
/// TR: Bir modülün yazacağı şekliyle bir ayar sınıfı.
/// </summary>
public sealed class TestSettings : IModuleSettings
{
    /// <inheritdoc />
    public static string Module => "test";

    /// <summary>EN: An enum setting. TR: Enum bir ayar.</summary>
    public TestMode Mode { get; init; } = TestMode.Strict;

    /// <summary>EN: A number setting. TR: Sayı bir ayar.</summary>
    public int Limit { get; init; } = 10;
}

/// <summary>
/// EN: Tenant settings (ADR-018, T-032) against a real PostgreSQL: defaults, saving, isolation, versions, history.
/// TR: Gerçek bir PostgreSQL üzerinde firma ayarları (ADR-018, T-032): varsayılanlar, kaydetme, izolasyon, sürümler, geçmiş.
/// </summary>
/// <param name="db">EN: Shared database fixture. TR: Paylaşılan veritabanı fixture'ı.</param>
public sealed class TenantSettingsTests(PostgreSqlFixture db)
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_NeverSaved_ReturnsCodeDefaults_AtVersionZero()
    {
        await using var context = db.CreateContext(TestUser.OfNewTenant());

        var current = await new TenantSettings<TestSettings>(context).GetWithVersionAsync(Ct);

        Assert.Equal(TestMode.Strict, current.Value.Mode);
        Assert.Equal(10, current.Value.Limit);
        Assert.Equal(0u, current.Version);
    }

    [Fact]
    public async Task Save_ThenGet_ReturnsTheSavedValues_OnlyForThatTenant()
    {
        var owner = TestUser.OfNewTenant();
        await SaveAsync(owner, new TestSettings { Mode = TestMode.Lenient, Limit = 3 }, expectedVersion: 0);

        await using var sameTenant = db.CreateContext(owner);
        await using var otherTenant = db.CreateContext(TestUser.OfNewTenant());
        var mine = await new TenantSettings<TestSettings>(sameTenant).GetAsync(Ct);
        var theirs = await new TenantSettings<TestSettings>(otherTenant).GetAsync(Ct);

        Assert.Equal((TestMode.Lenient, 3), (mine.Mode, mine.Limit));
        Assert.Equal((TestMode.Strict, 10), (theirs.Mode, theirs.Limit));
    }

    [Fact]
    public async Task Get_StoredDocumentMissingAProperty_UsesThatPropertysDefault()
    {
        // EN: A document saved before "Limit" existed, plus a field that no longer exists.
        // TR: "Limit" yokken kaydedilmiş bir doküman ve artık var olmayan bir alan.
        var owner = TestUser.OfNewTenant();
        await using (var context = db.CreateContext(owner))
        {
            context.TenantSettings.Add(new TenantSetting { Module = "test", Values = """{"mode":"Lenient","removed":1}""" });
            await context.SaveChangesAsync(Ct);
        }

        await using var check = db.CreateContext(owner);
        var settings = await new TenantSettings<TestSettings>(check).GetAsync(Ct);

        Assert.Equal((TestMode.Lenient, 10), (settings.Mode, settings.Limit));
    }

    [Fact]
    public async Task Save_WithStaleVersion_OrSecondFirstSave_IsRefused()
    {
        var owner = TestUser.OfNewTenant();
        var first = await SaveAsync(owner, new TestSettings { Limit = 1 }, expectedVersion: 0);

        var secondFirstSave = await SaveAsync(owner, new TestSettings { Limit = 2 }, expectedVersion: 0);
        var update = await SaveAsync(owner, new TestSettings { Limit = 3 }, expectedVersion: first!.Value);
        var stale = await SaveAsync(owner, new TestSettings { Limit = 4 }, expectedVersion: first.Value);

        Assert.Null(secondFirstSave);
        Assert.NotNull(update);
        Assert.Null(stale);
    }

    [Fact]
    public async Task Save_RecordsChangeHistory_ButNotForTheSameValues()
    {
        var owner = TestUser.OfNewTenant();
        var v1 = await SaveAsync(owner, new TestSettings(), expectedVersion: 0);
        var v2 = await SaveAsync(owner, new TestSettings(), expectedVersion: v1!.Value);
        await SaveAsync(owner, new TestSettings { Mode = TestMode.Lenient }, expectedVersion: v2!.Value);

        await using var check = db.CreateContext(owner);
        var entry = Assert.Single(await check.AuditLog
            .Where(e => e.TenantId == owner.TenantId && e.EntityType == nameof(TenantSetting))
            .ToListAsync(Ct));
        Assert.Equal(v1, v2);
        Assert.Contains("Strict", entry.OldValue);
        Assert.Contains("Lenient", entry.NewValue);
    }

    /// <summary>
    /// EN: Saves settings as <paramref name="user"/> in a fresh context.
    /// TR: Ayarları yeni bir context içinde <paramref name="user"/> adına kaydeder.
    /// </summary>
    /// <param name="user">EN: Acting user. TR: İşlemi yapan kullanıcı.</param>
    /// <param name="values">EN: Values. TR: Değerler.</param>
    /// <param name="expectedVersion">EN: Expected version. TR: Beklenen sürüm.</param>
    /// <returns>EN: New version or null. TR: Yeni sürüm veya null.</returns>
    private async Task<uint?> SaveAsync(TestUser user, TestSettings values, uint expectedVersion)
    {
        await using var context = db.CreateContext(user);
        return await new TenantSettings<TestSettings>(context).SaveAsync(values, expectedVersion, Ct);
    }
}
