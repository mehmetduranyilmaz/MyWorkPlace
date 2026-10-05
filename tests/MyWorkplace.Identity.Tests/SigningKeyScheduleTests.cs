using MyWorkplace.Identity.Tokens;

namespace MyWorkplace.Identity.Tests;

/// <summary>
/// EN: The life of a signing key as plain rules (T-066, ADR-032, ADR-029): published → signing → retiring → deleted, with
///     the default timings (90 days, 24 hours ahead, 24 hours after).
/// TR: Saf kurallar olarak bir imzalama anahtarının yaşamı (T-066, ADR-032, ADR-029): yayınlandı → imzalıyor → emekliye ayrılıyor → silindi;
///     varsayılan sürelerle (90 gün, 24 saat önce, 24 saat sonra).
/// </summary>
public sealed class SigningKeyScheduleTests
{
    /// <summary>EN: Start of the story. TR: Hikâyenin başlangıcı.</summary>
    private static readonly DateTimeOffset _t0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>EN: Default timings. TR: Varsayılan süreler.</summary>
    private static readonly SigningKeyOptions _options = new();

    [Fact]
    public void NoKey_CreatesOneThatSignsNow()
    {
        var plan = SigningKeySchedule.Plan([], _t0, _options);

        Assert.Empty(plan.ToDelete);
        Assert.Equal(_t0, plan.CreateActivatingAt);
    }

    [Fact]
    public void BeforeThePublishWindow_NothingChanges()
    {
        var plan = SigningKeySchedule.Plan([Key("a", _t0)], _t0.AddDays(89).AddHours(-1), _options);

        Assert.Empty(plan.ToDelete);
        Assert.Null(plan.CreateActivatingAt);
    }

    [Fact]
    public void InThePublishWindow_TheNextKeyIsCreated_ToSignWhenTheCurrentOneIsDue()
    {
        var plan = SigningKeySchedule.Plan([Key("a", _t0)], _t0.AddDays(89), _options);

        Assert.Equal(_t0.AddDays(90), plan.CreateActivatingAt);
    }

    [Fact]
    public void ALateCheck_StillPublishesTheFullWindowAhead()
    {
        // EN: The service was down past the key's due date: the next key still waits 24 h, the current one signs meanwhile.
        // TR: Servis anahtarın vadesinden sonrasına kadar kapalıydı: sıradaki anahtar yine 24 sa bekler, bu sırada mevcut olan imzalar.
        var now = _t0.AddDays(120);

        var plan = SigningKeySchedule.Plan([Key("a", _t0)], now, _options);

        Assert.Equal(now.AddHours(24), plan.CreateActivatingAt);
        Assert.Equal("a", SigningKeySchedule.SignerAt([Key("a", _t0)], now)?.KeyId);
    }

    [Fact]
    public void APublishedKey_SignsOnlyFromItsActivation()
    {
        SigningKeyState[] keys = [Key("a", _t0), Key("b", _t0.AddDays(90))];

        Assert.Equal("a", SigningKeySchedule.SignerAt(keys, _t0.AddDays(90).AddTicks(-1))?.KeyId);
        Assert.Equal("b", SigningKeySchedule.SignerAt(keys, _t0.AddDays(90))?.KeyId);
        Assert.Null(SigningKeySchedule.Plan(keys, _t0.AddDays(89).AddHours(12), _options).CreateActivatingAt);
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    public void AKeyThatStoppedSigning_IsDeleted24HoursLater(int hoursAfter, bool deleted)
    {
        SigningKeyState[] keys = [Key("a", _t0), Key("b", _t0.AddDays(90))];

        var plan = SigningKeySchedule.Plan(keys, _t0.AddDays(90).AddHours(hoursAfter), _options);

        Assert.Equal(deleted ? ["a"] : [], plan.ToDelete);
    }

    [Fact]
    public void ARetiredPlaintextKey_IsReplacedAtOnce_AndDeleted24HoursLater()
    {
        SigningKeyState legacy = new("legacy", _t0, CanSign: false);

        var atUpgrade = SigningKeySchedule.Plan([legacy], _t0.AddDays(10), _options);
        var dayAfter = SigningKeySchedule.Plan([legacy, Key("new", _t0.AddDays(10))], _t0.AddDays(11), _options);

        Assert.Equal(_t0.AddDays(10), atUpgrade.CreateActivatingAt);
        Assert.Null(SigningKeySchedule.SignerAt([legacy], _t0.AddDays(10)));
        Assert.Equal(["legacy"], dayAfter.ToDelete);
    }

    /// <summary>
    /// EN: A key that can sign.
    /// TR: İmzalayabilen bir anahtar.
    /// </summary>
    /// <param name="id">EN: Key id. TR: Anahtar kimliği.</param>
    /// <param name="activatesAt">EN: Activation time. TR: Aktivasyon zamanı.</param>
    /// <returns>EN: The state. TR: Durum.</returns>
    private static SigningKeyState Key(string id, DateTimeOffset activatesAt) => new(id, activatesAt, CanSign: true);
}
