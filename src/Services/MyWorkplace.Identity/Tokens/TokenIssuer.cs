using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyWorkplace.Abstractions.Identity;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Identity.Domain;

namespace MyWorkplace.Identity.Tokens;

/// <summary>
/// EN: Creates short-lived RS256 access tokens (ADR-005). Tokens are signed, not encrypted: anyone can read the
///     claims, so nothing secret ever goes into them.
/// TR: Kısa ömürlü RS256 erişim token'ları üretir (ADR-005). Token'lar şifreli değil, imzalıdır: claim'leri herkes
///     okuyabilir, bu yüzden içlerine asla gizli bilgi konmaz.
/// </summary>
/// <param name="keys">EN: Signing keys. TR: İmzalama anahtarları.</param>
/// <param name="timeProvider">EN: Clock (UTC). TR: Saat (UTC).</param>
public sealed class TokenIssuer(SigningKeyProvider keys, TimeProvider timeProvider)
{
    /// <summary>
    /// EN: Token lifetime. Short on purpose: a plan change takes effect with the next token (ADR-006).
    /// TR: Token ömrü. Bilerek kısa: plan değişikliği bir sonraki token'la geçerli olur (ADR-006).
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>EN: Thread-safe token writer. TR: Thread-safe token yazıcısı.</summary>
    private static readonly JsonWebTokenHandler _handler = new();

    /// <summary>
    /// EN: Issues a token for <paramref name="user"/> of a company on <paramref name="plan"/>.
    /// TR: <paramref name="plan"/> planındaki bir firmanın <paramref name="user"/> kullanıcısı için token üretir.
    /// </summary>
    /// <param name="user">EN: Signed-in user. TR: Giriş yapan kullanıcı.</param>
    /// <param name="plan">EN: Plan of the user's company. TR: Kullanıcının firmasının planı.</param>
    /// <returns>EN: The encoded token and its lifetime. TR: Kodlanmış token ve ömrü.</returns>
    public IssuedToken Issue(User user, Plan plan)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ProductTokens.Issuer,
            Audience = ProductTokens.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(Lifetime),
            Claims = new Dictionary<string, object>
            {
                [TokenClaims.Subject] = user.Id.ToString(),
                [TokenClaims.TenantId] = user.TenantId.ToString(),
                [TokenClaims.Plan] = plan == Plan.Pro ? ProductTokens.ProPlan : ProductTokens.BasicPlan,
                // EN: One "perm" claim per effective permission; services check them locally (ADR-022).
                // TR: Her etkin izin için bir "perm" claim'i; servisler bunları yerelde kontrol eder (ADR-022).
                [TokenClaims.Permission] = user.EffectivePermissions.ToArray(),
            },
            SigningCredentials = new SigningCredentials(keys.Current, SecurityAlgorithms.RsaSha256),
        };

        return new IssuedToken(_handler.CreateToken(descriptor), (int)Lifetime.TotalSeconds);
    }
}

/// <summary>
/// EN: A freshly issued access token.
/// TR: Yeni üretilmiş bir erişim token'ı.
/// </summary>
/// <param name="AccessToken">EN: The encoded JWT. TR: Kodlanmış JWT.</param>
/// <param name="ExpiresIn">EN: Seconds until it expires. TR: Süresinin dolmasına kalan saniye.</param>
public sealed record IssuedToken(string AccessToken, int ExpiresIn);
