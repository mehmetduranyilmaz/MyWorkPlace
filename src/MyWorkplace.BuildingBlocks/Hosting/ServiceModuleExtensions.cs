using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWorkplace.Abstractions.Identity;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Hosting;

/// <summary>
/// EN: The standard setup of a business service in two calls (ADR-021). Everything every module needs — telemetry,
///     health, token validation, secure-by-default authorization, database conventions, ProblemDetails, API docs,
///     development migrations — and the middleware order, defined once.
/// TR: Bir iş servisinin standart kurulumu iki çağrıda (ADR-021). Her modülün ihtiyaç duyduğu her şey — telemetri, sağlık,
///     token doğrulama, varsayılan olarak korumalı yetkilendirme, veritabanı kuralları, ProblemDetails, API dokümanları,
///     geliştirme migration'ları — ve middleware sırası, tek yerde tanımlı.
/// </summary>
public static class ServiceModuleExtensions
{
    /// <summary>
    /// EN: Registers the standard services. Call <c>builder.Services.AddValidation()</c> in the service itself:
    ///     the validation source generator must run in the project that declares the request types (ADR-021).
    /// TR: Standart servisleri kaydeder. <c>builder.Services.AddValidation()</c> servisin kendisinde çağrılır: doğrulama kaynak
    ///     üreteci, istek tiplerini tanımlayan projede çalışmalıdır (ADR-021).
    /// </summary>
    /// <typeparam name="TContext">EN: The service's DbContext. TR: Servisin DbContext'i.</typeparam>
    /// <param name="builder">EN: The application builder. TR: Uygulama builder'ı.</param>
    /// <param name="connectionName">EN: Aspire connection name, e.g. "customers-db". TR: Aspire bağlantı adı, ör. "customers-db".</param>
    /// <param name="permissions">
    /// EN: The product's permission catalog (ADR-026), e.g. <c>Permissions.Catalog</c>; required, so a service can't
    ///     start without knowing its permissions.
    /// TR: Ürünün izin kataloğu (ADR-026), ör. <c>Permissions.Catalog</c>; zorunludur, böylece bir servis izinlerini bilmeden başlayamaz.
    /// </param>
    /// <returns>EN: The same builder. TR: Aynı builder.</returns>
    public static WebApplicationBuilder AddServiceModule<TContext>(
        this WebApplicationBuilder builder, string connectionName, PermissionCatalog permissions)
        where TContext : ServiceDbContext
    {
        builder.Services.AddPermissionCatalog(permissions);
        builder.AddServiceDefaults();
        builder.AddTokenAuthentication();
        builder.AddServiceDbContext<TContext>(connectionName);
        builder.Services.AddServiceProblemDetails();
        builder.Services.AddServiceApiDocs();

        // EN: Enums travel as names ("Block"), not numbers: readable, and reordering an enum can't change the API.
        // TR: Enum'lar sayı değil ad olarak taşınır ("Block"): okunur ve enum sırasının değişmesi API'yi değiştiremez.
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return builder;
    }

    /// <summary>
    /// EN: Adds the middleware in the one correct order (errors first, so they wrap everything; authentication before
    ///     authorization) and maps health and API docs; applies migrations in Development (ADR-015).
    ///     Map the module's endpoints after this call.
    /// TR: Middleware'i tek doğru sırayla ekler (hatalar önce, böylece her şeyi sarar; kimlik doğrulama yetkilendirmeden önce),
    ///     sağlık ve API doküman uç noktalarını açar; geliştirme ortamında migration'ları uygular (ADR-015).
    ///     Modülün uç noktaları bu çağrıdan sonra tanımlanır.
    /// </summary>
    /// <typeparam name="TContext">EN: The service's DbContext. TR: Servisin DbContext'i.</typeparam>
    /// <param name="app">EN: The built application. TR: Oluşturulmuş uygulama.</param>
    /// <returns>EN: A task that completes when the pipeline is ready. TR: Pipeline hazır olduğunda tamamlanan görev.</returns>
    public static async Task UseServiceModuleAsync<TContext>(this WebApplication app)
        where TContext : ServiceDbContext
    {
        app.UseServiceProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapDefaultEndpoints();
        app.MapServiceApiDocs();

        if (app.Environment.IsDevelopment())
        {
            await app.MigrateDatabaseAsync<TContext>();
        }
    }
}
