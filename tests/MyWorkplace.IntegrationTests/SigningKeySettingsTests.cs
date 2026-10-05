namespace MyWorkplace.IntegrationTests;

/// <summary>
/// EN: The AppHost's guard for key rotation (T-066, ADR-032): a new signing key must be published longer than verifiers
///     wait between key refreshes, or it could sign before they know it. Checked before anything starts.
/// TR: AppHost'un anahtar rotasyonu için koruması (T-066, ADR-032): yeni bir imzalama anahtarı, doğrulayıcıların anahtar yenilemeleri arasında
///     beklediğinden daha uzun süre yayınlanmalıdır; aksi halde onlar tanımadan imzalayabilirdi. Hiçbir şey başlamadan kontrol edilir.
/// </summary>
public sealed class SigningKeySettingsTests
{
    /// <summary>EN: Test cancellation token. TR: Test iptal belirteci.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("00:05:00", "00:05:00")]
    [InlineData("00:05:00", "00:10:00")]
    public async Task PublishAheadNotLongerThanTheVerifiersRefresh_StopsTheAppHost(string publishAhead, string refresh)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => DistributedApplicationTestingBuilder.CreateAsync<Projects.MyWorkplace_AppHost>(
            ["Storage:Ephemeral=true", $"SigningKeys:PublishAhead={publishAhead}", $"Auth:MetadataRefreshInterval={refresh}"], Ct));

        Assert.Contains("SigningKeys:PublishAhead", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheShippedSettings_PassTheGuard()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MyWorkplace_AppHost>(
            ["Storage:Ephemeral=true"], Ct);

        Assert.Contains(builder.Resources, r => r.Name == "signing-key-master-secret");
    }
}
