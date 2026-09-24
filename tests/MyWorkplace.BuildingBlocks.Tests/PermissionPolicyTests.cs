using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyWorkplace.Contracts.Identity;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Permission policies (T-025, ADR-022): any catalog permission is a policy name; a typo never authorizes anyone.
/// TR: İzin politikaları (T-025, ADR-022): katalogdaki her izin bir politika adıdır; bir yazım hatası kimseye asla yetki vermez.
/// </summary>
public sealed class PermissionPolicyTests : IAsyncLifetime
{
    /// <summary>EN: Header carrying the test user's permissions. TR: Test kullanıcısının izinlerini taşıyan başlık.</summary>
    private const string PermissionsHeader = "X-Test-Permissions";

    /// <summary>EN: In-memory test app. TR: Bellekte çalışan test uygulaması.</summary>
    private WebApplication _app = null!;

    /// <summary>EN: Client of the test app. TR: Test uygulamasının istemcisi.</summary>
    private HttpClient _client = null!;

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.AddPermissionPolicies();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapDelete("/customers", () => Results.NoContent()).RequireAuthorization(Permissions.Customers.Delete);
        _app.MapGet("/typo", () => Results.Ok()).RequireAuthorization("customers.destroy");

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task UserWithThePermission_IsAllowed()
    {
        using var response = await SendAsync(HttpMethod.Delete, "/customers", "customers.read,customers.delete");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutThePermission_Gets403()
    {
        using var response = await SendAsync(HttpMethod.Delete, "/customers", "customers.read,customers.write");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCaller_Gets401()
    {
        using var response = await SendAsync(HttpMethod.Delete, "/customers", permissions: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PolicyNameOutsideTheCatalog_FailsLoudly_InsteadOfAuthorizing()
    {
        // EN: "customers.destroy" is a typo of "customers.delete": no policy exists, so ASP.NET Core throws.
        // TR: "customers.destroy", "customers.delete"in yazım hatası: politika yok, bu yüzden ASP.NET Core hata fırlatır.
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(HttpMethod.Get, "/typo", "customers.destroy"));
    }

    [Fact]
    public void OwnerHasEveryPermission_ViewerOnlyReads()
    {
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), Roles.EffectivePermissions([Roles.Owner], []));
        Assert.Equal(
            [Permissions.Customers.Read, Permissions.Inventory.Read, Permissions.Orders.Read],
            Roles.EffectivePermissions([Roles.Viewer], []));
        Assert.Contains(Permissions.Customers.Delete, Roles.EffectivePermissions([Roles.Member], [Permissions.Customers.Delete]));
        Assert.DoesNotContain("made.up", Roles.EffectivePermissions(["NoSuchRole"], ["made.up"]));
    }

    /// <summary>
    /// EN: Sends a request as a user with the given comma-separated permissions (null = anonymous).
    /// TR: Verilen virgülle ayrılmış izinlere sahip bir kullanıcı olarak istek gönderir (null = anonim).
    /// </summary>
    /// <param name="method">EN: HTTP method. TR: HTTP metodu.</param>
    /// <param name="url">EN: Address. TR: Adres.</param>
    /// <param name="permissions">EN: Permissions or null. TR: İzinler veya null.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? permissions)
    {
        using var request = new HttpRequestMessage(method, url);
        if (permissions is not null)
        {
            request.Headers.Add(PermissionsHeader, permissions);
        }

        return await _client.SendAsync(request, Ct);
    }

    /// <summary>
    /// EN: Test-only authentication: the principal's "perm" claims come from a request header.
    /// TR: Sadece test için kimlik doğrulama: kullanıcının "perm" claim'leri bir istek başlığından gelir.
    /// </summary>
    /// <param name="options">EN: Scheme options. TR: Şema seçenekleri.</param>
    /// <param name="logger">EN: Logger factory. TR: Logger fabrikası.</param>
    /// <param name="encoder">EN: URL encoder. TR: URL kodlayıcı.</param>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <summary>EN: Scheme name. TR: Şema adı.</summary>
        public const string SchemeName = "TestHeader";

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(PermissionsHeader, out var header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = header.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => new Claim(TokenClaims.Permission, p));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
