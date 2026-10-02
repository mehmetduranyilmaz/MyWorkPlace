namespace MyWorkplace.Products.Tests;

/// <summary>
/// EN: Throw-away (T-059): fails on purpose, to prove a red CI blocks the merge. Never merged.
/// TR: Geçici (T-059): bilerek başarısız olur; kırmızı bir CI'ın merge'ü engellediğini kanıtlamak için. Asla merge edilmez.
/// </summary>
public sealed class RedProbeTests
{
    [Fact]
    public void FailsOnPurpose() => Assert.Fail("T-059 red probe: this pull request must not be mergeable.");
}
