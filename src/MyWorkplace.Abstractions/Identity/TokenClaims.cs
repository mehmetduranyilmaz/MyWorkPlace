namespace MyWorkplace.Abstractions.Identity;

/// <summary>
/// EN: Names of the claims in access tokens (ADR-005). Issued by the identity service, read by the gateway and services —
///     defined once so a typo can't silently break authorization. The <i>values</i> (plan names, issuer) belong to the
///     product (ADR-026).
/// TR: Erişim token'larındaki claim'lerin adları (ADR-005). Kimlik servisi üretir, gateway ve servisler okur — tek yerde tanımlanır,
///     böylece bir yazım hatası yetkilendirmeyi sessizce bozamaz. <i>Değerler</i> (plan adları, issuer) ürüne aittir (ADR-026).
/// </summary>
public static class TokenClaims
{
    /// <summary>EN: User id (standard JWT subject). TR: Kullanıcı kimliği (standart JWT subject).</summary>
    public const string Subject = "sub";

    /// <summary>EN: Company of the user. TR: Kullanıcının firması.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>EN: Plan of the company. TR: Firmanın planı.</summary>
    public const string Plan = "plan";

    /// <summary>EN: One claim per effective permission (ADR-022). TR: Her etkin izin için bir claim (ADR-022).</summary>
    public const string Permission = "perm";
}
