using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.BuildingBlocks.Tests;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Starts one throw-away PostgreSQL container for the whole test run and creates the test schema.
///     Tests isolate themselves by using fresh tenant ids, so they can share the database.
/// TR: Tüm test çalıştırması için tek bir geçici PostgreSQL konteyneri başlatır ve test şemasını oluşturur.
///     Testler her seferinde yeni firma kimlikleri kullanarak birbirinden ayrılır; bu yüzden veritabanını paylaşabilir.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    /// <summary>
    /// EN: The PostgreSQL container (same major version the AppHost uses).
    /// TR: PostgreSQL konteyneri (AppHost'un kullandığı ana sürümle aynı).
    /// </summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>
    /// EN: Creates a context acting as <paramref name="user"/>, configured exactly like the services' contexts.
    /// TR: <paramref name="user"/> adına çalışan, servislerin context'leriyle birebir aynı yapılandırılmış bir context oluşturur.
    /// </summary>
    /// <param name="user">EN: Acting user. TR: İşlemi yapan kullanıcı.</param>
    /// <param name="time">EN: Clock; the system clock when null. TR: Saat; null ise sistem saati.</param>
    /// <returns>EN: A new context. TR: Yeni bir context.</returns>
    public TestDbContext CreateContext(ICurrentUser user, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var options = new DbContextOptionsBuilder<TestDbContext>();
        options.UseServiceConventions(
            _container.GetConnectionString(),
            new AuditingInterceptor(user, time),
            new ChangeHistoryInterceptor(user, time));
        return new TestDbContext(options.Options, user);
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext(new TestUser());
        await context.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
