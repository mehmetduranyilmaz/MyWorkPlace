using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyWorkplace.Contracts.Identity;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// EN: Turns any catalog permission into a policy on demand (ADR-022): <c>RequireAuthorization("customers.delete")</c>
///     means "a signed-in user whose token has <c>perm = customers.delete</c>". Registered policies (fallback, pro-plan)
///     still come first. A name that is neither registered nor in the catalog yields no policy, so ASP.NET Core fails
///     loudly instead of authorizing a typo.
/// TR: Katalogdaki herhangi bir izni istek anında politikaya çevirir (ADR-022): <c>RequireAuthorization("customers.delete")</c>,
///     "token'ında <c>perm = customers.delete</c> olan giriş yapmış kullanıcı" demektir. Kayıtlı politikalar (fallback, pro-plan)
///     yine önce gelir. Ne kayıtlı ne de katalogda olan bir ad politika üretmez; böylece ASP.NET Core bir yazım hatasına yetki
///     vermek yerine açıkça hata verir.
/// </summary>
/// <param name="options">EN: Authorization options. TR: Yetkilendirme seçenekleri.</param>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    /// <summary>EN: Built policies, one per permission. TR: Oluşturulmuş politikalar, izin başına bir tane.</summary>
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _permissionPolicies = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var registered = await base.GetPolicyAsync(policyName);
        if (registered is not null || !Permissions.All.Contains(policyName))
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
/// EN: Registration of permission policies.
/// TR: İzin politikalarının kaydı.
/// </summary>
public static class PermissionPolicyExtensions
{
    /// <summary>
    /// EN: Makes every catalog permission usable as a policy name. Called by <c>AddTokenAuthentication</c>.
    /// TR: Katalogdaki her izni politika adı olarak kullanılabilir yapar. <c>AddTokenAuthentication</c> tarafından çağrılır.
    /// </summary>
    /// <param name="services">EN: Service collection. TR: Servis koleksiyonu.</param>
    /// <returns>EN: The same collection. TR: Aynı koleksiyon.</returns>
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>());
        return services;
    }
}
