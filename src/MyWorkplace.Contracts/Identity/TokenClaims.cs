namespace MyWorkplace.Contracts.Identity;

/// <summary>
/// EN: Names and values of the claims in our access tokens (ADR-005). Issued by Identity, read by the gateway and
///     services — defined once here so a typo can't silently break authorization.
/// TR: Erişim token'larımızdaki claim'lerin adları ve değerleri (ADR-005). Identity üretir, gateway ve servisler okur —
///     tek yerde tanımlanır, böylece bir yazım hatası yetkilendirmeyi sessizce bozamaz.
/// </summary>
public static class TokenClaims
{
    /// <summary>EN: User id (standard JWT subject). TR: Kullanıcı kimliği (standart JWT subject).</summary>
    public const string Subject = "sub";

    /// <summary>EN: Company of the user. TR: Kullanıcının firması.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>EN: Plan of the company: <see cref="BasicPlan"/> or <see cref="ProPlan"/>. TR: Firmanın planı.</summary>
    public const string Plan = "plan";

    /// <summary>EN: One claim per effective permission (ADR-022). TR: Her etkin izin için bir claim (ADR-022).</summary>
    public const string Permission = "perm";

    /// <summary>EN: Value of <see cref="Plan"/> for Basic companies. TR: Basic firmalar için <see cref="Plan"/> değeri.</summary>
    public const string BasicPlan = "basic";

    /// <summary>EN: Value of <see cref="Plan"/> for Pro companies. TR: Pro firmalar için <see cref="Plan"/> değeri.</summary>
    public const string ProPlan = "pro";

    /// <summary>EN: Token issuer (<c>iss</c>). TR: Token'ı üreten (<c>iss</c>).</summary>
    public const string Issuer = "myworkplace-identity";

    /// <summary>EN: Token audience (<c>aud</c>). TR: Token'ın hedef kitlesi (<c>aud</c>).</summary>
    public const string Audience = "myworkplace-api";
}
