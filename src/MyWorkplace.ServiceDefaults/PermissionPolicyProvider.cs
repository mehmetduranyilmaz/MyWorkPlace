using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyWorkplace.Abstractions.Identity;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// EN: Turns any permission of the registered catalog into a policy on demand (ADR-022, ADR-026):
///     <c>RequireAuthorization("customers.delete")</c> means "a signed-in user whose token has <c>perm = customers.delete</c>".
///     Registered policies (fallback, plan) still come first. A name that is neither registered nor in the catalog yields
///     no policy, so ASP.NET Core fails loudly instead of authorizing a typo. The catalog is the product's — the core only
///     applies it; a host without one (the gateway) simply has no permission policies.
/// TR: Kayıtlı kataloğun herhangi bir iznini istek anında politikaya çevirir (ADR-022, ADR-026): <c>RequireAuthorization("customers.delete")</c>,
///     "token'ında <c>perm = customers.delete</c> olan giriş yapmış kullanıcı" demektir. Kayıtlı politikalar (fallback, plan) yine önce gelir.
///     Ne kayıtlı ne de katalogda olan bir ad politika üretmez; böylece ASP.NET Core bir yazım hatasına yetki vermek yerine açıkça hata verir.
///     Katalog ürünündür — çekirdek sadece uygular; kataloğu olmayan bir host (gateway) sadece izin politikasına sahip değildir.
/// </summary>
/// <param name="options">EN: Authorization options. TR: Yetkilendirme seçenekleri.</param>
/// <param name="catalogs">EN: The registered catalog, if any. TR: Varsa kayıtlı katalog.</param>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options, IEnumerable<PermissionCatalog> catalogs)
    : DefaultAuthorizationPolicyProvider(options)
{
    /// <summary>EN: The product's catalog, or null. TR: Ürünün kataloğu veya null.</summary>
    private readonly PermissionCatalog? _catalog = catalogs.SingleOrDefault();

    /// <summary>EN: Built policies, one per permission. TR: Oluşturulmuş politikalar, izin başına bir tane.</summary>
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _permissionPolicies = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var registered = await base.GetPolicyAsync(policyName);
        if (registered is not null || _catalog is null || !_catalog.All.Contains(policyName))
        {
            return registered;
        }

        return _permissionPolicies.GetOrAdd(policyName, permission => new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(TokenClaims.Permission, permission)
            .Build());
    }
}

/// <summary>
/// EN: Registration of permission policies and of the product's catalog.
/// TR: İzin politikalarının ve ürün kataloğunun kaydı.
/// </summary>
public static class PermissionPolicyExtensions
{
    /// <summary>
    /// EN: Makes every permission of the registered catalog usable as a policy name. Called by <c>AddTokenAuthentication</c>.
    /// TR: Kayıtlı kataloğun her iznini politika adı olarak kullanılabilir yapar. <c>AddTokenAuthentication</c> tarafından çağrılır.
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>());
        return services;
    }

    /// <summary>
    /// EN: Registers the product's permission catalog (ADR-026) — explicitly, so where permissions come from is visible
    ///     in each service's setup. Registering two different catalogs is a mistake and fails at once.
    /// TR: Ürünün izin kataloğunu kaydeder (ADR-026) — açıkça; böylece izinlerin nereden geldiği her servisin kurulumunda görünür.
    ///     İki farklı katalog kaydetmek bir hatadır ve hemen hata verir.
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <param name="catalog">EN: The product's catalog. TR: Ürünün kataloğu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddPermissionCatalog(this IServiceCollection services, PermissionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(PermissionCatalog))?.ImplementationInstance;
        if (existing is not null && !ReferenceEquals(existing, catalog))
        {
            throw new InvalidOperationException("A different permission catalog is already registered.");
        }

        services.TryAddSingleton(catalog);
        return services;
    }
}
