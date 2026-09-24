using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Persistence;
using MyWorkplace.Identity.Tokens;
using Testcontainers.PostgreSql;

namespace MyWorkplace.Identity.Tests;

/// <summary>
/// EN: Proves that the signing key survives a restart (ADR-012), against a real PostgreSQL with the real migrations.
/// TR: İmzalama anahtarının yeniden başlatmadan etkilenmediğini, gerçek migration'larla gerçek bir PostgreSQL üzerinde kanıtlar (ADR-012).
/// </summary>
public sealed class SigningKeyProviderTests : IAsyncLifetime
{
    /// <summary>
    /// EN: Throw-away database, on exactly the version the AppHost runs (eng/PostgresImage.cs).
    /// TR: Geçici veritabanı; AppHost'un çalıştırdığı sürümün birebir aynısı (eng/PostgresImage.cs).
    /// </summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage.Reference).Build();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var services = BuildServices();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    [Fact]
    public async Task Initialize_AfterRestart_ReusesTheStoredKey()
    {
        var ct = TestContext.Current.CancellationToken;

        // EN: Each ServiceProvider plays one run of the service: "first start", then "after restart".
        // TR: Her ServiceProvider servisin bir çalıştırmasını temsil eder: "ilk açılış", sonra "yeniden başlatma sonrası".
        await using var firstRun = BuildServices();
        var first = firstRun.GetRequiredService<SigningKeyProvider>();
        await first.InitializeAsync(ct);

        await using var secondRun = BuildServices();
        var second = secondRun.GetRequiredService<SigningKeyProvider>();
        await second.InitializeAsync(ct);

        Assert.False(string.IsNullOrEmpty(first.Current.KeyId));
        Assert.Equal(first.Current.KeyId, second.Current.KeyId);

        await using var scope = secondRun.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(1, await db.SigningKeys.CountAsync(ct));
    }

    /// <summary>
    /// EN: Wires the Identity data layer exactly like the service does, against the test container.
    /// TR: Identity veri katmanını servisin yaptığı gibi, test konteynerine karşı bağlar.
    /// </summary>
    /// <returns>EN: A new service provider. TR: Yeni bir service provider.</returns>
    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddBuildingBlocksPersistence();
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            options.UseServiceConventions(
                _container.GetConnectionString(),
                provider.GetRequiredService<AuditingInterceptor>(),
                provider.GetRequiredService<ChangeHistoryInterceptor>()));
        services.AddSingleton<SigningKeyProvider>();
        return services.BuildServiceProvider();
    }
}
