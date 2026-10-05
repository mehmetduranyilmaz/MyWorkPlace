using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Persistence;
using MyWorkplace.Identity.Tokens;
using Testcontainers.PostgreSql;

namespace MyWorkplace.Identity.Tests;

/// <summary>
/// EN: Signing keys on a real PostgreSQL with the real migrations (ADR-012, ADR-032, T-066): they survive a restart, are
///     stored encrypted, rotate on schedule with a fake clock, refuse a wrong master secret, retire a plaintext key, and two
///     instances starting together create one key.
/// TR: Gerçek migration'larla gerçek bir PostgreSQL üzerinde imzalama anahtarları (ADR-012, ADR-032, T-066): yeniden başlatmadan etkilenmezler,
///     şifreli saklanırlar, sahte bir saatle takvimlerine göre değişirler, yanlış bir ana sırrı reddederler, düz bir anahtarı emekliye ayırırlar ve
///     aynı anda başlayan iki örnek tek bir anahtar üretir.
/// </summary>
public sealed class SigningKeyProviderTests : IAsyncLifetime
{
    /// <summary>EN: The master secret of these tests. TR: Bu testlerin ana sırrı.</summary>
    private const string Secret = "test-master-secret-0123456789abcdefghijklmnop";

    /// <summary>
    /// EN: Throw-away database, on exactly the version the AppHost runs (eng/PostgresImage.cs).
    /// TR: Geçici veritabanı; AppHost'un çalıştırdığı sürümün birebir aynısı (eng/PostgresImage.cs).
    /// </summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage.Reference).Build();

    /// <summary>EN: The shared fake clock. TR: Ortak sahte saat.</summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        // EN: Each ServiceProvider plays one run of the service: "first start", then "after restart".
        // TR: Her ServiceProvider servisin bir çalıştırmasını temsil eder: "ilk açılış", sonra "yeniden başlatma sonrası".
        await using var firstRun = BuildServices();
        var first = await StartAsync(firstRun);
        await using var secondRun = BuildServices();
        var second = await StartAsync(secondRun);

