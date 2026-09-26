# Task Board

Conventions: [process/task-conventions.md](process/task-conventions.md)

---

## Milestone — Plug-and-play core

**Definition (agreed with the owner):** the core is complete when a new module can be added by writing **only**
its entities, business rules, endpoints and tests — everything else comes from the core.

**Proof:** a new module (Products, T-013) is added by following the module guide **without changing a single line
of core code** (Abstractions, BuildingBlocks, ServiceDefaults, Gateway code). If the core has to change,
it is not done.

**Allowed registration points** (declarations, not core changes — ADR-025): the new project and its line in the
solution, its AppHost registration, its route in the gateway's `appsettings.json`, and **additions** to Contracts —
the module's permission class and its events. No existing line in Contracts changes.

**When every row below is Done, tell the owner explicitly that the core is complete** (the owner asked for this).

| Capability a new module gets for free | Status | Task |
| --- | --- | --- |
| Token validation, secure by default, plan policies (gateway + service) | Done | T-007, T-008 |
| Tenant isolation, audit fields, change history, soft delete, concurrency | Done | T-024, T-008 |
| ProblemDetails errors, API docs (Scalar) | Done | T-024, T-006 |
| Reference module to copy: CRUD, ETag concurrency, validation, tenant isolation tests | Done | T-009 |
| Paging and search standard | Done | T-028 |
| Roles and permissions: a module declares who may call which endpoint | Done | T-025, T-037 |
| Tenant settings: business rules that vary per company are parameters with defaults, not code | Done | T-032 |
| Cross-service events (RabbitMQ + outbox) | Done | T-015, T-016, T-017 |
| No module boilerplate: base entity, one-line service setup, shared mappings | Done | T-036 |
| Module guide: step-by-step recipe for adding a module | Done | T-027 |
| **Proof: Products added via the guide with zero core changes** | Done | T-013 |

---

## Sprint 0 — Foundation

Goal: an empty but professional skeleton. Everything builds and starts with one command.

### T-001 — Architecture and process docs

- **State:** Done
- **Acceptance criteria:**
  - [x] `CLAUDE.md`, `docs/architecture.md`, `docs/process/*` written
  - [x] Approved by the owner
- **Notes:** Project named MyWorkplace. Committed docs are English; Turkish mirrors live locally under `tr/`.

### T-002 — Git repository and base files

- **State:** Done
- **Acceptance criteria:**
  - [x] `git init`, `main` branch
  - [x] `.gitignore` (including `tr/` and local Claude settings), `.gitattributes`, `.editorconfig`
  - [x] `LICENSE` (MIT)
  - [x] English + Turkish README (purpose, architecture diagram, how to run)
- **Notes:** README split into `README.md` (EN) and `README.tr.md` (TR) with a language switch.
  Diagram in Mermaid (rendered by GitHub). `tr/` verified as ignored with `git check-ignore`.

### T-003 — Solution skeleton

- **State:** Done
- **Acceptance criteria:**
  - [x] `MyWorkplace.slnx`, `Directory.Build.props`, `Directory.Packages.props`
  - [x] AppHost, ServiceDefaults, Gateway and one empty sample service
  - [x] `dotnet build` passes with no warnings
  - [x] Runs from VS Code (`launch.json` / `tasks.json`) and Visual Studio
  - [x] "Commands" section in `CLAUDE.md` filled in
- **Notes:**
  - Sample service is Customers with a temporary `GET /customers/info`; the gateway routes `/customers/*` to it
    via Aspire service discovery. Covered by an integration test that starts the real system.
  - Added (not in the original scope): bilingual XML doc comments enforced by the build (CS1591),
    `global.json` pinning the SDK and the test runner, `.gitattributes` switched to LF everywhere.
  - Aspire 13.5 requires the Aspire CLI bundle; documented in `CLAUDE.md` and README.
  - xUnit v3 runs on Microsoft.Testing.Platform; VSTest packages removed.

### T-004 — CI (GitHub Actions)

- **State:** Done
- **Acceptance criteria:**
  - [x] Build and tests run on every push and PR
  - [x] Status badge in README
- **Notes:** `.github/workflows/ci.yml` on `ubuntu-latest`: installs the Aspire CLI bundle, trusts the dev
  certificate, builds in Release and runs the integration test. Read-only permissions; outdated runs are cancelled.
  Verified locally with the same commands. Remote: `github.com/mehmetduranyilmaz/MyWorkPlace`.

### T-005 — Claude Code project commands

- **State:** Done
- **Goal:** Turn recurring rituals into commands (like "modus" in the reference article).
- **Acceptance criteria:**
  - [x] `/task T-xxx` → pick up the task, move to In Progress, create branch
  - [x] `/ship` → verify, commit (with approval), move to In Review, write summary
  - [x] `/refine T-xxx` → clarify acceptance criteria
- **Notes:** Commands live in `.claude/commands/` and are documented in `docs/process/workflow.md`.
  `/task` stops after proposing a plan; `/ship` stops before merging; both keep the human checkpoints.
  `/ship` also pushes and checks CI after approval, which the original criteria didn't include.
  This task itself was shipped by following `/ship` manually (commands load in the next session).

---

## Sprint 1 — MVP: Basic / Pro end to end

Goal: "A Basic tenant can't access Inventory, a Pro tenant can" works against the real system.
Remaining order: **T-011 → T-010 → T-012**.

### T-024 — Building blocks: shared persistence conventions

- **State:** Done
- **Goal:** Shared technical infrastructure every service's data layer builds on (ADR-011, ADR-014).
- **Acceptance criteria:**
  - [x] `MyWorkplace.BuildingBlocks` project with `Entity` (UUID v7 `Id`), `ITenantOwned`, `IAuditable`,
        `ISoftDeletable`, `[AuditChanges]`, `ICurrentUser` — and no domain types
  - [x] Model conventions applied automatically: tenant filter for `ITenantOwned`, soft-delete filter for
        `ISoftDeletable`, `xmin` concurrency token, `snake_case` naming, `NoTracking` by default
  - [x] Auditing interceptor fills `CreatedAt/By` and `UpdatedAt/By` from `TimeProvider` and `ICurrentUser`;
        sets `TenantId` on insert and rejects any later change to it
  - [x] Change-history interceptor writes one `audit_log` row per changed `[AuditChanges]` property
        (entity, id, property, old value, new value, user, tenant, time) in the same transaction; unmarked properties are not logged
  - [x] `FindForUpdateAsync` loads a tracked entity; a concurrency conflict becomes a `409` ProblemDetails
  - [x] Shared ProblemDetails setup for `400` (field errors), `401`, `403`, `404`, `409`
  - [x] Tests against a **real PostgreSQL** prove: tenant A can't read tenant B's rows; soft-deleted rows are hidden;
        audit fields are filled; only marked properties are logged; a stale update fails with a concurrency error;
        queries are untracked by default
