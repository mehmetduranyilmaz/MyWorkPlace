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

## Module building blocks (ADR-021)

- **Entities** inherit the smallest base that fits: `BusinessEntity` for business data (tenant-owned, audited,
  soft-deletable), `TenantOwnedEntity`, `AuditableEntity` or plain `Entity` otherwise. Don't repeat interface properties.
- **Responses** define one `static readonly Expression<Func<TEntity, TResponse>> Projection`, used by lists
  (`ToPagedResultAsync`), single reads (`SingleWithVersionAsync`) and `From` (compiled once). Never write the mapping twice.
- **`Program.cs`** calls `AddServiceModule<TContext>("x-db")`, `builder.Services.AddValidation()` (it must stay in the
  service) and `await app.UseServiceModuleAsync<TContext>()`, then maps the module's endpoints.
- **Design-time factory:** one line — `internal sealed class XDbContextDesignTimeFactory : ServiceDbContextDesignTimeFactory<XDbContext>;`

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

List endpoints bind `[AsParameters] PageQuery`, filter (search with `SearchPattern.Contains` + `EF.Functions.ILike`),
order by a business key **then `Id`**, and finish with `ToPagedResultAsync(selector, page, ct)`. It only accepts an
ordered query, so forgetting the order is a compile error (ADR-016).

Authorization is declared with **named policies** (`RequireAuthorization("...")`), never with role checks inside handlers.
Every business endpoint declares its **permission** from the catalog (ADR-022) — reads `Permissions.X.Read`,
create/update `Permissions.X.Write`, delete `Permissions.X.Delete`. A new module adds its permissions to
`Contracts.Identity.Permissions` (and to `All`) and to the role matrix in `Roles`.

## Audit logging

Never put `[AuditChanges]` on sensitive properties (password hashes, keys, tokens): their values would be copied into `audit_log`.

## Enforcement

`GenerateDocumentationFile` is on and warnings are errors, so a public member without an XML comment
**fails the build** (CS1591). Non-public members are checked in review.
