# Task Board

Conventions: [process/task-conventions.md](process/task-conventions.md)

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

- **State:** Todo
- **Goal:** Shared technical infrastructure every service's data layer builds on (ADR-011, ADR-014).
- **Acceptance criteria:**
  - [ ] `MyWorkplace.BuildingBlocks` project with `Entity` (UUID v7 `Id`), `ITenantOwned`, `IAuditable`,
        `ISoftDeletable`, `[AuditChanges]`, `ICurrentUser` — and no domain types
  - [ ] Model conventions applied automatically: tenant filter for `ITenantOwned`, soft-delete filter for
        `ISoftDeletable`, `xmin` concurrency token, `snake_case` naming, `NoTracking` by default
  - [ ] Auditing interceptor fills `CreatedAt/By` and `UpdatedAt/By` from `TimeProvider` and `ICurrentUser`;
        sets `TenantId` on insert and rejects any later change to it
  - [ ] Change-history interceptor writes one `audit_log` row per changed `[AuditChanges]` property
        (entity, id, property, old value, new value, user, tenant, time) in the same transaction; unmarked properties are not logged
  - [ ] `FindForUpdateAsync` loads a tracked entity; a concurrency conflict becomes a `409` ProblemDetails
  - [ ] Shared ProblemDetails setup for `400` (field errors), `401`, `403`, `404`, `409`
  - [ ] Tests against a **real PostgreSQL** prove: tenant A can't read tenant B's rows; soft-deleted rows are hidden;
        audit fields are filled; only marked properties are logged; a stale update fails with a concurrency error;
        queries are untracked by default

### T-006 — Identity: service, database and tenant sign-up

- **State:** Todo
- **Goal:** A company can sign up; the first real service with its own PostgreSQL database.
- **Acceptance criteria:**
  - [ ] PostgreSQL runs in the AppHost with an `identity-db` database; the Identity service is registered and
        the gateway routes `/identity/*` to it
  - [ ] `Tenant` (name, plan) and `User` (email, password hash, tenant) entities built on BuildingBlocks;
        the initial migration is committed and applied at startup in Development (ADR-015)
  - [ ] `POST /identity/register` with `{ companyName, email, password }` → `201` with `{ tenantId, userId }`;
        the new tenant's plan is **Basic**
  - [ ] `400` ProblemDetails with field errors for: missing company name, invalid email, password shorter than 8
  - [ ] `409` when the email is already registered — case-insensitive (`A@x.com` equals `a@x.com`)
  - [ ] Passwords are stored only as `PasswordHasher` hashes (ADR-013): a test asserts the stored value is not the
        password and verifies against it
  - [ ] Integration tests through the gateway cover `201`, `400` and `409`

### T-023 — Identity: login, signing key, JWT and JWKS

- **State:** Todo
- **Goal:** Registered users get a signed token that any service can verify on its own (ADR-005, ADR-012).
- **Acceptance criteria:**
  - [ ] An RSA key is generated on first start, stored in `identity-db` with a `kid`, and reused after a restart
  - [ ] `POST /identity/login` with `{ email, password }` → `200` with `{ accessToken, expiresIn: 900 }`;
        the token is RS256-signed, carries the `kid`, and has `sub`, `tenant_id`, `plan` and a 15-minute `exp`
  - [ ] Wrong email and wrong password return **identical** `401` ProblemDetails (ADR-013)
  - [ ] `GET /identity/.well-known/jwks.json` publishes the public key(s) and no private key material
  - [ ] An integration test validates an issued token using only the JWKS response

### T-007 — Gateway: routing, authentication and plan policy

- **State:** Todo
- **Acceptance criteria:**
  - [ ] YARP route `/inventory/*` (`/identity/*` and `/customers/*` exist from earlier tasks)
  - [ ] JWT validated via JWKS
  - [ ] `ProPlan` policy: Basic token on a Pro route → 403

### T-008 — Shared: tenant context and plan check (service side)

- **State:** Todo
- **Acceptance criteria:**
  - [ ] `ICurrentUser` is filled from the validated JWT (`sub`, `tenant_id`, `plan`), activating the tenant filter from T-024
  - [ ] In-service plan check (defense in depth)

### T-009 — Customers service (Basic)

- **State:** Todo
- **Acceptance criteria:**
  - [ ] Create / list / get / update customers
  - [ ] Duplicate email within a tenant → 409
  - [ ] Tenant A can't see tenant B's customers (test)

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

## Backlog

- **T-013** — Products service (Basic)
- **T-014** — Orders service (Basic)
- **T-015** — Messaging: library choice (ADR-007) + RabbitMQ + outbox
- **T-016** — `OrderPlaced` event → Inventory decreases stock
- **T-017** — Resilience demo: orders accepted while Inventory is down; stock catches up when it returns
- **T-018** — Reporting service (Pro)
- **T-019** — Per-plan rate limiting at the gateway (Basic: low, Pro: high)
- **T-020** — Refresh tokens
- **T-021** — User interface (Blazor or React; to be decided)
- **T-022** — Dependabot for NuGet packages and GitHub Actions

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
