using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWorkplace.BuildingBlocks.Identity;

namespace MyWorkplace.BuildingBlocks.Persistence;

/// <summary>
/// EN: One-line registration of a service's DbContext with all shared conventions.
/// TR: Bir servisin DbContext'ini tüm ortak kurallarla tek satırda kaydeder.
/// </summary>
public static class PersistenceExtensions
{
    /// <summary>
    /// EN: Registers <typeparamref name="TContext"/> on PostgreSQL with snake_case naming, the auditing and
    ///     change-history interceptors, and Aspire's health checks, retries and telemetry.
    /// TR: <typeparamref name="TContext"/>'i PostgreSQL üzerinde snake_case isimlendirme, denetim ve değişiklik geçmişi
    ///     interceptor'ları ile Aspire'ın sağlık kontrolü, yeniden deneme ve telemetrisiyle kaydeder.
    /// </summary>
    /// <typeparam name="TContext">EN: The service's DbContext. TR: Servisin DbContext'i.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <param name="connectionName">EN: Aspire connection name, e.g. "identity-db". TR: Aspire bağlantı adı, ör. "identity-db".</param>
    /// <returns>EN: The same builder. TR: Aynı builder.</returns>
    public static IHostApplicationBuilder AddServiceDbContext<TContext>(
        this IHostApplicationBuilder builder,
        string connectionName)
        where TContext : ServiceDbContext
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionName}' is missing. Is the database referenced in the AppHost?");

        builder.Services.AddBuildingBlocksPersistence();
        builder.Services.AddDbContext<TContext>((services, options) =>
            options.UseServiceConventions(
                connectionString,
                services.GetRequiredService<AuditingInterceptor>(),
                services.GetRequiredService<ChangeHistoryInterceptor>()));

        builder.EnrichNpgsqlDbContext<TContext>();
        return builder;
    }

    /// <summary>
    /// EN: Applies pending EF Core migrations at startup. For Development only: with several instances running,
    ///     migrations must run as a separate step before deployment (ADR-015).
    /// TR: Bekleyen EF Core migration'larını açılışta uygular. Sadece geliştirme için: birden fazla kopya çalışırken
    ///     migration'lar dağıtımdan önce ayrı bir adımda çalıştırılmalıdır (ADR-015).
    /// </summary>
    /// <typeparam name="TContext">EN: The service's DbContext. TR: Servisin DbContext'i.</typeparam>
    /// <param name="app">EN: The built application. TR: Oluşturulmuş uygulama.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task that completes when the schema is up to date. TR: Şema güncel olduğunda tamamlanan görev.</returns>
    public static async Task MigrateDatabaseAsync<TContext>(this IHost app, CancellationToken cancellationToken = default)
        where TContext : ServiceDbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// EN: Registers the pieces the interceptors need. Defaults can be replaced (T-008 replaces the current user).
    /// TR: Interceptor'ların ihtiyaç duyduğu parçaları kaydeder. Varsayılanlar değiştirilebilir (T-008 aktif kullanıcıyı değiştirir).
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddBuildingBlocksPersistence(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.TryAddScoped<AuditingInterceptor>();
        services.TryAddScoped<ChangeHistoryInterceptor>();
        return services;
    }

    /// <summary>
    /// EN: Applies the provider-level conventions. Shared by the runtime registration and the tests,
    ///     so both use exactly the same configuration.
    /// TR: Sağlayıcı düzeyindeki kuralları uygular. Çalışma zamanı kaydı ve testler bunu paylaşır;
    ///     böylece ikisi de birebir aynı yapılandırmayı kullanır.
    /// </summary>
    /// <param name="options">EN: Options builder. TR: Seçenek builder'ı.</param>
    /// <param name="connectionString">EN: PostgreSQL connection string. TR: PostgreSQL bağlantı cümlesi.</param>
    /// <param name="interceptors">EN: Interceptors, in execution order. TR: Çalışma sırasına göre interceptor'lar.</param>
    /// <returns>EN: The same options builder. TR: Aynı seçenek builder'ı.</returns>
    public static DbContextOptionsBuilder UseServiceConventions(
        this DbContextOptionsBuilder options,
        string connectionString,
        params IInterceptor[] interceptors) =>
        options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors);
}