- **Notes:**
  - Base `ServiceDbContext` seals `OnModelCreating`: services describe their model in `ConfigureModel`, then the
    conventions are applied on top, so no service can skip them. `AddServiceDbContext<T>("name")` wires everything.
  - Filters are **named** (EF 10): `IgnoreQueryFilters([QueryFilters.SoftDelete])` shows deleted rows while the
    tenant filter stays active.
  - Soft delete: `Remove()` becomes an update. `[AuditChanges]` logs modifications only (not inserts).
  - `xmin` is a shadow property (`Version`); it can be exposed later for HTTP ETags.
  - 400 field errors: this task provides the ProblemDetails format; the validation itself (.NET 10 built-in
    Minimal API validation) is wired per service, starting with T-006.
  - Tests: 15 against a real PostgreSQL (Testcontainers) and an in-memory test server. Tests now need Docker.
  - Added to conventions: bilingual `WithSummary`/`WithDescription` on endpoints, named authorization policies,
    no `[AuditChanges]` on sensitive data. Roles and permissions went to the backlog as T-025.

### T-006 — Identity: service, database and tenant sign-up

- **State:** Done
- **Goal:** A company can sign up; the first real service with its own PostgreSQL database.
- **Acceptance criteria:**
  - [x] PostgreSQL runs in the AppHost with an `identity-db` database; the Identity service is registered and
        the gateway routes `/identity/*` to it
  - [x] `Tenant` (name, plan) and `User` (email, password hash, tenant) entities built on BuildingBlocks;
        the initial migration is committed and applied at startup in Development (ADR-015)
  - [x] `POST /identity/register` with `{ companyName, email, password }` → `201` with `{ tenantId, userId }`;
        the new tenant's plan is **Basic**
  - [x] `400` ProblemDetails with field errors for: missing company name, invalid email, password shorter than 8
  - [x] `409` when the email is already registered — case-insensitive (`A@x.com` equals `a@x.com`)
  - [x] Passwords are stored only as `PasswordHasher` hashes (ADR-013): a test asserts the stored value is not the
        password and verifies against it
  - [x] Integration tests through the gateway cover `201`, `400` and `409`
- **Notes:**
  - Vertical slice: `Features/Register/` holds the request, validation attributes and handler.
  - Uniqueness is guaranteed by a unique index on `normalized_email`; a unique-violation (`23505`) from a racing
    sign-up is also mapped to `409`. The pre-check only gives a fast answer.
  - The duplicate check deliberately bypasses the tenant filter (`IgnoreQueryFilters([QueryFilters.Tenant])`).
  - Audited properties: `Tenant.Name`, `Tenant.Plan`, `User.Email`. `PasswordHash` is never audited.
  - `MigrateDatabaseAsync<T>()` added to BuildingBlocks for every service; `dotnet-ef` pinned as a local tool.
  - AppHost: PostgreSQL with a data volume and PgWeb for development; tests pass `Storage:Ephemeral=true`
    for a fresh database and no PgWeb. Scalar UI at `/scalar` on the Identity service in Development.
  - API docs setup moved to BuildingBlocks (`AddServiceApiDocs` / `MapServiceApiDocs`) after review, so every
    service shows C# code samples by default (owner's request).
  - Integration tests now share one running system (`AppFixture`) instead of starting it per test.
    The test HTTP client has no retry handler, so a POST is never sent twice.

### T-023 — Identity: login, signing key, JWT and JWKS

- **State:** Done
- **Goal:** Registered users get a signed token that any service can verify on its own (ADR-005, ADR-012).
- **Acceptance criteria:**
  - [x] An RSA key is generated on first start, stored in `identity-db` with a `kid`, and reused after a restart
  - [x] `POST /identity/login` with `{ email, password }` → `200` with `{ accessToken, expiresIn: 900 }`;
        the token is RS256-signed, carries the `kid`, and has `sub`, `tenant_id`, `plan` and a 15-minute `exp`
  - [x] Wrong email and wrong password return **identical** `401` ProblemDetails (ADR-013)
  - [x] `GET /identity/.well-known/jwks.json` publishes the public key(s) and no private key material
  - [x] An integration test validates an issued token using only the JWKS response
- **Notes:**
  - `SigningKeyProvider` (singleton) loads the keys at startup, creating the first one on first start; the newest
    key signs, every key is published. `kid` is the key's UUID v7.
  - JWKS is built by hand from public RSA parameters only (`n`, `e`), so private fields can't leak by accident.
  - Added a minimal OpenID discovery document; `iss = myworkplace-identity`, `aud = myworkplace-api`.
    Claim names and plan values moved to `BuildingBlocks.Identity.TokenClaims` for the gateway and services.
  - Unknown emails verify a decoy hash (same timing as a wrong password); `SuccessRehashNeeded` re-hashes at sign-in.
  - Build found an EF Core version conflict (10.0.11 via Npgsql vs 10.0.12 via EF Design); fixed at the root with
    central **transitive pinning**, not by suppressing the warning.
  - New `MyWorkplace.Identity.Tests` project (Testcontainers + real migrations) for the key-reuse test;
    `IdentityApi` test helper shared by sign-up and sign-in integration tests.

### T-007 — Gateway: routing, authentication and plan policy

- **State:** Done
- **Acceptance criteria:**
  - [x] YARP route `/inventory/*` (`/identity/*` and `/customers/*` exist from earlier tasks)
  - [x] JWT validated via JWKS
  - [x] `ProPlan` policy: Basic token on a Pro route → 403
- **Notes:**
  - Secure by default: fallback policy requires a valid token; sign-up, sign-in and `/.well-known/*` are explicit
    `anonymous` YARP routes; `/inventory/*` uses `pro-plan`. 401/403 from the gateway are ProblemDetails.
  - JwtBearer reads Identity's discovery document through service discovery and caches the keys;
    `MapInboundClaims = false` keeps `sub`, `tenant_id`, `plan` as issued.
  - New `MyWorkplace.Contracts` project (no dependencies): `TokenClaims` moved there from BuildingBlocks, plus
    `PolicyNames`. The gateway references only Contracts — no database packages.
  - Inventory skeleton service (`GET /inventory/info`) added so the Pro route is real; T-010 fills it.
  - Health endpoints marked anonymous in ServiceDefaults, otherwise Aspire's probes would get 401.
  - The whole IdentityModel package family pinned to one version (mixed versions fail only at runtime).
  - Tests: no token → 401, tampered signature → 401, Basic on `/inventory` → 403, public routes stay open.
    Pro → 200 needs a Pro tenant, so it comes with T-011/T-012.

### T-026 — Pin the PostgreSQL version in one place

- **State:** Done
- **Goal:** Development and tests run the same PostgreSQL version, and an Aspire update can't change it silently.
- **Acceptance criteria:**
  - [x] The PostgreSQL image and tag are defined once and used by the AppHost and every Testcontainers fixture
  - [x] The AppHost sets the tag explicitly (`WithImageTag`) instead of relying on Aspire's default
  - [x] The wrong "same major version" comment in the BuildingBlocks test fixture is gone
  - [x] All tests pass on the pinned version
- **Notes:** Found while reviewing Docker Desktop: Aspire ran `postgres:18.3`, tests ran `postgres:17-alpine`.
  Fixed with one linked source file, `eng/PostgresImage.cs` (tag `18.3`), compiled into the AppHost and both
  Testcontainers test projects — no new project dependency just for a constant.

### T-008 — Shared: tenant context and plan check (service side)

- **State:** Done
- **Acceptance criteria:**
  - [x] `ICurrentUser` is filled from the validated JWT (`sub`, `tenant_id`, `plan`), activating the tenant filter from T-024
  - [x] In-service plan check (defense in depth)
