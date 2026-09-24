# Task Board

Conventions: [process/task-conventions.md](process/task-conventions.md)

---

## Milestone — Plug-and-play core

**Definition (agreed with the owner):** the core is complete when a new module can be added by writing **only**
its entities, business rules, endpoints and tests — everything else comes from the core.

**Proof:** a new module (Products, T-013) is added by following the module guide **without changing a single line
of core code** (BuildingBlocks, ServiceDefaults, Contracts, Gateway code). If the core has to change, it is not done.

| Capability a new module gets for free | Status | Task |
| --- | --- | --- |
| Token validation, secure by default, plan policies (gateway + service) | Done | T-007, T-008 |
| Tenant isolation, audit fields, change history, soft delete, concurrency | Done | T-024, T-008 |
| ProblemDetails errors, API docs (Scalar) | Done | T-024, T-006 |
| Reference module to copy: CRUD, ETag concurrency, validation, tenant isolation tests | Todo | T-009 |
| Paging and search standard | Todo | T-028 |
| Roles and permissions: a module declares who may call which endpoint | Todo | T-025 |
| Cross-service events (RabbitMQ + outbox) | Todo | T-015, T-016, T-017 |
| Module guide: step-by-step recipe for adding a module | Todo | T-027 |
| **Proof: Products added via the guide with zero core changes** | Todo | T-013 |

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

- **State:** Todo
- **Goal:** The first business module, built as the template every later module copies (ADR-016, ADR-017).
- **Acceptance criteria:**
  - [ ] `customers-db` in the AppHost; Customers uses BuildingBlocks; the temporary `/customers/info` is removed
  - [ ] `Customer` (tenant-owned, auditable, soft-deletable): `Name` required ≤ 200; `Email` optional, valid, ≤ 320;
        `Phone` optional ≤ 30; `TaxNumber` optional ≤ 20; `Notes` optional ≤ 2000. `[AuditChanges]` on Name, Email,
        Phone, TaxNumber (not Notes). Initial migration committed
  - [ ] `POST /customers` → `201` with `Location` and `ETag`; `400` field errors for the rules above
  - [ ] `GET /customers/{id}` → `200` with `ETag`; unknown id or another tenant's customer → `404`
  - [ ] `PUT /customers/{id}` (full update) with `If-Match` → `200` with the new `ETag`; stale version → `412`;
        missing `If-Match` → `428`; another tenant's customer → `404`
  - [ ] `DELETE /customers/{id}` → `204` (soft delete); a later `GET` → `404`
  - [ ] Email unique within a tenant, case-insensitive, ignoring deleted customers → `409`; the same email in another
        tenant is allowed; an email of a deleted customer can be reused
  - [ ] Integration tests through the gateway cover every status above, tenant B's `404` on tenant A's customer for
        get / update / delete, and a change-history row for an audited field

### T-028 — Paging and search standard

- **State:** Todo
- **Goal:** One list format for every module, delivered by BuildingBlocks and applied to Customers first (ADR-016).
- **Acceptance criteria:**
  - [ ] BuildingBlocks: `PagedResult<T>` (`items`, `page`, `pageSize`, `totalCount`) and paging parameters:
        `page` ≥ 1 (default 1), `pageSize` 1–100 (default 20); out-of-range values → `400`
  - [ ] `GET /customers?page=&pageSize=&search=`: search in name, email and phone, case-insensitive;
        sorted by name, then id (stable order across pages)
  - [ ] Tests: page arithmetic and `totalCount`, bounds → `400`, search, only the caller's tenant is listed and counted

### T-010 — Inventory service (Pro) — minimal

- **State:** Todo
- **Acceptance criteria:**
  - [ ] Create / list stock items
  - [ ] Stock can't go negative (unit test)

### T-011 — Plan upgrade

- **State:** Todo
- **Acceptance criteria:**
  - [ ] `POST /identity/tenant/upgrade` → plan = Pro
  - [ ] Pro module accessible with a new token (integration test)

### T-012 — End-to-end integration tests

- **State:** Todo
- **Acceptance criteria:**
  - [ ] Basic → Inventory: 403
  - [ ] Pro → Inventory: 200
  - [ ] No token → 401

---

## Sprint 2 — Plug-and-play core

Goal: reach the milestone above. Tasks are refined with `/refine` before they start.

- **T-025** — Roles and permissions: permission-based policies, roles as permission sets, per-user extra
  permissions (needs an ADR: storage, token claims, default roles)
- **T-015** — Messaging: library choice (ADR-007) + RabbitMQ + transactional outbox, as a BuildingBlocks capability
- **T-014** — Orders service (Basic) — needed as the publisher of the first event
- **T-016** — `OrderPlaced` event → Inventory decreases stock
- **T-017** — Resilience demo: orders accepted while Inventory is down; stock catches up when it returns
- **T-027** — Module guide (`docs/process/adding-a-module.md`): step-by-step recipe, based on the reference module
- **T-013** — Products service (Basic) — **the proof**: built only by following T-027, with zero core changes

---

## Backlog

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