        Assert.False(string.IsNullOrEmpty(first.Current.KeyId));
        Assert.Equal(first.Current.KeyId, second.Current.KeyId);
        Assert.Equal(1, await CountAsync(secondRun));
    }

    [Fact]
    public async Task StoredKey_IsEncrypted_AndCantBeReadWithoutTheMasterSecret()
    {
        await using var run = BuildServices();
        var keys = await StartAsync(run);

        await using var scope = run.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SigningKeys.SingleAsync(Ct);
        using var attempt = RSA.Create();

        Assert.Null(stored.LegacyPrivateKey);
        Assert.ThrowsAny<CryptographicException>(() => attempt.ImportPkcs8PrivateKey(stored.EncryptedPrivateKey, out _));
        attempt.ImportPkcs8PrivateKey(run.GetRequiredService<ISigningKeyProtector>().Unprotect(stored.KeyId, stored.EncryptedPrivateKey), out _);
        Assert.Equal(
            keys.Current.Rsa.ExportParameters(includePrivateParameters: false).Modulus,
            attempt.ExportParameters(includePrivateParameters: false).Modulus);
    }

    [Fact]
    public async Task WrongMasterSecret_StopsStartup_AndChangesNothing()
    {
        await using var firstRun = BuildServices();
        var first = await StartAsync(firstRun);

        await using var wrongRun = BuildServices(secret: "another-master-secret-0123456789abcdefghijk");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrongRun.GetRequiredService<SigningKeyProvider>().InitializeAsync(Ct));

        Assert.Contains("SigningKeys:MasterSecret", error.Message);
        Assert.Equal(1, await CountAsync(firstRun));
        Assert.Equal(first.Current.KeyId, (await StartAsync(firstRun)).Current.KeyId);
    }

    [Fact]
    public async Task Rotation_PublishesTheNewKeyAhead_ThenSigns_ThenDropsTheOldOne()
    {
        await using var run = BuildServices();
        var keys = await StartAsync(run);
        var first = keys.Current.KeyId;

        _time.Advance(TimeSpan.FromDays(89));
        await keys.RefreshAsync(Ct);
        var published = keys.All.Select(k => k.KeyId).ToList();
        var signerWhilePublished = keys.Current.KeyId;

        _time.Advance(TimeSpan.FromDays(1));
        var signerAtActivation = keys.Current.KeyId;

        _time.Advance(TimeSpan.FromHours(24));
        await keys.RefreshAsync(Ct);

        Assert.Equal(2, published.Count);
        Assert.Equal(first, signerWhilePublished);
        Assert.NotEqual(first, signerAtActivation);
        Assert.Contains(signerAtActivation, published);
        Assert.Equal([signerAtActivation], keys.All.Select(k => k.KeyId));
        Assert.Equal(1, await CountAsync(run));
    }

    [Fact]
    public async Task PlaintextKeyFromBeforeAdr032_IsRetired_VerifiesFor24Hours_ThenIsDeleted()
    {
        using var legacyRsa = RSA.Create(2048);
        await using var run = BuildServices();
        await using (var scope = run.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO signing_keys (id, key_id, algorithm, private_key, public_key, activates_at, created_at)
                VALUES ({Guid.CreateVersion7()}, 'legacy', 'RS256', {legacyRsa.ExportPkcs8PrivateKey()}, {Array.Empty<byte>()},
                        {_time.GetUtcNow().AddDays(-10)}, {_time.GetUtcNow().AddDays(-10)})
                """,
                Ct);
        }

        var keys = await StartAsync(run);
        var tokenOfTheLegacyKey = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "test",
            Expires = _time.GetUtcNow().UtcDateTime.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(legacyRsa) { KeyId = "legacy" }, SecurityAlgorithms.RsaSha256),
        });
        var verified = await new JsonWebTokenHandler().ValidateTokenAsync(tokenOfTheLegacyKey, new TokenValidationParameters
        {
            ValidIssuer = "test",
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKeys = keys.All,
        });
        await using (var scope = run.CreateAsyncScope())
        {
            var legacy = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SigningKeys
                .SingleAsync(k => k.KeyId == "legacy", Ct);
            Assert.Null(legacy.LegacyPrivateKey);
            Assert.NotEmpty(legacy.PublicKey);
        }

        _time.Advance(TimeSpan.FromHours(24));
        await keys.RefreshAsync(Ct);

        Assert.True(verified.IsValid, verified.Exception?.Message);
        Assert.NotEqual("legacy", keys.Current.KeyId);
        Assert.DoesNotContain("legacy", keys.All.Select(k => k.KeyId));
        Assert.Equal(1, await CountAsync(run));
    }

    [Fact]
    public async Task TwoInstancesStartingTogether_CreateOneKey()
    {
        await using var first = BuildServices();
        await using var second = BuildServices();

        var started = await Task.WhenAll(
            Task.Run(() => StartAsync(first)), Task.Run(() => StartAsync(second)));

        Assert.Equal(started[0].Current.KeyId, started[1].Current.KeyId);
        Assert.Equal(1, await CountAsync(first));
    }

    /// <summary>
    /// EN: Starts one "run" of the key provider.
    /// TR: Anahtar sağlayıcısının bir "çalıştırmasını" başlatır.
    /// </summary>
    /// <param name="run">EN: The run's services. TR: Çalıştırmanın servisleri.</param>
    /// <returns>EN: The started provider. TR: Başlatılmış sağlayıcı.</returns>
    private static async Task<SigningKeyProvider> StartAsync(ServiceProvider run)
    {
        var keys = run.GetRequiredService<SigningKeyProvider>();
        await keys.InitializeAsync(Ct);
        return keys;
    }

    /// <summary>
    /// EN: Number of stored keys.
    /// TR: Saklanan anahtar sayısı.
    /// </summary>
    /// <param name="run">EN: Any run's services. TR: Herhangi bir çalıştırmanın servisleri.</param>
    /// <returns>EN: The count. TR: Sayı.</returns>
    private static async Task<int> CountAsync(ServiceProvider run)
    {
        await using var scope = run.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SigningKeys.CountAsync(Ct);
    }

    /// <summary>
    /// EN: Services as the Identity service registers them, on the test database and the fake clock.
    /// TR: Identity servisinin kaydettiği gibi servisler; test veritabanı ve sahte saat üzerinde.
    /// </summary>
    /// <param name="secret">EN: Master secret. TR: Ana sır.</param>
    /// <returns>EN: A new service provider. TR: Yeni bir service provider.</returns>
    private ServiceProvider BuildServices(string secret = Secret)
    {
        var services = new ServiceCollection();
        services.AddBuildingBlocksPersistence();
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            options.UseServiceConventions(
                _container.GetConnectionString(),
                provider.GetRequiredService<AuditingInterceptor>(),
                provider.GetRequiredService<ChangeHistoryInterceptor>()));
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(Options.Create(new SigningKeyOptions { MasterSecret = secret }));
        services.AddSingleton<ISigningKeyProtector, AesGcmSigningKeyProtector>();
        services.AddSingleton<SigningKeyProvider>();
        return services.BuildServiceProvider();
    }
}
