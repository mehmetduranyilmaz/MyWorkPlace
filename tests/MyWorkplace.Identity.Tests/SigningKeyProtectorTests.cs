using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MyWorkplace.Identity.Tokens;

namespace MyWorkplace.Identity.Tests;

/// <summary>
/// EN: Envelope encryption of signing keys as plain logic (T-066, ADR-032): what is stored can't be read without the master
///     secret, and any change to it is refused.
/// TR: Saf mantık olarak imzalama anahtarlarının zarf şifrelemesi (T-066, ADR-032): saklanan şey ana sır olmadan okunamaz ve üzerindeki her
///     değişiklik reddedilir.
/// </summary>
public sealed class SigningKeyProtectorTests
{
    /// <summary>EN: A master secret for these tests. TR: Bu testler için bir ana sır.</summary>
    private const string Secret = "test-master-secret-0123456789abcdefghijklmnop";

    [Fact]
    public void ProtectedKey_RoundTrips()
    {
        using var rsa = RSA.Create(2048);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();
        var protector = ProtectorWith(Secret);

        var stored = protector.Protect("kid-1", pkcs8);

        Assert.Equal(pkcs8, protector.Unprotect("kid-1", stored));
    }

    [Fact]
    public void StoredBytes_AreNotAReadableKey_AndDontContainIt()
    {
        using var rsa = RSA.Create(2048);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();

        var stored = ProtectorWith(Secret).Protect("kid-1", pkcs8);

        using var attempt = RSA.Create();
        Assert.ThrowsAny<CryptographicException>(() => attempt.ImportPkcs8PrivateKey(stored, out _));
        Assert.Equal(-1, stored.AsSpan().IndexOf(pkcs8.AsSpan(0, 64)));
    }

    [Fact]
    public void WrongMasterSecret_CantRead()
    {
        var stored = ProtectorWith(Secret).Protect("kid-1", [1, 2, 3, 4]);

        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectorWith("another-master-secret-0123456789abcdefghijk").Unprotect("kid-1", stored));
    }

    [Fact]
    public void ChangedBytes_OrAnotherKeysId_AreRefused()
    {
        var protector = ProtectorWith(Secret);
        var stored = protector.Protect("kid-1", [1, 2, 3, 4]);
        var tampered = (byte[])stored.Clone();
        tampered[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("kid-1", tampered));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("kid-2", stored));
    }

    [Fact]
    public void SameKeyProtectedTwice_GivesDifferentBytes()
    {
        var protector = ProtectorWith(Secret);

        Assert.NotEqual(protector.Protect("kid-1", [1, 2, 3, 4]), protector.Protect("kid-1", [1, 2, 3, 4]));
    }

    /// <summary>
    /// EN: A protector with the given master secret.
    /// TR: Verilen ana sırla bir koruyucu.
    /// </summary>
    /// <param name="secret">EN: Master secret. TR: Ana sır.</param>
    /// <returns>EN: The protector. TR: Koruyucu.</returns>
    private static AesGcmSigningKeyProtector ProtectorWith(string secret) =>
        new(Options.Create(new SigningKeyOptions { MasterSecret = secret }));
}
