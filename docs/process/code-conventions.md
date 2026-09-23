# Code Conventions

Formatting and naming rules are enforced by [.editorconfig](../../.editorconfig) at build time.
This document covers what a tool can't fully enforce.

## Naming

All identifiers are **English**: namespaces, types, methods, properties, fields, parameters, variables.

## Bilingual documentation comments

Every **class, record, interface, enum, method, property and field** gets an XML documentation comment
with an **English** line and a **Turkish** line. They show up in IntelliSense tooltips in both
VS Code and Visual Studio.

```csharp
/// <summary>
/// EN: Issues signed access tokens for authenticated users.
/// TR: Kimliği doğrulanmış kullanıcılar için imzalı erişim token'ları üretir.
/// </summary>
public sealed class TokenIssuer(RsaKeyProvider keyProvider)
{
    /// <summary>
    /// EN: Lifetime of an access token.
    /// TR: Bir erişim token'ının geçerlilik süresi.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// EN: Creates a JWT carrying the user's tenant and plan.
    /// TR: Kullanıcının firma ve plan bilgisini taşıyan bir JWT oluşturur.
    /// </summary>
    /// <param name="user">EN: The signed-in user. TR: Giriş yapan kullanıcı.</param>
    /// <returns>EN: The encoded token. TR: Kodlanmış token.</returns>
    public string Issue(User user) { ... }
}
```

Guidelines:

- Explain **intent and why**, not what the code literally does (`// increments i` adds nothing).
- Keep each line short; one sentence per language is usually enough.
- Inline comments inside method bodies (`//`) follow the same rule when needed: `// EN: ... / TR: ...`.
- `Program.cs` top-level statements are documented with regular `//` comments in both languages.
- Test methods are exempt: their names (`Method_Scenario_ExpectedResult`) are their documentation.

## Endpoints

Every Minimal API endpoint has a bilingual summary and description; they appear in the OpenAPI document and Scalar.

```csharp
group.MapPost("/register", RegisterTenant.HandleAsync)
    .WithName("RegisterTenant")
    .WithSummary("EN: Register a company | TR: Firma kaydı")
    .WithDescription("EN: Creates a company on the Basic plan and its first user. " +
                     "TR: Basic planda bir firma ve ilk kullanıcısını oluşturur.");
```

API documentation is wired with `AddServiceApiDocs()` / `MapServiceApiDocs()` from BuildingBlocks, never per service:
every service gets the same OpenAPI document and Scalar UI (`/scalar`, Development only, C# samples by default).

Authorization is declared with **named policies** (`RequireAuthorization("...")`), never with role checks inside handlers.

## Audit logging

Never put `[AuditChanges]` on sensitive properties (password hashes, keys, tokens): their values would be copied into `audit_log`.

## Enforcement

`GenerateDocumentationFile` is on and warnings are errors, so a public member without an XML comment
**fails the build** (CS1591). Non-public members are checked in review.
