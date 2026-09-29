using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Idempotency;
using MyWorkplace.BuildingBlocks.Persistence;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: <c>Idempotency-Key</c> (T-057, ADR-027) on a small app over the real test database: made-up endpoints count how
///     often they really run, so every rule shows as "ran once" or "ran twice".
/// TR: Gerçek test veritabanı üzerindeki küçük bir uygulamada <c>Idempotency-Key</c> (T-057, ADR-027): uydurma uç noktalar gerçekten kaç
///     kez çalıştıklarını sayar; böylece her kural "bir kez çalıştı" veya "iki kez çalıştı" olarak görünür.
/// </summary>
/// <param name="db">EN: Shared database fixture. TR: Paylaşılan veritabanı fixture'ı.</param>
public sealed class IdempotencyTests(PostgreSqlFixture db) : IAsyncLifetime
{
    /// <summary>EN: Header carrying the test caller's company. TR: Test çağıranın firmasını taşıyan başlık.</summary>
    private const string TenantHeader = "X-Test-Tenant";

    /// <summary>EN: Test clock. TR: Test saati.</summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>EN: Runs per endpoint. TR: Uç nokta başına çalışma sayısı.</summary>
    private readonly Dictionary<string, int> _runs = [];

    /// <summary>EN: Holds the slow endpoint until released. TR: Yavaş uç noktayı bırakılana kadar tutar.</summary>
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>EN: The test app. TR: Test uygulaması.</summary>
    private WebApplication _app = null!;

    /// <summary>EN: Client of the test app. TR: Test uygulamasının istemcisi.</summary>
    private HttpClient _client = null!;

    /// <summary>EN: A fresh company per test. TR: Test başına yeni bir firma.</summary>
    private readonly Guid _tenant = Guid.CreateVersion7();

    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HeaderUser>();
        builder.Services.AddDbContext<TestDbContext>(options => options.UseServiceConventions(db.ConnectionString));
        builder.Services.AddScoped<ServiceDbContext>(services => services.GetRequiredService<TestDbContext>());
        builder.Services.AddIdempotency();

        _app = builder.Build();
        _app.UseIdempotency();
        _app.MapPost("/notes", (NoteInput input) =>
        {
            var run = Run("notes");
            return Results.Created($"/notes/{run}", new { input.Text, run });
        });
        _app.MapPost("/invalid", () =>
        {
            Run("invalid");
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid.");
        });
        _app.MapPost("/flaky", () => Run("flaky") == 1
            ? Results.StatusCode(StatusCodes.Status500InternalServerError)
            : Results.Ok(new { done = true }));
        _app.MapPost("/slow", async () =>
        {
            Run("slow");
            await _gate.Task;
            return Results.Ok();
        });

        await _app.StartAsync(Ct);
        _client = _app.GetTestClient();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task WithoutAKey_EveryPostRuns()
    {
        using var first = await PostAsync("/notes", new { text = "a" }, key: null);
        using var second = await PostAsync("/notes", new { text = "a" }, key: null);

        Assert.Equal(2, Runs("notes"));
    }