- **Notes:**
  - Token validation and policies moved from the gateway into ServiceDefaults (`AddTokenAuthentication()`), used by
    the gateway, Customers and Inventory — one definition, so the two layers can't drift apart.
  - `HttpCurrentUser` is now the default `ICurrentUser` in BuildingBlocks: every service with a `ServiceDbContext`
    gets the signed-in user (and the tenant filter) without extra wiring. `ICurrentUser` gained `Plan`.
  - Customers requires a signed-in user; Inventory's endpoint group requires `pro-plan`. Scalar/OpenAPI endpoints are
    explicitly anonymous. Identity's own protected endpoints (and its authentication) come with T-011.
  - Tests: services called **directly, bypassing the gateway** (Aspire test client): no token → 401 on both,
    Basic token on Inventory → 403, valid token on Customers → 200. Claim reading and the tenant filter driven by a
    real token principal are proven against PostgreSQL.
  - A test first failed because `HttpContextAccessor` keeps the request in a static AsyncLocal shared by all
    instances; the test now uses its own fixed accessor. Production behavior was correct.

### T-009 — Customers: data and CRUD (the reference module)

- **State:** Done
- **Goal:** The first business module, built as the template every later module copies (ADR-016, ADR-017).
- **Acceptance criteria:**
  - [x] `customers-db` in the AppHost; Customers uses BuildingBlocks; the temporary `/customers/info` is removed
  - [x] `Customer` (tenant-owned, auditable, soft-deletable): `Name` required ≤ 200; `Email` optional, valid, ≤ 320;
        `Phone` optional ≤ 30; `TaxNumber` optional ≤ 20; `Notes` optional ≤ 2000. `[AuditChanges]` on Name, Email,
        Phone, TaxNumber (not Notes). Initial migration committed
  - [x] `POST /customers` → `201` with `Location` and `ETag`; `400` field errors for the rules above
  - [x] `GET /customers/{id}` → `200` with `ETag`; unknown id or another tenant's customer → `404`
  - [x] `PUT /customers/{id}` (full update) with `If-Match` → `200` with the new `ETag`; stale version → `412`;
        missing `If-Match` → `428`; another tenant's customer → `404`
  - [x] `DELETE /customers/{id}` → `204` (soft delete); a later `GET` → `404`
  - [x] Email unique within a tenant, case-insensitive, ignoring deleted customers → `409`; the same email in another
        tenant is allowed; an email of a deleted customer can be reused
  - [x] Integration tests through the gateway cover every status above, tenant B's `404` on tenant A's customer for
        get / update / delete, and a change-history row for an audited field
- **Notes:**
  - Layout to copy: `Domain/Customer.cs`, `Persistence/` (context, design-time factory, migrations),
    `Features/` (one file per endpoint + `CustomerContracts.cs` with input, response DTO and shared problems), `Program.cs`.
  - The entity guards its own consistency: setters are private and `Update(...)` trims values, turns blanks into null
    and keeps `NormalizedEmail` in sync with `Email`.
  - Reads are untracked and project the row version for the ETag; writes use `FindForUpdateAsync` + `ExpectVersion`,
    so a concurrent change between the check and the save still ends in `412`.
  - Added to BuildingBlocks for every module: `ETags` (ETag / If-Match / 412 / 428) and `ConcurrencyExtensions`;
    `PostgresErrors.IsUniqueViolation()` — Identity now uses it too.
  - Filtered unique index `(tenant_id, normalized_email) WHERE normalized_email IS NOT NULL AND is_deleted = false`.
  - The temporary `/customers/info` is gone; routing, authorization and defense-in-depth tests now use real endpoints.
  - 16 new integration tests; 58 tests in total.

### T-028 — Paging and search standard

- **State:** Done
- **Goal:** One list format for every module, delivered by BuildingBlocks and applied to Customers first (ADR-016).
- **Acceptance criteria:**
  - [x] BuildingBlocks: `PagedResult<T>` (`items`, `page`, `pageSize`, `totalCount`) and paging parameters:
        `page` ≥ 1 (default 1), `pageSize` 1–100 (default 20); out-of-range values → `400`
  - [x] `GET /customers?page=&pageSize=&search=`: search in name, email and phone, case-insensitive;
        sorted by name, then id (stable order across pages)
  - [x] Tests: page arithmetic and `totalCount`, bounds → `400`, search, only the caller's tenant is listed and counted
