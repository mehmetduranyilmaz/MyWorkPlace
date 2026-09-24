using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: The current user is read from the validated token, and it drives the tenant filter (T-008).
/// TR: Aktif kullanıcı doğrulanmış token'dan okunur ve firma filtresini o yönlendirir (T-008).
/// </summary>
/// <param name="db">EN: Shared database fixture. TR: Paylaşılan veritabanı fixture'ı.</param>
public sealed class HttpCurrentUserTests(PostgreSqlFixture db)
{
    [Fact]
    public void AuthenticatedRequest_ClaimsAreRead()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        var user = CreateUser(authenticated: true, ("sub", userId.ToString()), ("tenant_id", tenantId.ToString()), ("plan", "pro"));

        Assert.Equal(userId, user.UserId);
        Assert.Equal(tenantId, user.TenantId);
        Assert.Equal("pro", user.Plan);
    }

    [Fact]
    public void UnauthenticatedPrincipal_IsTreatedAsAnonymous()
    {
        // EN: Claims without a validated identity must never select a tenant.
        // TR: Doğrulanmış kimliği olmayan claim'ler asla bir firma seçmemeli.
        var user = CreateUser(authenticated: false, ("sub", Guid.CreateVersion7().ToString()), ("tenant_id", Guid.CreateVersion7().ToString()));

        Assert.Null(user.UserId);
        Assert.Null(user.TenantId);
        Assert.Null(user.Plan);
    }

    [Fact]
    public void MalformedTenantClaim_ReadsAsNull()
    {
        var user = CreateUser(authenticated: true, ("sub", "not-a-guid"), ("tenant_id", "also-not-a-guid"));

        Assert.Null(user.UserId);
        Assert.Null(user.TenantId);
    }

    [Fact]
    public void OutsideOfARequest_IsAnonymous()
    {
        var user = new HttpCurrentUser(new HttpContextAccessor());

        Assert.Null(user.UserId);
        Assert.Null(user.TenantId);
    }

    [Fact]
    public async Task TenantFilter_FollowsTheTokenOfTheRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = TestUser.OfNewTenant();
        Guid noteId;
        await using (var context = db.CreateContext(owner))
        {
            var note = new TestNote { Title = "owned" };
            context.Notes.Add(note);
            await context.SaveChangesAsync(ct);
            noteId = note.Id;
        }

        var sameTenant = CreateUser(authenticated: true, ("sub", Guid.CreateVersion7().ToString()), ("tenant_id", owner.TenantId.ToString()!));
        var otherTenant = CreateUser(authenticated: true, ("sub", Guid.CreateVersion7().ToString()), ("tenant_id", Guid.CreateVersion7().ToString()));

        await using var sameContext = db.CreateContext(sameTenant);
        await using var otherContext = db.CreateContext(otherTenant);
        Assert.True(await sameContext.Notes.AnyAsync(n => n.Id == noteId, ct));
        Assert.False(await otherContext.Notes.AnyAsync(n => n.Id == noteId, ct));
    }

    /// <summary>
    /// EN: Builds an <see cref="HttpCurrentUser"/> over a request whose user has the given claims.
    /// TR: Kullanıcısı verilen claim'lere sahip bir istek üzerinde <see cref="HttpCurrentUser"/> oluşturur.
    /// </summary>
    /// <param name="authenticated">EN: Whether the token was validated. TR: Token doğrulandı mı.</param>
    /// <param name="claims">EN: Claims as (type, value). TR: (tip, değer) olarak claim'ler.</param>
    /// <returns>EN: The current user. TR: Aktif kullanıcı.</returns>
    private static HttpCurrentUser CreateUser(bool authenticated, params (string Type, string Value)[] claims)
    {
        // EN: A non-null authentication type is what marks an identity as authenticated.
        // TR: Bir kimliği "doğrulanmış" yapan, null olmayan kimlik doğrulama tipidir.
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: authenticated ? "Bearer" : null);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new HttpCurrentUser(new FixedHttpContextAccessor(context));
    }

    /// <summary>
    /// EN: Holds one request per instance. The real <see cref="HttpContextAccessor"/> stores the request in a static
    ///     AsyncLocal shared by all instances — right for real requests, but two "users" created in one test would
    ///     overwrite each other.
    /// TR: Örnek başına tek bir istek tutar. Gerçek <see cref="HttpContextAccessor"/> isteği tüm örneklerin paylaştığı
    ///     statik bir AsyncLocal'da saklar — gerçek istekler için doğru, ama tek testte oluşturulan iki "kullanıcı"
    ///     birbirinin üzerine yazar.
    /// </summary>
    /// <param name="context">EN: The fixed request. TR: Sabit istek.</param>
    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        /// <inheritdoc />
        public HttpContext? HttpContext
        {
            get => context;
            set => throw new NotSupportedException("This accessor always returns the request it was created with.");
        }
    }
}