    [Fact]
    public async Task SameKeySameRequest_ReplaysTheFirstResponse_AndRunsOnce()
    {
        using var first = await PostAsync("/notes", new { text = "a" }, "key-1");
        using var repeat = await PostAsync("/notes", new { text = "a" }, "key-1");

        Assert.Equal(1, Runs("notes"));
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, repeat.StatusCode));
        Assert.Equal(first.Headers.Location, repeat.Headers.Location);
        Assert.Equal(await first.Content.ReadAsStringAsync(Ct), await repeat.Content.ReadAsStringAsync(Ct));
        Assert.Equal("application/json; charset=utf-8", repeat.Content.Headers.ContentType?.ToString());
        Assert.False(first.Headers.Contains(IdempotencyMiddleware.ReplayedHeader));
        Assert.Equal(["true"], repeat.Headers.GetValues(IdempotencyMiddleware.ReplayedHeader));
    }

    [Fact]
    public async Task SameKeyDifferentRequest_Returns422()
    {
        using var first = await PostAsync("/notes", new { text = "a" }, "key-1");
        using var otherBody = await PostAsync("/notes", new { text = "b" }, "key-1");
        using var otherPath = await PostAsync("/invalid", new { text = "a" }, "key-1");

        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity), (otherBody.StatusCode, otherPath.StatusCode));
        Assert.Equal(1, Runs("notes"));
        Assert.Equal(0, Runs("invalid"));
    }

    [Fact]
    public async Task SameKeyWhileTheFirstRuns_Returns409()
    {
        var first = PostAsync("/slow", new { }, "key-1");
        await WaitUntilAsync(() => Runs("slow") == 1);

        using var repeat = await PostAsync("/slow", new { }, "key-1");
        _gate.SetResult();
        using var firstDone = await first;

        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstDone.StatusCode);
        Assert.Equal(1, Runs("slow"));
    }

    [Fact]
    public async Task ClientError_IsStoredAndReplayed()
    {
        using var first = await PostAsync("/invalid", new { }, "key-1");
        using var repeat = await PostAsync("/invalid", new { }, "key-1");

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (first.StatusCode, repeat.StatusCode));
        Assert.Equal(1, Runs("invalid"));
    }

    [Fact]
    public async Task ServerError_IsNotStored_SoTheRetryRuns()
    {
        using var failed = await PostAsync("/flaky", new { }, "key-1");
        using var retried = await PostAsync("/flaky", new { }, "key-1");
        using var replayed = await PostAsync("/flaky", new { }, "key-1");

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.True(replayed.Headers.Contains(IdempotencyMiddleware.ReplayedHeader));
        Assert.Equal(2, Runs("flaky"));
    }

    [Fact]
    public async Task KeysArePerCompany()
    {
        using var mine = await PostAsync("/notes", new { text = "a" }, "shared-key");
        using var theirs = await PostAsync("/notes", new { text = "a" }, "shared-key", tenant: Guid.CreateVersion7());

        Assert.Equal(2, Runs("notes"));
        Assert.False(theirs.Headers.Contains(IdempotencyMiddleware.ReplayedHeader));
    }

    [Fact]
    public async Task WithoutACompany_TheKeyIsIgnored()
    {
        // EN: Sign-up and sign-in have no company yet, so a key can't be scoped: the request just runs.
        // TR: Kayıt ve girişte henüz firma yoktur; anahtar bir firmaya bağlanamaz: istek sadece çalışır.
        using var first = await PostAsync("/notes", new { text = "a" }, "key-1", tenant: Guid.Empty);
        using var second = await PostAsync("/notes", new { text = "a" }, "key-1", tenant: Guid.Empty);

        Assert.Equal(2, Runs("notes"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task KeyOfTheWrongLength_Returns400(int length)
    {
        using var response = await PostAsync("/notes", new { text = "a" }, length == 0 ? " " : new string('k', length));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Runs("notes"));
    }

    [Fact]
    public async Task ExpiredKey_RunsAgain()
    {
        using var first = await PostAsync("/notes", new { text = "a" }, "key-1");

        _time.Advance(IdempotencyStore.Lifetime + TimeSpan.FromMinutes(1));
        using var later = await PostAsync("/notes", new { text = "b" }, "key-1");

        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
        Assert.Equal(2, Runs("notes"));
    }

    [Fact]
    public async Task AbandonedKey_CanBeTakenOverAfterAMinute()
    {
        // EN: A key left "in progress" by a crash (no response stored). TR: Bir çökmenin "işleniyor" bıraktığı anahtar (cevap saklanmamış).
        var store = _app.Services.GetRequiredService<IdempotencyStore>();
        var fingerprintOf = await PostAsync("/notes", new { text = "a" }, "probe");
        fingerprintOf.Dispose();
        await using (var context = db.CreateContext(new TestUser()))
        {
            var probe = await context.IdempotencyKeys.SingleAsync(r => r.TenantId == _tenant && r.Key == "probe", Ct);
            var reservation = await store.ReserveAsync(_tenant, "crashed", probe.RequestHash, Ct);
            Assert.Equal(ReservationOutcome.Reserved, reservation.Outcome);
        }

        using var tooSoon = await PostAsync("/notes", new { text = "a" }, "crashed");
        _time.Advance(IdempotencyStore.AbandonedAfter + TimeSpan.FromSeconds(1));
        using var takenOver = await PostAsync("/notes", new { text = "a" }, "crashed");

        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);
        Assert.Equal(HttpStatusCode.Created, takenOver.StatusCode);
    }

    [Fact]
    public async Task Cleanup_DeletesOnlyExpiredKeys()
    {
        using var old = await PostAsync("/notes", new { text = "a" }, "old");
        _time.Advance(IdempotencyStore.Lifetime);
        using var fresh = await PostAsync("/notes", new { text = "a" }, "fresh");

        await _app.Services.GetRequiredService<IdempotencyStore>().DeleteExpiredAsync(Ct);

        await using var context = db.CreateContext(new TestUser());
        Assert.Equal(["fresh"], await context.IdempotencyKeys.Where(r => r.TenantId == _tenant).Select(r => r.Key).ToListAsync(Ct));
    }

    /// <summary>
    /// EN: Counts a run of an endpoint.
    /// TR: Bir uç noktanın bir çalışmasını sayar.
    /// </summary>
    /// <param name="endpoint">EN: Endpoint name. TR: Uç nokta adı.</param>
    /// <returns>EN: Its run count so far. TR: Şimdiye kadarki çalışma sayısı.</returns>
    private int Run(string endpoint)
    {
        lock (_runs)
        {
            _runs[endpoint] = Runs(endpoint) + 1;
            return _runs[endpoint];
        }
    }

    /// <summary>
    /// EN: How often an endpoint really ran.
    /// TR: Bir uç noktanın gerçekte kaç kez çalıştığı.
    /// </summary>
    /// <param name="endpoint">EN: Endpoint name. TR: Uç nokta adı.</param>
    /// <returns>EN: The count. TR: Sayı.</returns>
    private int Runs(string endpoint)
    {
        lock (_runs)
        {
            return _runs.GetValueOrDefault(endpoint);
        }
    }

    /// <summary>
    /// EN: Posts as a company, with an optional key.
    /// TR: Bir firma adına, isteğe bağlı bir anahtarla gönderir.
    /// </summary>
    /// <param name="path">EN: Address. TR: Adres.</param>
    /// <param name="body">EN: Body. TR: Gövde.</param>
    /// <param name="key">EN: Idempotency key or null. TR: Idempotency anahtarı veya null.</param>
    /// <param name="tenant">EN: Company; the test's own when null, none when empty. TR: Firma; null ise testin kendisi, boşsa hiçbiri.</param>
    /// <returns>EN: The response. TR: Cevap.</returns>
    private Task<HttpResponseMessage> PostAsync(string path, object body, string? key, Guid? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add(IdempotencyMiddleware.KeyHeader, key);
        }

        if ((tenant ?? _tenant) is var company && company != Guid.Empty)
        {
            request.Headers.Add(TenantHeader, company.ToString());
        }

        return _client.SendAsync(request, Ct);
    }

    /// <summary>
    /// EN: Polls a condition for up to 10 seconds.
    /// TR: Bir koşulu en fazla 10 saniye yoklar.
    /// </summary>
    /// <param name="condition">EN: The condition. TR: Koşul.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
            await Task.Delay(20, Ct);
        }
    }

    /// <summary>
    /// EN: Body of the notes endpoint.
    /// TR: Notlar uç noktasının gövdesi.
    /// </summary>
    /// <param name="Text">EN: Text. TR: Metin.</param>
    private sealed record NoteInput(string? Text);

    /// <summary>
    /// EN: Test user whose company comes from a request header.
    /// TR: Firması bir istek başlığından gelen test kullanıcısı.
    /// </summary>
    /// <param name="http">EN: HTTP context accessor. TR: HTTP bağlam erişimcisi.</param>
    private sealed class HeaderUser(IHttpContextAccessor http) : ICurrentUser
    {
        /// <inheritdoc />
        public Guid? UserId => null;

        /// <inheritdoc />
        public Guid? TenantId =>
            Guid.TryParse(http.HttpContext?.Request.Headers[TenantHeader], out var tenant) ? tenant : null;

        /// <inheritdoc />
        public string? Plan => null;
    }
}