- **Notes:**
  - BuildingBlocks: `PageQuery` (validated with `[Range]`/`[MaxLength]` → automatic 400), `PagedResult<T>`,
    `ToPagedResultAsync` and `SearchPattern`.
  - `ToPagedResultAsync` takes an `IOrderedQueryable`: paging an unordered query does not compile. Projection happens
    after `Skip`/`Take`, so only one page of rows is materialized.
  - A page beyond the end returns `200` with empty items (no offset overflow even for `page=int.MaxValue`).
  - `%`, `_` and `\` in search text are escaped and matched literally.
  - Tests: 7 new BuildingBlocks tests (page slices, stable order with equal sort values, overflow, literal wildcards)
    and 6 new integration tests; 72 in total. A search test first failed twice because its expected order was wrong —
    it now compares sets, since ordering has its own test.

### T-010 — Inventory: stock items (the first Pro module)

- **State:** Done
- **Goal:** Stock items built from the reference module, standalone until Products exists (ADR-019).
  **Start after T-011**: the tests need a Pro tenant.
- **Acceptance criteria:**
  - [x] `inventory-db` in the AppHost; Inventory uses BuildingBlocks; the temporary `/inventory/info` is removed
  - [x] `StockItem` (tenant-owned, auditable, soft-deletable): `Sku` required ≤ 50, unique per tenant, case-insensitive,
        ignoring deleted items (`409`); `Name` required ≤ 200; `BaseUnit` one of the default unit codes
        (`PCS`, `KG`, `L`, `M`, `BOX`, `PACK` — a catalog comes with T-031); `Quantity` decimal(18,3), starts at 0,
        read-only through this API (changed only by movements, T-030). `[AuditChanges]` on Sku, Name, BaseUnit
  - [x] `POST`, `GET`, `PUT` (If-Match), `DELETE`, and a paged list searching SKU and name, sorted by SKU then id —
        exactly as ADR-016 / ADR-017
  - [x] `DELETE` of an item whose quantity is not 0 → `409`
  - [x] Every endpoint requires the Pro plan: a Basic token → `403` through the gateway and directly
  - [x] Integration tests with a Pro tenant (upgraded via T-011) cover the statuses above and tenant isolation (`404`)
- **Notes:**
  - Routes: `/inventory/items` (Inventory will have several resources). The `/inventory` group keeps `pro-plan`.
  - Base unit validated with `[AllowedValues]` until the catalog (T-031). Quantity is `numeric(18,3)` and read-only.
  - **Friction log — copying the reference module** (input for the module guide T-027 and for T-036):
    1. `Program.cs`: ~20 identical lines (defaults, auth, db, problems, validation, docs, middleware order,
       migration) — easy to get the middleware order wrong. **Core gap.**
    2. Every entity repeats 7 interface properties (TenantId, 4 audit, 2 soft-delete). **Core gap.**
    3. Design-time factory is identical except for names. **Core gap.**
    4. Entity → response mapping written three times (From, Get projection, List projection); they can drift. **Core gap.**
    5. Create/Update "pre-check + catch unique violation" pattern repeated — acceptable, documented pattern.
    6. Outside the core (fine, but the guide must list them): AppHost database + project + references; gateway route
       when the module has a new prefix; `dotnet ef migrations add`.
    7. Test helper `UpdateAsync` was tied to `/customers`; generalized to `PutWithIfMatchAsync` for every module.
  - Items 1–4 mean a new module still needs boilerplate the core could provide → T-036 added to Sprint 2.
  - 11 new integration tests; 86 in total.

### T-011 — Plan upgrade

- **State:** Done
- **Goal:** A Basic company can move to Pro and use Pro modules right away (ADR-006).
- **Acceptance criteria:**
  - [x] Identity validates its own tokens with the keys it holds in memory — no network call to itself; register,
        login and `/.well-known/*` stay explicitly anonymous; everything else is secure by default
  - [x] `POST /identity/tenant/upgrade` by a signed-in user → `200` with `{ plan: "pro", accessToken, expiresIn }`;
        the new token carries `plan = pro`; the tenant is Pro in the database
  - [x] Upgrading a company that is already Pro → the same `200` (idempotent), and no second change-history row
  - [x] No token → `401`
  - [x] The change is in the change history: `Plan` from `Basic` to `Pro`, with the upgrading user
  - [x] Integration test through the gateway: the new token reaches a Pro route (`200`); the old Basic token still
        gets `403` until it expires (ADR-006, accepted trade-off)
- **Notes:** Downgrade (Pro → Basic) is out of scope (T-035). Until roles exist (T-025) any user of the company may upgrade.
  The company comes from the validated token, never from the request. A concurrent upgrade (xmin conflict) is treated
  as "already Pro". Public Identity endpoints now declare `AllowAnonymous()` themselves. `CreateProClientAsync()`
  test helper added for Pro tests (T-010, T-012). 3 new integration tests; 75 in total.

### T-012 — End-to-end integration tests

- **State:** Done
- **Goal:** Prove the Sprint 1 goal end to end, and keep it readable as living documentation.
- **Acceptance criteria:**
  - [x] Basic → Inventory: 403 — already covered by `GatewayAuthorizationTests`, `DefenseInDepthTests`, `StockItemTests`
  - [x] Pro → Inventory: 200 — already covered by `PlanUpgradeTests`, `StockItemTests`
  - [x] No token → 401 — already covered by `GatewayAuthorizationTests`, `DefenseInDepthTests`
  - [x] One scenario test tells the Sprint 1 story in order: sign up (Basic) → add a customer → Inventory refused →
        upgrade → add a stock item → another company sees none of it
- **Notes:** The three original criteria were met by earlier tasks; duplicating those tests would add upkeep without
  value, so this task adds the story-level scenario instead.

---

## Sprint 2 — Plug-and-play core

Goal: reach the milestone above. Tasks are refined with `/refine` before they start.

- **T-025** — Roles and permissions: infrastructure and enforcement (ADR-022) — **Done**
  - Goal: every endpoint declares which permission it needs; the token carries what the user may do.
  - [x] Permission catalog in Contracts (`customers.read|write|delete`, `inventory.read|write|delete`, `users.manage`,
        `settings.manage`, `plan.manage`) and the default roles Owner, Admin, Member, Viewer with the agreed matrix, in code
  - [x] Identity stores user roles and per-user extra permissions (grant only); the first user of a new company is Owner;
        existing users are backfilled as Owner by the migration
  - [x] Login and upgrade tokens carry the user's effective permissions (roles ∪ extras) as `perm` claims
  - [x] `RequireAuthorization(Permissions.X)` works for every permission without registering each policy
        (a policy provider in ServiceDefaults)
  - [x] Applied: Customers and Inventory reads → `*.read`, create/update → `*.write`, delete → `*.delete`;
        plan upgrade → `plan.manage`. The gateway keeps only token + plan checks
  - [x] Tests: the first user's token carries every permission; policy tests prove a principal with / without a permission
        is allowed / refused (`403`); all existing integration tests stay green
  - Notes: roles and extra permissions are `text[]` columns on `users`. The migration's data step makes pre-existing
    users Owner — proven by a test that upgrades a database from the previous migration (integration tests start empty
    and could never catch it). `PermissionPolicyProvider` builds policies on demand and returns none for names outside
    the catalog, so a typo fails loudly. Change history now compares collections by content and logs them as
    "Admin, Member". Refused-by-role integration tests come with T-037 (a second user is needed). 8 new tests; 99 in total.
- **T-037** — User management (ADR-022) — **Done**
  - Goal: a company can have more than one user, and the roles of T-025 visibly work.
  - [x] `/identity/users` (all `users.manage`): add a user with an initial password and roles (default Member), list
        (paged, email search), get with ETag, replace roles and extra permissions with `If-Match`, remove
  - [x] Unknown role or permission names → `400`; email already registered → `409`; another company's user → `404`
  - [x] The last Owner can't be demoted or removed (`409`); concurrent changes are serialized by a company-row lock
  - [x] No privilege escalation: only an Owner touches Owners or grants Owner; nobody grants what they don't have (`403`)
  - [x] Removal is a soft delete: the user can't sign in and the email can be reused
  - [x] Integration tests for refused actions per role: Member delete → `403`, Viewer create → `403`,
        Admin upgrade → `403`, Member user management → `403`, Viewer write on Pro inventory → `403`
  - Notes: `User` is now a `BusinessEntity` (soft delete); the email index became a filtered unique index. The race test
    (two Owners demoting each other at once) was checked to fail with the lock removed. 21 new tests; 120 in total.
- **T-032** — Tenant settings (ADR-018) — **Done**
  - Goal: business rules that vary per company are parameters a permitted user changes, with defaults in code.
  - [x] BuildingBlocks capability: a module declares a settings class with defaults and exposes it with one line;
        code reads the current tenant's values through one service (used by T-030)
  - [x] Stored per tenant and module as one `jsonb` row in the module's database; adding a setting needs no migration;
        no row → code defaults; a stored document missing a property → that property's default
  - [x] First setting: `InventorySettings.NegativeStockPolicy` — `Block` (default), `Allow`, `Warn`
  - [x] `GET /inventory/settings` (`inventory.read`) returns the values with an ETag; `PUT` (`settings.manage`)
        replaces them with `If-Match`: `428` without it, `412` if stale — also when two first saves race;
        an invalid value → `400`
  - [x] Changes are recorded in the change history (old and new values)
  - [x] Tests: defaults for a new company, update visible on the next read, tenant isolation, `428` / `412` / `400`,
        Member `PUT` → `403`, Viewer `GET` → `200`, Basic plan → `403`, missing property → default
  - Notes: `tenant_settings` lives in every service's database like `audit_log` (one empty-table migration each for
    Identity and Customers). A never-saved company has ETag `"0"`; the unique (tenant, module) index settles racing
    first saves. PostgreSQL reformats `jsonb`, so values are compared by meaning — saving the same values writes no
    history. The `PUT` handler reads the body itself: a parameter typed by a generic argument crashes the ASP.NET route
    analyzer (AD0001), and reading it ourselves gives a field-level `400` for unknown values. Enums now travel as names
    in every service. 12 new tests; 132 in total.
- **T-015** — Messaging building block (ADR-007, ADR-023) — **Done**
  - Goal: services can publish and consume events reliably; the first real use comes with T-014 / T-016.
  - [x] Licence and maintenance status of Wolverine re-checked and recorded in ADR-023
  - [x] RabbitMQ in the AppHost; services opt in with one line in the standard setup
  - [x] Publishing goes through the transactional outbox in the service's own database
  - [x] Events live in `MyWorkplace.Contracts/Events` and carry an id and a `TenantId`
  - [x] A consumer runs as a system actor of the event's tenant: the tenant filter shows only that tenant's rows
  - [x] Tests (BuildingBlocks, real PostgreSQL + RabbitMQ containers): rolled-back change → no message;
        committed change → delivered; committed while RabbitMQ is down → delivered after it returns;
        the same event delivered twice → processed once; consumer sees only the event's tenant
  - Notes: the broker-outage test first passed for the wrong reason — Wolverine handed events to a handler in the same
    process in memory, so nothing went through RabbitMQ; local routing is now off and every test goes through the
    broker. Wolverine opens its transaction when an event is published, which the retrying execution strategy refuses,
    so `IEventOutbox` collects events and hands them over inside the strategy (tested with retries on). The current
    user is now `ServiceCurrentUser`: the token's user in requests, the tenant's system actor while an event is
    processed. RabbitMQ is pinned in `eng/RabbitMqImage.cs`. 5 new tests (stable over three runs); 137 in total.
- **T-014** — Orders service (Basic, ADR-024) — **Done**
  - Goal: companies record orders, and placing one publishes the first real cross-service event.
  - [x] New service with its own `orders-db`, in the AppHost and behind the gateway at `/orders` (Basic plan)
  - [x] Permissions `orders.read` / `orders.write` / `orders.delete` in the catalog and the role matrix
  - [x] Draft orders: create, list (paged, newest first), get with ETag, replace with `If-Match`, delete; lines carry
        SKU, name, quantity (> 0), unit price (≥ 0); optional `CustomerId` + customer name snapshot; at least one line
  - [x] Line totals and the order total are computed by the server
  - [x] `POST /orders/{id}/place`: draft → `Placed` with the next per-company number (1001, 1002, …; never duplicated,
        also when two orders are placed at once) and `OrderPlaced` (order id, number, lines) published through the outbox
  - [x] A placed order can't be changed, deleted or placed again (`409`)
  - [x] Tests: create / read / update / delete a draft, validation (`400`), stale ETag (`412`), tenant isolation (`404`),
        role refusals (Viewer create → `403`, Member delete → `403`), placing (number, status, `409` afterwards),
        concurrent placing gets distinct numbers, `OrderPlaced` is published with the order's lines
  - Notes: two things the tests caught. The validation source generator silently skipped rules on the elements of an
    array — line rules only work because `Lines` is a `List` (now a code convention). And soft-deleting an owner
    physically deleted its owned parts (EF marks them deleted with the owner) — fixed in the core, with a test shown to
    fail without the fix. Also: quantities / prices with more than 3 / 2 decimals are rejected instead of being rounded
    silently; placing requires `If-Match`; the `OrderPlaced` test reads the event from RabbitMQ through its own queue;
    exchanges are named after the event type. While documenting, `code-conventions.md` turned out to have been
    corrupted by scripted edits in T-032 / T-015 (the "Events" rule had never landed) — repaired here. 12 new tests;
    149 in total.
- **T-016** — `OrderPlaced` → Inventory decreases stock (ADR-020, ADR-023) — **Done**
  - Goal: placing an order decreases stock in Inventory without any call between the two services.
  - [x] Minimal movement model: append-only `StockMovement` (`Out`, quantity, reason `Order`, order id and number,
        negative-stock flag); the balance changes only through a movement, in the same transaction
  - [x] Inventory consumes `OrderPlaced`: each line's SKU is matched to a stock item of the event's company
        (case-insensitive, like the SKU uniqueness rule); quantity is in the item's base unit
  - [x] The `Out` is always applied, even below zero; a movement that takes the balance below zero is flagged
  - [x] Lines with an unknown SKU are skipped with a warning log; the rest of the order is still applied
  - [x] The same `OrderPlaced` delivered twice decreases stock once (building block, ADR-023)
  - [x] Tests (through the gateway, end to end): a Pro company's item goes from 0 to −2.5 after an order of 2.5 and the
        movement is flagged; another company's item with the same SKU is untouched; an unknown SKU is skipped while the
        other line applies; a Basic company's order changes nothing
  - Notes: `StockLedger` changes a balance with one atomic `UPDATE … SET quantity = quantity - @q`; the event dispatcher
    now wraps the handler in one explicit transaction (core change), so that update, the movement and the "processed"
    mark commit together. An extra test places five orders for one item at once: −5 with five movements. With a
    read-then-write version swapped in, the same test ended at −1 — Inventory does process events in parallel, and the
    atomic update is what keeps the balance right. Items are issued in id order to rule out deadlocks. The Basic-company
    test waits for `processed_events` first, so "nothing changed" can't just mean "not processed yet". 5 new tests;
    154 in total.
- **T-017** — Resilience: orders are accepted while Inventory is down; stock catches up when it returns (ADR-007) — **Done**
  - Goal: prove, on every CI run, that one service being down doesn't stop the others.
  - [x] Integration test with its own system instance (other tests share one and must not see Inventory go down):
        stop Inventory → `/inventory` answers `5xx` through the gateway within seconds (no hang) → orders are still
        created and placed (`200`) → start Inventory → every missed order decreases stock within 60 seconds
  - [x] README (English and Turkish): "Try it yourself" steps to watch the same scenario in the Aspire dashboard
  - [x] ADR-023 records the known limit: a queue exists only once its consumer has started
  - Notes: the test stops and starts Inventory with Aspire's resource commands on an `IsolatedAppFixture` (a second
    copy of the system; Aspire randomizes ports and container names, so both run side by side). Its first run failed
    for a real reason: the gateway waited 15 seconds — YARP's default connect timeout — before answering for the
    stopped service. The gateway now gives up connecting after 3 seconds (ADR-007). The whole suite takes about
    40 seconds longer for the second system. 1 new test; 155 in total.
- **T-036** — Module boilerplate into the core (ADR-021) — **Done**
  - Goal: a new module writes only its entities, rules, endpoints and tests; the recurring setup comes from BuildingBlocks.
  - [x] Base classes `AuditableEntity` → `TenantOwnedEntity` → `BusinessEntity` in BuildingBlocks; Tenant and SigningKey,
        User, Customer and StockItem use them (the interfaces stay: filters and interceptors rely on them)
  - [x] `AddServiceModule<TContext>(connection)` and `UseServiceModuleAsync<TContext>()` own the standard setup and the middleware
        order; Identity, Customers and Inventory use them. `AddValidation()` stays in each service (source generator, ADR-021)
  - [x] Generic `ServiceDbContextDesignTimeFactory<TContext>`; each service keeps a one-line subclass
  - [x] Each response has one `Projection` expression used by lists, single reads (`SingleWithVersionAsync`) and `From`;
        BuildingBlocks tests cover `SingleWithVersionAsync` (value + version, not found, other tenant)
  - [x] **Behavior preserved:** all 87 existing tests pass unchanged, and `dotnet ef migrations add` produces an empty
        migration for every service (removed after the check)
  - Notes: services lost 169 lines net (247 removed, 78 added); each `Program.cs` is 12 lines shorter. Proof of a pure
    refactoring: the 87 existing tests pass with zero changes under `tests/`, and a schema-check migration was empty for
    all three services (created, inspected, deleted — `migrations remove` needs a live database, so the files were
    removed by hand and the snapshots restored). 4 new BuildingBlocks tests for `SingleWithVersionAsync`; 91 in total.
    Services now reference only BuildingBlocks (ServiceDefaults comes through it). Code conventions updated.
- **T-043** — CI failed after T-017; make failing tests readable without signing in (unplanned) — **Done**
  - Goal: `main` is green again, and the next failure names its test where anyone can see it.
  - [x] Failing tests appear as GitHub annotations (test name + message) and the test results are kept as an artifact
  - [x] Tests that break the system on purpose run on their own, after the parallel tests, so they don't compete
        with them for the CI machine
  - [x] CI is green on the task branch (CI #30), then on `main`
  - [x] CI runs on every branch push, so a fix is proven before it is merged
  - Notes: the failing test of CI #27 couldn't be identified: raw logs need a signed-in user and only "exit code 2"
    was readable. `eng/ci/Report-FailedTests.ps1` now turns failed tests in the TRX files into annotations and a job
    summary (tested here on a real and a deliberately failed TRX). My first guess — load from T-017's second system —
    was wrong: CI #28 named the test and the cause, Aspire's stop command failing on Linux. The second guess — a slow
    graceful shutdown — was wrong too: CI #29 (on the task branch, now possible) showed Aspire losing track of the
    resource (state "Unknown"). With the owner's agreement the outage test is quarantined on Linux (`SkipWhen`, visible
    reason) and the cause moves to T-044; it still runs and passes on Windows. Kept from the attempts: the outage test
    runs alone after the parallel tests, reports the stop result and state, and every service shuts down within
    10 seconds.
- **T-027** — Module guide and the last core change before the proof (ADR-025) — **Done**
  - Goal: a developer adds a module by reading one document, touching only the allowed registration points.
  - [x] Roles follow permission suffixes (`read` / `write` / `delete`) and `Permissions.All` is collected from the
        declared classes; administrative permissions stay explicit. Adding a module's permission class is the only
        Contracts change a module needs; tests prove the matrix is unchanged for the existing modules
  - [x] `docs/process/adding-a-module.md` (and its `tr/` mirror): a checklist from an empty folder to a green test run —
        project, solution, entity, context and migration, contracts with one projection, endpoints with permissions,
        `Program.cs`, AppHost, gateway route, permission class, integration tests (tenant isolation, `404`, `412`,
        role refusals) — each step linking to the reference module (Customers)
  - [x] Optional sections: Pro-only module, tenant settings, publishing and consuming events, owned lines
  - [x] "Common mistakes" section with what earlier tasks found: arrays in request types, `SaveChanges` in an event
        handler, saving through the context instead of `IEventOutbox`, forgetting `If-Match`
  - [x] Linked from `CLAUDE.md` and the code conventions
  - Notes: `Roles.Grants(role, permission)` is the whole convention in one tested function; a suffix other than
    read / write / delete goes to Owner and Admin only (least privilege, agreed with the owner). `RoleMatrixTests` pins
    the exact matrix of ADR-022 and the convention for an undeclared `products.*` permission. Every link in both guides
    was checked to point at an existing file. The code conventions still told modules to edit `All` and `Roles` —
    corrected. 7 new tests; 162 in total. The guide's real test is T-013.
- **T-013** — Products service (Basic) — **the proof**: built only by following T-027, with zero core changes — **Done**
  - [x] Products (a Basic catalog: SKU, name, price) is added using only the guide and the allowed registration points
  - [x] A friction log records every step the guide didn't cover; the guide (or, if unavoidable, the core — and then
        the proof starts over) is fixed before the proof counts
  - [x] `git diff` of the task shows no change in BuildingBlocks, ServiceDefaults, Gateway code or existing Contracts lines
  - [x] Products has the same test coverage as the reference module
  - **Result:** the core is plug-and-play. Core paths changed: `Permissions.cs` (+13 lines, additions only) and the
    gateway's `appsettings.json` (+7 lines of routing configuration) — both allowed registration points; no line in
    BuildingBlocks, ServiceDefaults, Gateway code or Contracts logic was removed or changed. Roles picked up
    `products.*` by their suffix without touching `Roles.cs`.
  - **Friction log** (all fixed in the guide; none needed a core change):
    1. The guide ran the first migration at step 5, but `dotnet ef` builds the project and a web project doesn't build
       without `Program.cs` (step 8) — the migration moved to step 8.
    2. "Unit tests for domain rules" didn't say where — the guide now names `tests/MyWorkplace.<Module>.Tests` and which
       project file to copy.
    3. The core-diff check in step 11 left Contracts out and couldn't show removed lines — now two exact commands.
    4. Six existing tests pinned the complete permission list (Owner, Member and Viewer tokens, the role matrix), so
       adding any module broke them. They now assert subsets and the rule; the guide lists it as a common mistake.
       Test code only — no core change.
  - Notes: 7 unit tests (`MyWorkplace.Products.Tests`) and 20 integration tests; 189 in total. Linking products to stock
    items went to the backlog as T-050, to be refined with the business questions first.

---

## Sprint 3 — Inventory depth

Goal: stock the way small businesses really handle it (ADR-019, ADR-020). Refined with `/refine` before starting.

- **T-052** — Split Contracts and register the permission catalog (ADR-026 stage 1, part 1) — **Done**
  - Goal: the generic contracts live in a project with no product knowledge; the product only declares.
  - [x] New dependency-free `MyWorkplace.Abstractions`: token claim names, `IntegrationEvent`, the permission and role
        convention (`Roles`, `Grants`, effective permissions) working on a registered catalog
  - [x] `MyWorkplace.Contracts` keeps only the product part (permission catalog, `OrderPlaced`) and references Abstractions;
        BuildingBlocks no longer references `MyWorkplace.Contracts`. ServiceDefaults keeps a reference only for the plan
        policy and token values, which T-054 removes (agreed with the owner)
  - [x] The product registers its catalog explicitly (`AddPermissionCatalog(typeof(Permissions))`) in the shared service
        setup; an unregistered catalog fails at startup with a clear message
  - [x] No behavior change: all existing tests pass with at most `using` changes; the guide (`adding-a-module.md`) is updated
  - Notes: `PermissionCatalog.FromType` reads the product's catalog class; `DefaultRoles` holds the role names. Two hidden
    couplings surfaced: the core's settings endpoint used the product's `Permissions.SettingsManage` (now
    `CorePermissions.SettingsManage`, which a catalog must declare — checked at build of the catalog), and the Admin rule
    named `plan.manage` (now an `[OwnerOnly]` attribute in the catalog). The catalog is a required parameter of
    `AddServiceModule`, so a service without one doesn't even compile; registering a different catalog twice fails.
    Services now reference Contracts explicitly — the arrow points from product to core. The core's tests use a made-up
    catalog; the product's agreed matrix test moved to Identity.Tests. Abstractions counts as core in the milestone and
    in the guide's core-diff check. 195 tests pass.
- **T-054** — Plan names and token settings out of the core (ADR-026 stage 1, part 2) — **Done**
  - Goal: the core knows the concept of a plan and a token, not this product's names and addresses.
  - [x] Plan policies `plan:<name>` are built on demand (like permission policies); `basic` / `pro` move to the product;
        the gateway route and the Pro services use `plan:pro`
  - [x] Issuer, audience and the Identity address are read from `Auth:*` configuration, set once in the AppHost;
        a missing value fails at startup with a clear message
  - [x] Core check: no product term (`basic`, `pro`, `myworkplace-*`, `identity` address) left in BuildingBlocks,
        ServiceDefaults or Abstractions
  - [x] No behavior change: all existing tests pass
  - Notes: decisions agreed with the owner — any `plan:<name>` is accepted (an unknown plan refuses everyone, fails
    closed), and the full discovery address is configured (`Auth:MetadataAddress`), so the core guesses no path.
    `PlanPolicy` (Abstractions) holds the convention; `ConventionPolicyProvider` (renamed from `PermissionPolicyProvider`)
    builds plan and permission policies; `Plans` (Contracts) holds `basic` / `pro` and `ProPolicy`; `PolicyNames` and
    `ProductTokens` are gone. `TokenAuthenticationOptions` is checked at startup by a source-generated validator; the
    AppHost's `Auth` section reaches every project through `WithTokenSettings`, and Identity issues and publishes the same
    values. ServiceDefaults no longer references Contracts. `CoreBoundaryTests` scans the core sources for product
    terms — it found two comments on its first run. 206 tests pass.
- **T-031** — Unit catalog and alternative units (ADR-019) — **Done**
  - Goal: a company works in its own units, and an item can be counted in more than its base unit.
  - [x] System units live in code and every company has them: `PCS` 0 decimals, `KG` / `L` / `M` 3, `BOX` / `PACK` 0;
        they can't be changed or deleted
  - [x] `GET /inventory/units` lists system units and the company's own units; `POST` adds one (code unique within the
        company, case-insensitive, also against system units → `409`; precision 0–3, as stored balances keep 3 decimals → otherwise `400`); `PUT` changes the
        name, and code or precision only while no item uses the unit (`409`); `DELETE` only an unused unit (`409`);
        a system unit can't be changed or deleted (`409`)
  - [x] Permissions: reading `inventory.read`, add / change `inventory.write`, delete `inventory.delete`; another
        company's units are invisible (`404`); a Basic company gets `403`
  - [x] An item's base unit and alternative units must exist in the catalog (`400`)
  - [x] The item form carries `units: [{ unit, factor }]`, saved with the item's full update (ETag): factor > 0, not the
        base unit, no unit twice (`400`); the item response returns them
  - [x] An item with stock movements can't change its base unit (`409`)
  - [x] A unit tells whether a quantity fits its precision (`PCS`: 2.5 invalid, `KG`: 2.5 valid) — covered by unit
        tests; applied to manual movements in T-030, never to order issues (ADR-020)
  - Notes: decided with the owner while planning — precision is capped at 0–3 (not 0–6 as first refined), because
    balances and movements are stored with 3 decimals; units are addressed by code. `SystemUnits` (code) replaces
    `DefaultUnits`; `UnitOfMeasure` (table `units`) holds a company's own units; `UnitCatalog` is the one place answering
    "exists?" and "in use?" (ready for T-030); alternative units are owned rows (`stock_item_units`, factor
    numeric(18,6), 1–1,000,000). Codes are letters, digits, `-` or `_` and stored upper-case. A new
    `MyWorkplace.Inventory.Tests` project holds the fast rule tests. Found on the way: when only an owned collection
    changes, EF skips the owner's row and with it the ETag check — fixed for items by marking a column modified (a test
    proves the fix: without it the ETag stays the same); the same gap exists in Orders → T-056. Known small race: a unit
    deleted at the same moment an item starts using it is not blocked by the database (units are referenced by code,
    system units have no row). 251 tests pass.
- **T-055** — Barcodes (ADR-019): barcodes per item unit (base or alternative), unique per company; managed with the
  item; `GET /inventory/barcodes/{code}` returns item, unit and factor (`404` if unknown or another company's).
  Refine first: format rules (free text or EAN checksum) and what happens to barcodes when a unit is removed from an item
- **T-030** — Manual stock movements and movement history (ADR-020) — **Done**
  - Goal: a company records goods coming in and going out by hand, in any of the item's units, and can always see why
    the stock is what it is.
  - [x] `POST /inventory/items/{id}/movements` with `type` (`In` / `Out`), `quantity` > 0, optional `unit` (default: the
        base unit) and optional `note` (≤ 500): the quantity must fit the unit's precision and, converted with the item's
        factor, the base unit's precision; a unit the item doesn't have → `400`
  - [x] A movement records type, entered quantity, unit, factor, base-unit quantity, balance after, reason `Manual`,
        note, user and time; movements are never edited or deleted — a mistake is fixed with an opposite movement
  - [x] `201` with the movement, the balance after it and a `warnings` list
  - [x] Negative stock follows `NegativeStockPolicy`: `Block` → `409` and nothing is recorded; `Allow` → applied;
        `Warn` → applied and `warnings` contains `NegativeStock`. Under `Allow` / `Warn` a movement taking the balance
        below zero is flagged as negative stock, like an order's
  - [x] Under `Block`, parallel `Out`s can never take the balance below zero: one conditional update
        (`… WHERE quantity >= @q`) — proven by a concurrency test (e.g. 5 × 1 out of 3 → exactly 3 succeed, balance 0)
  - [x] `GET /inventory/items/{id}/movements`: paged, newest first, manual and order movements together, each with
        type, entered quantity and unit, base-unit quantity, balance after, reason, note, order number, user and time
  - [x] Recording needs `inventory.write`, reading `inventory.read`; another company's or a deleted item → `404`;
        a Basic company → `403`
  - Notes: decided with the owner while planning — no `Location` header (no single-movement endpoint yet; the movement
    is in the body), and only an *issue* ending below zero is flagged (a receipt while negative improves things).
    `StockLedger` has one atomic balance change for orders and manual movements; under `Block` it is conditional
    (`… WHERE quantity + @delta >= 0`). Proven: swapping in a read-then-write check lets 5 of 5 parallel issues through
    (balance −2); the conditional update lets exactly 3. `UnitConversion` checks both precisions; a movement is at most
    1,000,000 of its unit, so the base quantity always fits the column. Migration `AddManualMovements` fills the new
    columns of earlier order movements (base unit, factor 1). The delete-with-stock test now uses a real movement
    instead of SQL. 285 tests pass.

---

## Backlog

- **T-053** — Publish the core as versioned NuGet packages, stage 2 of ADR-026: GitHub Packages, SemVer, changelog,
  publishing from CI on a tag; this repository consumes the packages. Start only when both signals of ADR-026 hold
  (the core has settled, and a second real consumer is about to start)
- **T-057** — Idempotent POSTs, in the core: a client that retries a `POST` after a lost response (e.g. a stock
  movement "in 10") must not apply it twice. An `Idempotency-Key` header stored once per tenant, returning the first
  answer to a repeat; decide storage, lifetime and which endpoints require it. Found while refining T-030
- **T-056** — Version check for owned-only changes, in the core (ADR-017) — **Done**
  - Goal: changing only an owned collection (an order's lines with the same total, an item's alternative units) is a
    change to its owner, so a stale ETag is always refused — by the core, not by a line each endpoint must remember.
    Found in T-031.
  - [x] Orders: updating a draft's lines with the total unchanged, using a stale ETag → `412` — written first and seen
        failing without the fix
  - [x] The auditing interceptor marks an unchanged owner as modified when one of its owned entries is added, modified
        or deleted: the save runs `UPDATE … WHERE xmin = @version`, the version changes, `UpdatedAt` / `UpdatedBy` are set
  - [x] Core test (BuildingBlocks, real PostgreSQL, a made-up owner with an owned collection): an owned-only change with
        a stale version fails with a concurrency error; with the current version it saves and stamps `UpdatedAt`
  - [x] The per-endpoint line from T-031 in `UpdateStockItem` is removed; its test still passes
  - [x] ADR-017 records the rule; no behavior change otherwise — all existing tests pass
  - Notes: correction to T-031 — Orders was *not* exposed: `UpdateOrder` already carried the same hand-written line
    (marking `Total` modified, since T-014). So two endpoints each patched the gap by hand, which is the case for fixing
    it once in the core; both lines are removed. Red/green proven: with both lines removed and the interceptor step
    disabled, the six core tests and the Orders and Inventory tests fail; with the step they pass. Limits: only
    `IAuditable` owners (every entity with owned parts today) and one level of ownership. The core changed, so ADR-026's
    "core has settled" count restarts. On the first full run right after Docker started, `StockFromOrdersTests` hit its
    30-second wait once (cold containers); it passed alone and in a second full run — a timing flake like T-051.
    258 tests pass.
- **T-051** — Flaky CI: a Testcontainers container can fail to start with "address already in use" while the three
  test projects start containers in parallel (CI #35, passed on re-run). Options: run test projects one after another,
  or retry container start on a port conflict; measure the cost in CI time
- **T-050** — Link stock items to products: `ProductCreated` / `ProductUpdated` from Products, consumed by Inventory
  (ADR-019). Refine first: must every stock item have a product (raw materials?), and which fields does Inventory need?
- **T-046** — Test pyramid: fast unit tests for domain rules now covered only through the API (order totals and
  rounding, last-Owner and anti-escalation, stock ledger rules), so most rules fail in milliseconds, not minutes
- **T-047** — Shared integration-test helpers: one place for "create a stock item", "read a balance", "wait until",
  now duplicated across test classes
- **T-048** — Row lock without a hand-written table name: the company lock in user management uses
  `SELECT … FROM tenants FOR UPDATE`; take the table name from the EF model so a naming change can't break it silently
- **T-049** — Retry-in-a-fresh-scope as a building block: `PlaceOrder` runs its own retry loop and creates DI scopes;
  move the pattern to BuildingBlocks so endpoints only express the business step
- **T-045** — `dotnet new` template for a module, generated from the guide (T-027) once the guide has been proven
- **T-044** — Un-quarantine the outage test on Linux: find why Aspire can't stop a resource in Linux CI (state
  "Unknown", CI #28 / #29) — e.g. kill the process by its PID (closer to a real crash), check Aspire's known issues
- **T-042** — Review list of order lines Inventory could not match to a stock item (ADR-020)
- **T-039** — Customer replica in Orders fed by customer events, so an order's `CustomerId` is validated (ADR-024)
- **T-040** — Cancel a placed order: `OrderCancelled` and stock returned by Inventory (ADR-024)
- **T-041** — Currency (a company setting) and VAT on orders (ADR-024)
- **T-038** — Custom roles per company (named permission sets defined by the company)
- **T-035** — Plan downgrade (Pro → Basic): what happens to Pro-module data must be decided first
- **T-033** — Variable-weight items (e.g. cheese sold by piece and by kg with a different weight per piece)
- **T-034** — Per-item override of the negative stock policy
- **T-018** — Reporting service (Pro)
- **T-019** — Per-plan rate limiting at the gateway (Basic: low, Pro: high)
- **T-020** — Refresh tokens
- **T-021** — User interface (Blazor or React; to be decided)
- **T-022** — Dependabot for NuGet packages and GitHub Actions
- **T-029** — "Someone is editing this record" presence indicator — informational, never a lock (ADR-017); needs the UI (T-021)

---

## Sprint Notes

Added at the end of each sprint: what we did, what's left, what we learned.

### Sprint 0 — Foundation (closed)

**Done:** T-001 … T-005. Architecture and process docs with 10 ADRs; public repository with bilingual README;
.NET 10 solution skeleton (Aspire AppHost, ServiceDefaults, YARP gateway, sample Customers service);
an integration test that starts the real system; CI on GitHub Actions; `/task`, `/ship`, `/refine` commands.

**Left:** nothing from the sprint scope. T-022 (Dependabot) was added to the backlog.

**Learned:**

- *Decide before coding.* Writing ADRs first made later choices quick; questions were answered once, in writing.
- *Tooling moves fast.* Aspire 13.5 needs the Aspire CLI bundle, and xUnit v3 on .NET 10 needs
  Microsoft.Testing.Platform. Starting from official templates and reading error messages carefully solved both.
- *Stale build output can lie.* A failed first build left stale generated metadata; a clean rebuild fixed it.
- *CI catches what the local machine hides.* The dev-certificate step passed on Windows and failed on Linux.
  The fix verifies the outcome instead of silencing the error.
- *Rules enforced by the build are rules that stick.* Bilingual doc comments are checked by the compiler (CS1591).

### Sprint 1 — MVP: Basic / Pro end to end (closed)

**Done:** T-024, T-006, T-023, T-007, T-026, T-008, T-009, T-028, T-011, T-010, T-012. Shared persistence conventions;
Identity with sign-up, RS256 JWT, JWKS and plan upgrade; gateway and services that both validate tokens and plans;
the reference module (Customers) with ETag concurrency and the paging standard; the first Pro module (stock items).
87 tests, all against real PostgreSQL or the real running system.

**Left:** nothing from the sprint goal. Inventory depth (units, barcodes, movements) moved to Sprint 3 on purpose;
T-036 (module boilerplate) was discovered and added to Sprint 2.

**Learned:**

- *Refine before coding.* Every task written in Sprint 0 failed the Definition of Ready (T-006, T-009, T-010, T-011);
  `/refine` turned each into testable criteria and ADRs before a line of code was written.
- *Copy the template once, early.* Building the second module from the first produced a friction log that exposed
  four core gaps (T-036) — found now, not during the milestone's proof.
- *Attack your own system.* Tests that call services directly, bypassing the gateway, prove defense in depth instead
  of assuming it.
- *Pin package families, not packages.* An EF Core patch mismatch and the IdentityModel family were fixed at the root
  with central transitive pinning.
- *A red test asks "code or test?".* Three failures were test bugs (a static AsyncLocal, two wrong expected orders);
  one test was reshaped to assert exactly one thing.
- *Look at the running system.* A glance at Docker Desktop revealed that tests and development ran different
  PostgreSQL versions (T-026).
- *Know your shell.* Windows PowerShell 5.1 split a commit message at its quotes; commits now use `git commit -F`.
