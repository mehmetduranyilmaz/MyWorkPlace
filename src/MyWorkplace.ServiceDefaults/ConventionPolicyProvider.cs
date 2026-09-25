using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyWorkplace.Abstractions.Identity;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// EN: Builds policies on demand from their names, so none has to be registered one by one (ADR-022, ADR-026):
///     <list type="bullet">
///     <item><c>plan:&lt;name&gt;</c> — a signed-in user whose token has <c>plan = &lt;name&gt;</c>. Any name is accepted: an
///     unknown plan matches no token, so a typo refuses everyone (fails closed).</item>
///     <item>a permission of the registered catalog, e.g. <c>customers.delete</c> — a signed-in user whose token has
///     <c>perm = customers.delete</c>. A name outside the catalog yields no policy, so ASP.NET Core fails loudly instead of
///     authorizing a typo. A host without a catalog (the gateway) has no permission policies.</item>
///     </list>
///     Registered policies (the fallback) still come first. The core only applies the convention; plan names and the
///     catalog belong to the product.
/// TR: Politikaları adlarından istek anında üretir; böylece hiçbirinin tek tek kaydedilmesi gerekmez (ADR-022, ADR-026):
///     <list type="bullet">
///     <item><c>plan:&lt;ad&gt;</c> — token'ında <c>plan = &lt;ad&gt;</c> olan giriş yapmış kullanıcı. Her ad kabul edilir: bilinmeyen
///     bir plan hiçbir token'la eşleşmez, bu yüzden bir yazım hatası herkesi reddeder (güvenli tarafta kalır).</item>
///     <item>kayıtlı katalogdaki bir izin, örneğin <c>customers.delete</c> — token'ında <c>perm = customers.delete</c> olan giriş
///     yapmış kullanıcı. Katalog dışındaki bir ad politika üretmez; böylece ASP.NET Core bir yazım hatasına yetki vermek yerine
///     açıkça hata verir. Kataloğu olmayan bir host'un (gateway) izin politikası yoktur.</item>
///     </list>
///     Kayıtlı politikalar (fallback) yine önce gelir. Çekirdek sadece kuralı uygular; plan adları ve katalog ürüne aittir.
/// </summary>
/// <param name="options">EN: Authorization options. TR: Yetkilendirme seçenekleri.</param>
/// <param name="catalogs">EN: The registered catalog, if any. TR: Varsa kayıtlı katalog.</param>
public sealed class ConventionPolicyProvider(IOptions<AuthorizationOptions> options, IEnumerable<PermissionCatalog> catalogs)
    : DefaultAuthorizationPolicyProvider(options)
{
    /// <summary>EN: The product's catalog, or null. TR: Ürünün kataloğu veya null.</summary>
    private readonly PermissionCatalog? _catalog = catalogs.SingleOrDefault();

    /// <summary>EN: Built policies, one per name. TR: Oluşturulmuş politikalar, ad başına bir tane.</summary>
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var registered = await base.GetPolicyAsync(policyName);
        if (registered is not null)
        {
            return registered;
        }

        if (policyName.StartsWith(PlanPolicy.Prefix, StringComparison.Ordinal))
        {
            var plan = policyName[PlanPolicy.Prefix.Length..];
            return string.IsNullOrWhiteSpace(plan) ? null : _policies.GetOrAdd(policyName, _ => Requiring(TokenClaims.Plan, plan));
        }

        return _catalog is not null && _catalog.All.Contains(policyName)
            ? _policies.GetOrAdd(policyName, permission => Requiring(TokenClaims.Permission, permission))
            : null;
    }

    /// <summary>
    /// EN: A policy requiring a signed-in user whose token has the given claim value.
    /// TR: Token'ında verilen claim değeri olan, giriş yapmış bir kullanıcı isteyen politika.
    /// </summary>
    /// <param name="claim">EN: Claim name. TR: Claim adı.</param>
    /// <param name="value">EN: Required value. TR: İstenen değer.</param>
    /// <returns>EN: The policy. TR: Politika.</returns>
    private static AuthorizationPolicy Requiring(string claim, string value) => new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(claim, value)
        .Build();
}

/// <summary>
/// EN: Registration of convention policies and of the product's permission catalog.
/// TR: Kurala dayalı politikaların ve ürünün izin kataloğunun kaydı.
/// </summary>
public static class ConventionPolicyExtensions
{
    /// <summary>
    /// EN: Makes every <c>plan:&lt;name&gt;</c> and every permission of the registered catalog usable as a policy name.
    ///     Called by <c>AddTokenAuthentication</c>.
    /// TR: Her <c>plan:&lt;ad&gt;</c> ve kayıtlı kataloğun her iznini politika adı olarak kullanılabilir yapar.
    ///     <c>AddTokenAuthentication</c> tarafından çağrılır.
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddConventionPolicies(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, ConventionPolicyProvider>());
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
