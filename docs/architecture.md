# Architecture

This document explains **what** the system does and **why** it is built this way. Every significant
decision is recorded as an **ADR (Architecture Decision Record)**: context, decision, alternatives, cost.

> Rule: a new architectural decision is written here first, then implemented.

---

## 1. Product

**MyWorkplace**: a multi-tenant SaaS for small business management.
Every company (tenant) is on a plan: **Basic** or **Professional**.

| Module | Plan | Scope |
| --- | --- | --- |
| Identity | All | Company sign-up, user login, plan info, plan upgrade |
| Customers | Basic | Create / list / update customers |
| Products | Basic | Product catalog, prices |
| Orders | Basic | Create orders, track status |
| Inventory | **Pro** | Stock levels, automatic decrease on orders |
| Reporting | **Pro** | Sales summary, best-selling products |

### Deliberately out of scope

- AI features inside the application (would require a paid API)
- Payment / billing integration (plan upgrade is a single API call)
- User interface in the first phase (APIs are exercised through Scalar)
- Kubernetes, cloud deployment
- Off-the-shelf identity server (Keycloak etc.) — see ADR-005

---

## 2. Overview

```text
                                   ┌──→ Customers  (Basic) ──→ customers-db
                                   ├──→ Products   (Basic) ──→ products-db
 Client ──→ Gateway (YARP) ────────┼──→ Orders     (Basic) ──→ orders-db
              │ • JWT validation    ├──→ Inventory  (Pro)   ──→ inventory-db
              │ • Plan policy       └──→ Reporting  (Pro)   ──→ reporting-db
              │ • Rate limiting
              └──→ Identity ──→ identity-db

 Cross-service events (e.g. "order placed") ──→ Message broker (RabbitMQ)
 Local orchestration, logs, tracing        ──→ .NET Aspire
```

### Life of a request (Pro module)

1. Client calls `POST /identity/login` → Identity returns a **JWT** containing `tenant_id` and `plan`.
2. Client calls `GET /inventory/items` with the token.
3. The gateway validates the token. The route requires the `plan:pro` policy; if `plan` is not `pro`
   it returns **403** and the request never reaches the service.
4. Otherwise the request is forwarded to the Inventory service.
5. Inventory validates the token **itself**, re-checks the plan, and returns only that tenant's data.

---

## 3. Decisions (ADR)

### ADR-001 — Separate services behind an API gateway

- **Context:** The goal is to learn gateways and to build a system where one part can fail while the others keep working.
- **Decision:** Each module is its own ASP.NET Core service. All external traffic enters through a single gateway.
- **Alternative:** Modular monolith — simpler and often the right call for small teams.
  Rejected here because of the learning goal.
- **Cost:** Network failures, distributed data consistency, harder observability. We deliberately keep the number of services small.

### ADR-002 — YARP as the gateway

- **Decision:** Microsoft's .NET reverse proxy, **YARP**.
- **Why:** Native to .NET; uses standard ASP.NET Core authentication, authorization and rate limiting; actively maintained.
- **Alternatives:** Ocelot (slower development pace), cloud gateways (out of scope).
- **Cost:** The gateway is a single point of failure. Production needs multiple instances behind a load balancer
  (documented here; a single instance runs locally).

### ADR-003 — Database per service (PostgreSQL)

- **Decision:** One PostgreSQL server, **a separate database per service**. A service never accesses another service's database.
- **Why:** A shared database couples services — when it goes down, everything goes down.
- **Cost:** No cross-service joins. Data needed elsewhere is copied via events (see ADR-007).
- **Note:** One server is enough locally; in production services can be moved to separate servers.
- **Version:** pinned once in `eng/PostgresImage.cs` and linked into the AppHost and every Testcontainers fixture,
  so development and tests always run the same server version.

### ADR-004 — Multi-tenancy: shared database, `TenantId` column

- **Decision:** Tenants share tables. Every row has a `TenantId`, and an EF Core **global query filter**
  scopes every query to the current tenant.
- **Alternative:** Schema or database per tenant — stronger isolation, more expensive to operate.
- **Cost:** A bug that bypasses the filter leaks another tenant's data, so there are dedicated tests for it.
- **Implementation:** applied automatically to every `ITenantOwned` entity by BuildingBlocks (ADR-011).

### ADR-005 — Identity: our own service, JWT (RS256)

- **Decision:** The Identity service signs tokens with an **RSA key (RS256)** and publishes its public key
  at a JWKS endpoint. The gateway and every service validate tokens **themselves** with that public key.
- **Why:** If Identity goes down, signed-in users keep working until their token expires.
  With a symmetric key every service would hold the secret that can *issue* tokens.
- **Alternatives:** Keycloak / Duende / Entra ID — preferred in real projects, but they hide the mechanics.
  Rejected on purpose for learning.
- **Token claims:** `sub` (user), `tenant_id`, `plan` (`basic` | `pro`); short lifetime (15 min);
  `iss = myworkplace-identity`, `aud = myworkplace-api`. Claim names live in `Abstractions.Identity.TokenClaims`;
  the plan names in `Contracts.Identity.Plans`; issuer and audience in the AppHost's `Auth` settings (ADR-026).
- **Discovery:** Identity publishes `/identity/.well-known/jwks.json` and a minimal
  `/identity/.well-known/openid-configuration`, so standard JWT middleware finds and refreshes the keys by itself.

### ADR-006 — Plan enforcement in two layers

- **Decision:** (1) A per-route authorization policy at the gateway (`plan:pro`). (2) The same check inside the service.
- **Why:** Defense in depth — protection holds even if the gateway is misconfigured or a service is reached from the internal network.
- **Known behavior on upgrade:** The plan lives in the token, so after an upgrade the old plan stays in effect
  **until a new token is issued**. The short token lifetime bounds this window. Accepted trade-off.
  The upgrade endpoint (T-011) therefore returns a fresh token carrying the new plan, so the caller doesn't wait.
- **Identity validates its own tokens** with the keys it holds in memory (static JwtBearer configuration +
  `IssuerSigningKeyResolver`), never by downloading its own discovery document — no network call to itself.
- **Secure by default:** the gateway's fallback policy requires a valid token. Public routes (sign-up, sign-in,
  `/.well-known/*`) are explicitly marked `anonymous`; Pro routes use the `plan:pro` policy. A route whose policy is
  forgotten is closed, never open. Health endpoints are explicitly anonymous (Development only).
- **Token validation (both layers):** `AddTokenAuthentication()` in ServiceDefaults — standard JwtBearer with the
  Identity discovery document (`Auth:MetadataAddress`), fetched through Aspire service discovery (`https+http://identity`) — is used by the
  gateway **and** every service, so both layers accept exactly the same tokens. `RequireHttpsMetadata` is off because
  that logical scheme is not literally `https://`; service discovery still prefers HTTPS. A production deployment
  should point at a real `https://` address.
- **Services are secure by default too:** the same fallback policy applies inside each service; Pro services put their
  endpoints in a group requiring `plan:pro` (`Plans.ProPolicy`). A caller reaching a service directly, bypassing the
  gateway, still gets `401`/`403` (zero trust inside the network).
- **Plan policies by convention (ADR-026):** a policy named `plan:<name>` is built on demand and requires that plan
  claim; nothing is registered per plan. An unknown name matches no token, so a typo refuses everyone (fails closed).
- **Dependency rule:** the gateway references ServiceDefaults (and through it Abstractions), never BuildingBlocks —
  it has no business with databases.

### ADR-007 — Cross-service communication: events first (asynchronous)

- **Decision:** When one service *depends on* another, it publishes an **event** instead of calling it
  synchronously over HTTP (e.g. `OrderPlaced` → Inventory decreases stock). Broker: **RabbitMQ**.
  A **transactional outbox** keeps the database write and the message publish consistent.
- **Why:** Orders can still be placed while Inventory is down; stock catches up when it comes back.
- **Library:** decided in ADR-023 (T-015).
- **Unavoidable synchronous calls** use timeout + retry + circuit breaker (Aspire ServiceDefaults).
- **Proven (T-017):** an integration test stops Inventory for real, places orders, starts it again and sees the stock
  catch up. It also showed the gateway waited 15 seconds (YARP's default connect timeout) before answering for a
  stopped service; the gateway now gives up connecting after 3 seconds and answers with a `5xx`. The test runs on
  Windows and Linux (T-044: its Linux failure was each service waiting, on shutdown, for an OpenTelemetry endpoint
  nobody listened to in tests). Every service also shuts down within 10 seconds when asked (T-043).

### ADR-008 — .NET 10 + .NET Aspire

- **Decision:** All services are .NET 10 ASP.NET Core **Minimal APIs**. **.NET Aspire** handles local orchestration
  (PostgreSQL and RabbitMQ containers, services) and observability (OpenTelemetry dashboard).
- **Why:** One command starts the whole system. Logs, traces and metrics live in one dashboard.
  Services can be stopped from the dashboard to test resilience live.
- **Requirement:** Docker Desktop.

### ADR-009 — Service internals: vertical slices

- **Decision:** Each service is a single project. Code is organized **by feature**, not by layer
  (`Features/CreateCustomer/...`).
- **Why:** For small services, a four-project Clean Architecture adds ceremony without benefit.
  Changing a feature means working in one folder.
- **Cost:** Requires discipline; shared code is deliberately kept under `Common/`.

### ADR-010 — Testing strategy

- **Unit tests:** Business rules (e.g. stock can't go negative).
- **Integration tests:** Critical scenarios against the real system via Aspire testing:
  - A Basic tenant cannot access a Pro module (403)
  - A tenant cannot see another tenant's data
  - Orders can be placed while Inventory is down
- **Tests that break the system on purpose** (e.g. stop a service) use their own system copy
  (`IsolatedAppFixture`) and the `ServiceOutageCollection`, so they run alone, after the parallel tests (T-043).
- **CI failures are readable without signing in:** failed tests become annotations and a job summary; the TRX results
  are kept as an artifact (T-043).
- **Quarantine, never silent skips:** a test that fails for an environment reason outside our code may be skipped only
  conditionally (`SkipWhen`), with the reason in the skip message and a task on the board. Currently: none. The last
  one (the outage test on Linux) looked like an environment problem and was our test setup (T-044) — evidence first.
- **Library tests** (e.g. BuildingBlocks) run against a real PostgreSQL started by **Testcontainers** — an in-memory
  database can't reproduce PostgreSQL behavior such as `xmin`.
- **Tools:** xUnit v3 on Microsoft.Testing.Platform (the .NET 10 default direction; set in `global.json`),
  Aspire.Hosting.Testing.

### ADR-011 — Building blocks: shared persistence conventions

- **Context:** Every service stores data. Cross-cutting rules (ids, tenant isolation, auditing, soft delete,
  concurrency) must be identical everywhere and are expensive to retrofit into existing entities and migrations.
- **Decision:** A shared library, `MyWorkplace.BuildingBlocks`, provides:
  - `Entity` base class with a `Guid Id` generated as **UUID v7** (unguessable, index-friendly because it is time-ordered).
  - Opt-in interfaces: `ITenantOwned` (tenant filter + `TenantId` set on insert, immutable afterwards),
    `IAuditable` (`CreatedAt/By`, `UpdatedAt/By`), `ISoftDeletable` (`IsDeleted`, `DeletedAt`; hidden by a filter).
  - **Interceptors:** an auditing interceptor fills audit fields; a change-history interceptor writes
    *who / when / entity / property / old value / new value* to an `audit_log` table **in the same transaction**
    for properties marked `[AuditChanges]`. Which properties are marked is decided per entity, in the task that creates it.
  - **Optimistic concurrency** via PostgreSQL `xmin`; a conflicting update returns `409`.
  - `TimeProvider` for all timestamps (UTC, testable) and an `ICurrentUser` abstraction. Its default implementation,
    `HttpCurrentUser`, reads `sub`, `tenant_id` and `plan` from the request's **validated** token; unauthenticated
    principals, malformed claims and code running outside a request all read as anonymous.
  - PostgreSQL `snake_case` naming.
  - **Reads are not tracked:** `DbContext` defaults to `NoTracking`; queries project to DTOs with `Select`.
    Writes load entities explicitly with a `FindForUpdateAsync` helper that uses `AsTracking()`.
- **Boundary:** BuildingBlocks contains **technical infrastructure only — never domain types**. A `Customer` class
  there would couple services and defeat ADR-001.
- **Cost:** A no-tracking default means an entity loaded without tracking and then modified is **silently not saved**.
  The `FindForUpdateAsync` helper and a test guard against it. Every service depends on BuildingBlocks,
  so changes to it must stay backward compatible.

### ADR-012 — Token signing key stored in the Identity database

- **Decision:** On first start the Identity service generates an RSA key pair and stores it in `identity-db`
  with a key id (`kid`). Tokens carry the `kid`; JWKS publishes every active public key, which enables rotation later.
- **Why:** Tokens survive restarts, no manual setup, works unchanged in CI.
- **Alternatives:** In-memory key per start (every restart signs everyone out); user-secrets PEM (manual, extra CI setup).
- **Cost:** The private key sits unencrypted in the database. Production must use a key vault or HSM.
  **Amended by ADR-032:** keys are encrypted with a master key kept outside the database, and rotated.

### ADR-013 — Identity rules

- **Email is unique across the whole system** (among live users — a removed user's email can be reused, T-037); each user belongs to exactly one tenant, so login needs only email + password.
  Users working for several tenants are out of scope.
- **Passwords** are hashed with ASP.NET Core's `PasswordHasher` (salted PBKDF2, 100k+ iterations);
  minimum length 8. Full ASP.NET Core Identity is not used — too heavy for our needs and it hides the mechanics.
- **No user enumeration:** a wrong email and a wrong password return the **same** `401` response — and take the same
  time: for an unknown email a decoy hash is verified, so response timing can't reveal registered emails.
- **Hash upgrades:** when `PasswordHasher` reports `SuccessRehashNeeded`, the hash is re-created at sign-in.

### ADR-014 — Errors as ProblemDetails (RFC 9457)

- **Decision:** Every service returns errors in the standard `application/problem+json` format
  (`400` validation with per-field errors, `401`, `403`, `404`, `409`).
- **Why:** Clients handle errors from every service the same way.

### ADR-015 — Schema changes with EF Core migrations

- **Decision:** Each service owns its EF Core migrations, committed to the repository.
  In Development, a service applies pending migrations at startup.
- **Cost:** Startup migration is unsafe with multiple instances. Production must run migrations as a separate step
  (a migration job) before deploying.

### ADR-016 — Resource API conventions

Set by the reference module (Customers, T-009 / T-028) and copied by every later module.

- **Create:** `POST /{resources}` → `201` with `Location` and `ETag`.
- **Read:** `GET /{resources}/{id}` → `200` with `ETag`.
- **Update:** `PUT /{resources}/{id}` is a **full** update and requires `If-Match` (ADR-017). `PATCH` is not used.
- **Delete:** `DELETE /{resources}/{id}` → `204`; soft delete for business data.
- **Another tenant's record → `404`, never `403`:** `403` would confirm the record exists. The tenant filter produces
  the `404` by itself.
- **Uniqueness per tenant ignores soft-deleted rows** (filtered unique index), so a deleted record's value can be reused.
- **Lists:** `GET /{resources}?page=1&pageSize=20&search=...` → `{ items, page, pageSize, totalCount }`.
  `page` ≥ 1, `pageSize` 1–100 (default 20); out-of-range → `400`. Sorting is stable (a business key, then `id`).
- **Alternative (deferred):** keyset (cursor) paging — faster on very large tables, but no page numbers.
  Revisit if a list grows beyond what offset paging handles well.

### ADR-017 — Optimistic concurrency over HTTP (ETag / If-Match)

- **Context:** Two users may edit the same record at the same time; without a check, the second save silently
  overwrites the first (lost update).
- **Decision:** Optimistic concurrency. The row version (`xmin`, ADR-011) is returned as a strong `ETag`. `PUT` must
  send it back in `If-Match`: a stale version → `412 Precondition Failed`; no `If-Match` → `428 Precondition Required`.
- **Rejected: pessimistic locking** ("this record is being edited by X", as in desktop ERPs). HTTP is stateless, so
  a lock needs its own table with expiry, heartbeats and a force-unlock; abandoned locks (closed browser, lost
  connection) block other users; readers can be blocked too. Optimistic control would still be needed underneath,
  because locks can expire or be broken.
- **Later:** an informational "X is editing this record" indicator (T-029), which warns without locking — the approach
  of modern web apps. A real lock is added only for a record type where conflicts are proven costly, with its own ADR.
- **Implementation:** BuildingBlocks `ETags` (format, `SetETag`, `TryReadIfMatch`, 412/428 answers) and
  `ConcurrencyExtensions` (`GetVersion`, `ExpectVersion`). `ExpectVersion` makes the save conditional on the version
  the client edited (`UPDATE ... WHERE xmin = @version`), closing the gap between the check and the save.
  Untracked reads project the version with `EF.Property<uint>(e, "Version")`.
- **Owned parts count as a change to their owner (T-056):** EF writes only the changed rows, so a save that changes
  just an owned collection (an order's lines, an item's alternative units) would skip the owner's row — and with it the
  version check and `UpdatedAt`. The auditing interceptor therefore marks the owner modified whenever one of its owned
  parts is added, modified or deleted: every such save is conditional on the version, and produces a new one.

### ADR-018 — Tenant settings: business rules that vary per company

- **Context:** Some rules genuinely differ between businesses (a market lets the till go on when stock runs out,
  a pharmacy never does). Hard-coding them forces one company's choice on every company.
- **Rule of thumb:** a rule becomes a setting **only if companies legitimately differ on it** — e.g. negative stock
  policy, default unit, price display with or without VAT, order number format. Correctness and security rules are
  **never** settings (tenant isolation, password rules, ETag checks, "balance comes from movements"). Every setting
  adds test combinations and support questions ("why does it behave like this?"), so configurability is earned.
- **Decision:** A BuildingBlocks capability for **typed, per-module** settings. Each module declares a settings class
  with defaults in code (e.g. `InventorySettings { NegativeStockPolicy = Block }`); a tenant stores only what it
  changes, per tenant, in the module's own database (ADR-003). Exposed as `GET` / `PUT /{module}/settings` with
  ETag (ADR-017) and change history.
- **Rejected:** a global string key/value table — typos surface at runtime, values lose their types, and one
  table would couple every module.
- **Later:** per-item overrides (T-034).
- **Refined (T-032):**
  - **Storage:** one row per tenant and module holding the values as a `jsonb` document, read into the module's typed
    class. Adding a setting means adding a property with a default — no migration. Invalid values are rejected with
    `400` on save, so types are still enforced.
  - **Defaults:** a tenant that never saved gets the code defaults; a stored document missing a newer property gets
    that property's default. `PUT` replaces all values.
  - **Access:** reading needs the module's read permission (the UI must know e.g. `Warn`); changing needs
    `settings.manage` (Owner, Admin by default). The module's plan rule still applies.
  - **No cache for now:** one indexed row per read, so a change applies immediately. Caching can come with events (T-015).

### ADR-019 — Inventory model: base unit, alternative units, barcodes

- **Standalone first:** Inventory owns its stock items, identified by a per-tenant unique SKU. When Products and
  messaging exist, Products publishes "product created" and Inventory links its item to the product — Inventory
  **never calls Products synchronously** (ADR-003, ADR-007), so stock keeps working when Products is down.
- **Base unit:** every item has exactly one base unit and the balance is **always stored in it**.
- **Alternative units:** per item, a unit with a conversion factor to the base unit (1 `BOX` = 24 `PCS`). A movement
  in any unit is converted once, at the edge, so the balance can never mix units.
- **Unit catalog:** per tenant, seeded with defaults and extendable. Each unit has a decimal precision
  (`PCS` 0 → 2.5 pieces is rejected; `KG` 3).
- **Barcodes:** per item unit (the piece and the box have different barcodes), unique per tenant; a lookup returns
  item, unit and factor.
- **Out of scope:** variable-weight items, where each piece weighs differently (T-033).
- **Refined (T-031):**
  - **System units in code:** `PCS`, `KG`, `L`, `M`, `BOX`, `PACK` with their precision are defined in code and exist
    for every company; a company stores only the units it adds. The catalog is both together. No per-company seeding
    and no "company registered" event are needed (the same "defaults in code, the tenant stores the difference" idea
    as ADR-018). System units can't be changed or deleted.
  - **A company's own units:** code unique within the company and against system units (case-insensitive), precision
    0–3 — balances and movements are stored with 3 decimals, so a finer unit would be rounded silently (widening the
    columns waits for a real need). Code and precision are frozen once an item uses the unit, and a used unit can't be
    deleted — changing them would make stored quantities invalid.
  - **Units are addressed by code** (`/inventory/units/{code}`): system units have no database id, and items refer to
    units by code.
  - **Alternative units belong to the item:** a list of `{ unit, factor }` saved with the item's full update and ETag
    (ADR-016), not separate endpoints.
  - **Base unit is frozen once the item has movements:** changing it would silently re-read the balance in another unit.
  - **Precision is checked on manual movements (T-030)**, never on order issues, which are always applied (ADR-020).
  - **Barcodes** follow separately (T-055).
- **Refined (T-055) — barcodes:**
  - **Free text, no checksum:** 1–50 letters, digits, `-` or `.`, kept as entered and compared exactly. Small
    businesses print their own internal codes (Code128, case-sensitive, no EAN check digit); a scanner reads correctly,
    so a checksum rule would mostly refuse legitimate internal codes.
  - **A record of their own, not part of the item form:** uniqueness is a rule *across* items, so each barcode is a
    tenant-owned row with a unique index on (tenant, code) among live rows — the database settles parallel adds. Owned
    parts (like alternative units) carry no tenant id and could only be checked in code. Barcodes are added and removed
    one at a time (`/inventory/items/{id}/barcodes`), which is also how they are scanned in.
  - **No silent loss:** an alternative unit that still has barcodes can't be removed from its item (`409`); deleting an
    item deletes its barcodes, so their codes are free again (like SKUs).
  - **Lookup** returns the item, the scanned unit with its factor, the base unit and the balance — everything a till or
    a stock count needs in one call.

### ADR-020 — Stock movements and the negative stock policy

- **Decision:** the stock balance is never edited directly. It changes only through **append-only movements**
  (`In` / `Out`), each recording quantity, unit, factor, user and time — the answer to "why is stock 12?".
  Movements need no ETag: "add 5" overwrites nobody's change.
- **Negative stock** follows `InventorySettings.NegativeStockPolicy` (ADR-018):
  - `Block` (default): an `Out` larger than the balance → `409`. Enforced by one atomic statement
    (`UPDATE ... SET quantity = quantity - @q WHERE id = @id AND quantity >= @q`), so concurrent `Out`s cannot
    together push the balance below zero.
  - `Allow`: the balance may go negative.
  - `Warn`: allowed, and the successful response carries a `warnings` list (e.g. `NegativeStock`) for the UI to show.
- **Cost:** a database `CHECK (quantity >= 0)` is not possible because `Allow` / `Warn` are legitimate; under
  `Block` the guarantee comes from the conditional update.
- **Movements caused by orders (T-016):** when `OrderPlaced` arrives the sale has already happened — the goods have
  left, and Orders may not ask Inventory synchronously beforehand (ADR-007). So an order's `Out` is **always applied**,
  even below zero, and a movement that takes the balance below zero is flagged as negative stock for review. The
  policy above governs manual movements (T-030). Until units exist (T-031), an order quantity is in the item's base
  unit. Order lines whose SKU matches no stock item are skipped with a warning log (e.g. services); a review list
  follows (T-042). No plan check is needed: a Basic company can't have stock items, so nothing matches.
- **Implementation (T-016):** `StockLedger` is the only code that changes a balance — one atomic
  `UPDATE … SET quantity = quantity - @q` plus an append-only `StockMovement` (balance after, reason, order, negative
  flag), in the same transaction. The row lock serializes concurrent orders for the same item; a test showed that a
  read-then-write version loses updates (5 parallel orders left −1 instead of −5). Items of one order are issued in id
  order, so two orders can't deadlock.
- **Refined (T-030):**
  - **What a movement records:** type, the quantity and unit as entered, the factor, the quantity in the base unit,
    the balance after, the reason (`Order` or `Manual`), an optional note, the user and the time.
  - **Never edited or deleted:** a mistake is corrected with an opposite movement, as a reversing entry in bookkeeping;
    so the history always explains the balance.
  - **Precision at the edge:** the entered quantity must fit the entered unit's precision, and the converted quantity
    the base unit's; otherwise `400`, never a silent rounding.
  - **Answer:** `201` with the movement, the balance after and a `warnings` list (`NegativeStock` under `Warn`).
    Movements under `Allow` / `Warn` that go below zero are flagged like order movements.
  - **Retries** (the same `POST` sent twice after a lost response) are a core concern for every `POST`, handled by
    T-057, not per endpoint.
- **Refined (T-042) — unmatched order lines:**
  - **Seen, not just logged:** an order line whose SKU matches no item becomes an `Open` entry in Inventory (one per
    order and SKU), because a typo would otherwise mean stock that silently never leaves.
  - **A person settles it:** *resolve* issues the quantity from a chosen item now, recorded as an order issue of that
    order — so cancelling the order later returns it through T-040 with no extra code; *dismiss* leaves stock as it is
    (e.g. a service). No automatic matching when an item with the SKU appears later: a silent retroactive issue would
    surprise more than it helps.
  - **Ignored SKUs:** dismissing can add the SKU to the company's ignored list (e.g. `SHIPPING`), so lines that are never
    stock don't drown the list; the list can be edited.
  - **Closed entries stay closed:** a cancelled order closes its open entries; acting on a closed entry is `409`, so a
    quantity is never issued twice.

### ADR-021 — Module building blocks: no boilerplate in modules

- **Context:** Copying the reference module into Inventory (T-010) logged four kinds of repetition: the standard
  `Program.cs`, the same seven interface properties on every entity, identical design-time factories, and each
  entity-to-response mapping written three times. Repetition drifts; a module should contain only its own logic.
- **Decision:**
  - Entity base classes: `Entity` → `AuditableEntity` → `TenantOwnedEntity` → `BusinessEntity` (+ soft delete).
    The interfaces stay, because filters and interceptors are defined on them.
  - `AddServiceModule<TContext>(connection)` / `UseServiceModuleAsync<TContext>()` own the standard setup and the
    **middleware order**, so it is defined once.
  - `ServiceDbContextDesignTimeFactory<TContext>`: `dotnet ef` looks for a factory in the service's own assembly,
    so each service keeps a one-line subclass.
  - One mapping per response, as an expression (`Projection`): used by lists, by single reads through
    `SingleWithVersionAsync` (value + ETag version in one query, built by combining expression trees), and compiled
    once for `From`.
- **Exception:** `AddValidation()` stays in each service. The .NET 10 validation source generator inspects the
  project where it is called; called inside BuildingBlocks it would not see the service's request types and
  validation would silently stop.
- **Guarantee:** behavior-preserving refactoring — the existing tests pass unchanged and the schema is unchanged
  (empty migrations).

### ADR-022 — Roles and permissions

- **Permissions** are a catalog **in code** (Contracts), named `module.action` — `customers.read`, `customers.write`,
  `customers.delete`, `inventory.*`, `orders.*`, `users.manage`, `settings.manage`, `plan.manage`. Modules and Identity share the
  names, so a typo is a compile error. Destructive actions (`*.delete`) are separate permissions.
- **Default roles**, defined in code and available to every company (since T-027 derived from the permission suffixes,
  ADR-025 — the table below is the result):

  | Permission | Owner | Admin | Member | Viewer |
  | --- | :---: | :---: | :---: | :---: |
  | `*.read` | ✓ | ✓ | ✓ | ✓ |
  | `*.write` | ✓ | ✓ | ✓ | |
  | `*.delete` | ✓ | ✓ | | |
  | `users.manage`, `settings.manage` | ✓ | ✓ | | |
  | `plan.manage` | ✓ | | | |

- **Per-user extra permissions** can be granted on top of roles. **Grant only, no deny:** "why may this user do X?" must
  always be answerable by listing grants. Company-defined roles come later (T-038).
- **Effective permissions travel in the token** (`perm` claims): services check them locally, with no call to Identity
  (ADR-005, ADR-007). Like the plan (ADR-006), a change takes effect with the next token — at most 15 minutes.
- **Split of duties:** the gateway checks coarse rules (valid token, plan); services check permissions. The gateway never
  learns every module's permissions, which keeps modules plug-and-play.
- **Ownership:** the first user of a company is Owner; a company must always keep at least one Owner — demoting or
  removing the last one is refused with `409` (T-037).
- **User management (T-037):** users with `users.manage` add users (initial password, default role Member), change
  their roles and extra permissions (with `If-Match`, ADR-017) and remove them. Removal is a **soft delete**: the user
  can't sign in, but the audit trail keeps pointing to them and their email can be used again.
- **No privilege escalation:** only an Owner may change or remove an Owner or grant the Owner role, and nobody may grant
  a permission they don't hold (`403`). Taking permissions away is always allowed. The rule uses the caller's roles as
  stored now, not the possibly 15-minute-old token.
- **Last-Owner race:** changes and removals run in a short transaction that first locks the company row
  (`SELECT … FOR UPDATE`), so two Owners demoting each other at once can't leave the company without one. This is a
  lock held for milliseconds inside one request, not the edit-time lock ADR-017 rejects.
- **Known limit:** a demoted or removed user keeps their current token until it expires (at most 15 minutes).
  Short-lived tokens with refresh (T-020) keep that window small.
- **Rejected:** asking Identity per request (instant changes, but Identity going down would stop every service).

### ADR-023 — Messaging building block

- **Library: Wolverine** (MIT) on RabbitMQ, with its EF Core / PostgreSQL transactional outbox. Checked on 2026-09-24
  (T-015): WolverineFx 6.40.0 and its RabbitMQ, EF Core, PostgreSQL and runtime-compilation packages are MIT and were
  released that day; MassTransit 9.x carries no open-source licence expression.
- **Wolverine stays in BuildingBlocks:** services use `IEventOutbox` (publish with the business change) and plain
  `IEventHandler<TEvent>` classes, joined with `builder.AddServiceMessaging<TContext>("x-db")`. Replacing the library
  touches BuildingBlocks only.
- **Rejected:** MassTransit — v9 is commercial and the free v8 line has a limited lifetime; plain RabbitMQ.Client with
  our own outbox — retries, dead-lettering and duplicate handling are a lot of error-prone code that a mature free
  library already provides.
- **Outbox:** an event is stored in the same database transaction as the business change and sent afterwards.
  A rolled-back change sends nothing; an event committed while RabbitMQ is down is sent when it comes back.
- **At-least-once delivery, idempotent consumers:** every event has a unique id; a consumer that sees the same id
  twice does not process it again (otherwise an order could decrease stock twice).
- **Contracts:** events live in `MyWorkplace.Contracts` (`Events`), so services depend on the contract, never on each
  other's code. Once published, an event only gains fields — none is removed or renamed.
- **Tenant context:** every event carries its `TenantId`; a consumer runs as a system actor of that tenant, so the
  global query filters (ADR-004) still apply. Switching filters off in consumers is not allowed.
- **Implementation notes (T-015):**
  - One exchange per event type and one queue per service and event type, so every interested service gets every event.
    Events always go through the broker — in-memory hand-over to a handler in the same service is switched off.
  - Wolverine keeps its outbox / inbox tables in a `wolverine` schema of each service's database and creates them
    itself, outside our EF migrations. `processed_events` (our duplicate guard) is an EF table like `audit_log`.
  - Handler code is generated and compiled at startup (`WolverineFx.RuntimeCompilation`); pre-generating it would add
    a build step every module must remember.
  - The broker version is pinned in `eng/RabbitMqImage.cs` and shared by the AppHost and the tests (like PostgreSQL).
  - The dispatcher runs the "already processed?" check, the handler and the "processed" mark in one explicit
    transaction (T-016), so a handler may also update rows directly (`ExecuteUpdateAsync`) and all of it commits or
    rolls back together.
- **Known limit:** a consumer's queue is created when that consumer first starts. An event published before then has
  no queue to wait in and is lost, and a service added later doesn't receive past events. Accepted: events announce
  what happens now; they are not a history (that would be event sourcing, a separate decision). Once created, queues
  are durable, so a consumer that is down only delays its events (T-017).

### ADR-024 — Orders model

- **Lines are self-contained:** each line carries SKU, name, quantity and unit price as entered; nothing is checked
  against another service. Orders keeps working while Inventory or Products is down (ADR-007). Inventory matches
  lines to its stock items by SKU when it receives `OrderPlaced` (T-016); a `ProductId` joins the line once Products
  exists (T-013).
- **Customer snapshot:** an order may reference a customer by `CustomerId` and keeps a copy of the customer's name
  at the time of ordering — an order must show the name it was placed with, even if the customer is renamed later.
  The id is not validated for now; a local customer replica fed by events will do it (T-039).
- **Lifecycle:** `Draft` (editable, deletable, no number) → `Placed` via `POST /orders/{id}/place`. Placing gives the
  order the next per-company sequential number, freezes its lines and publishes `OrderPlaced` through the outbox
  (ADR-023). Cancelling a placed order, which must return stock, comes later (T-040).
- **Money:** line totals and the order total are computed by the server and stored as `decimal(18,2)`; totals sent by
  a client are ignored. Currency and VAT are out of scope for now (T-041).
- **Permissions:** `orders.read` / `orders.write` / `orders.delete` follow the role matrix of ADR-022: Members create
  and place orders, Viewers read, Admins and Owners also delete drafts.
- **Implementation notes (T-014):**
  - **Numbers:** one `order_number_sequences` row per company, incremented in the placement's transaction; its row
    version makes one of two simultaneous placements fail, and that one retries in a fresh scope (new context and
    outbox). Numbers never repeat and a failed placement leaves no gap.
  - **Placing needs If-Match:** you place exactly the version you last saw.
  - **Precision:** quantities have at most 3 decimals and prices 2; more is rejected (`400`) instead of being rounded
    silently by the database, which would make a stored price disagree with its line total.
  - **Lines** are owned by the order (`order_lines`). Soft-deleting an owner now keeps its owned parts — a core fix
    found here, since EF marks owned parts deleted along with their owner.
  - Events are published to an exchange named after the event type (`OrderPlaced`) — our rule (ADR-023), not the
    library's default.
- **Refined (T-040) — cancelling:**
  - **Only a placed order is cancelled** (a draft is deleted instead), as a whole, once; `Cancelled` is final. It
    needs `If-Match` like placing, and stores when, by whom and an optional reason. Partial returns are T-058.
  - **Permission `orders.cancel`:** undoing a sale is more sensitive than making one. The suffix convention (ADR-025)
    gives an unknown suffix to Owner and Admin only, so no role list is edited.
  - **The event carries only the order:** `OrderCancelled { OrderId, Number }`. Inventory returns what *it recorded*
    for that order — an `In` per issue movement, reason `OrderCancelled` — not what the lines say now: a SKU may have
    changed or moved to another item since the sale.
  - **Out-of-order events are expected:** the two events travel on different exchanges, so a cancellation can be
    processed first. Inventory then remembers the order as cancelled, and a late `OrderPlaced` issues nothing —
    otherwise the stock would stay wrong for good.
- **Refined (T-039) — customer replica:**
  - **Customers publishes, Orders keeps a copy:** `CustomerCreated` / `CustomerUpdated` / `CustomerDeleted` through
    the outbox; Orders keeps a per-company replica and validates `CustomerId` against it — never a synchronous call to
    Customers (ADR-007), so orders keep working while Customers is down.
  - **Last write wins, by `ChangedAt`:** events carry the time of the change; the replica applies only newer ones, so
    an old name arriving late never overwrites a newer one, and a late creation never revives a deleted customer.
  - **Checked on drafts, not on placing:** a draft names an existing, live customer (`400` otherwise); a placed order
    keeps its snapshot even if the customer is deleted later. A customer created a moment ago may not be in the
    replica yet — eventual consistency, accepted: the client retries.
  - **The name comes from the replica,** not from the client: the source of truth is Customers. A client-sent name is
    ignored, so existing clients don't break.
  - **No backfill:** customers created before T-039 reach the replica on their next update. There is no production data
    yet; a real deployment would need a one-time resync (republish every customer) before turning validation on.

### ADR-025 — Contracts is a registry; roles follow permission names

- **Context:** the plug-and-play proof (T-013) allows no core change, yet every module so far also edited
  `Contracts/Identity/Roles.cs` (the role matrix) and the `Permissions.All` list — an Open/Closed violation.
- **Decision — Contracts is a shared registry:** a module may **add** declarations there — its permission class and
  its events — and never change an existing line. Identity must know every permission to put it in a token, so the
  names have to live in one shared place; adding to it is declaring, not changing the core.
- **Decision — roles are derived by convention:** a module permission ends in `.read`, `.write` or `.delete`, and the
  default roles follow the suffix: `read` → every role, `write` → Owner, Admin, Member, `delete` → Owner, Admin.
  `Permissions.All` is collected from the declared classes by reflection. Administrative permissions (`users.manage`,
  `settings.manage`, `plan.manage`) stay explicit. A module that needs something else declares it explicitly — the
  convention is the default, not a cage.
- **Rejected:** permissions discovered at runtime from the services (Identity would depend on every service being up
  and would learn permissions late); a hand-edited matrix (every module edits core code and can forget a role).

### ADR-026 — The core is reused as versioned NuGet packages, extracted in two stages

- **Context:** the owner wants to reuse the plug-and-play core in future projects. Options were a template repository,
  a `dotnet new` solution template, or versioned packages; the first two copy the core, so fixes stop flowing between
  projects.
- **Decision:** the core (BuildingBlocks, ServiceDefaults and the generic part of Contracts) will be published as
  versioned NuGet packages (GitHub Packages, SemVer, a changelog, publishing from CI on a tag). This repository will
  consume the packages itself, so it is always the first user of every release.
- **Stage 1 — now (T-052), cheap:** keep the boundary clean before anything is packaged. Split Contracts into a
  generic part (token claims, `IntegrationEvent`, the permission convention) and this product's part (the permission
  catalog, `OrderPlaced`); find anything product-specific still inside the core (e.g. the plan names) and move it out
  or make it configurable.
- **Stage 2 — later (T-053), when both signals hold:** the core has settled (several module tasks in a row finished
  with no core change — Sprint 3 is the test) **and** a second real consumer is about to start. Packaging a core that
  still changes every week would turn every fix into a release (rule of three: generalize after real use, not before).
- **Rejected:** template repository or solution template as the main reuse path (projects drift apart; fixes are
  copied by hand); packaging immediately (premature — the core changed in T-014, T-016, T-027 and T-043).
- **Stage 1 refined (T-052, T-054):**
  - A dependency-free `MyWorkplace.Abstractions` project holds the generic contracts: token claim names,
    `IntegrationEvent`, the permission and role convention. `MyWorkplace.Contracts` keeps only this product's part
    (the permission catalog, events like `OrderPlaced`) and references Abstractions. Contracts stay light — never in
    BuildingBlocks, which would drag EF and Wolverine into every consumer.
  - The product registers its permission catalog explicitly (`AddPermissionCatalog(typeof(Permissions))`); no
    assembly scanning, so where permissions come from stays visible and testable.
  - The core knows the *concept* of a plan, not the plan names: policies named `plan:<name>` are built on demand like
    permission policies; `basic` / `pro` belong to the product (the gateway route becomes `plan:pro`).
  - Token issuer, audience and the Identity address come from configuration (`Auth:*`), set once in the AppHost for
    every service.
  - Done in T-054: the core's options (`TokenAuthenticationOptions`) are validated at startup, so a missing `Auth:*`
    value stops the host with the key's name. A plan-policy name with a typo is accepted and refuses everyone (fails
    closed) rather than making the product register its plans. A test (`CoreBoundaryTests`) keeps product terms out of
    the core projects.
  - `MyWorkplace.*` namespaces are renamed only when packaging (T-053), together with choosing the package names.

### ADR-027 — Idempotent POSTs with an `Idempotency-Key`, in the core

- **Context:** a client that loses the response of a `POST` (timeout, dropped connection) cannot know whether it was
  applied. Retrying a stock movement "in 10" would then add 20. Events between services are already deduplicated
  (`processed_events`, ADR-023); requests from clients were not.
- **Decision:** the core offers the `Idempotency-Key` header (as in the IETF draft and Stripe's API) on every
  authenticated `POST` of a service module, wired once in the shared service setup — no line per endpoint, the lesson
  of T-056. The header is optional: without it nothing changes, so no client breaks.
  - Same key and same request → the first response is replayed with `Idempotent-Replayed: true`; nothing runs twice.
  - Same key, different request (body, method or path) → `422`; same key while the first is still running → `409`.
  - `2xx` and `4xx` responses are stored; `5xx` are not, because a retry after a server error may rightly succeed.
  - Keys are per tenant, 1–100 characters, kept 24 hours; an hourly cleanup removes expired ones.
- **Storage:** an `idempotency_keys` table in each service's own database, next to `processed_events` (ADR-003) — no
  new infrastructure, and calls that reach a service directly are covered too.
- **How it works:** the key is reserved first (a unique index turns a parallel duplicate into `409`), the endpoint
  runs, then the response is stored. A key left "in progress" for over a minute (a crash) can be taken over.
- **Cost, accepted:** this is not exactly-once. If a service crashes after committing the change but before storing
  the response, a retry after the minute applies it again. Exactly-once would need the key saved in the endpoint's
  own transaction, i.e. code inside every endpoint; the reservation approach is the common industry trade-off.
- **Rejected:** idempotency at the gateway with Redis — one place, but new infrastructure, a stateful gateway, and
  direct calls to a service unprotected.
- **Implementation (T-057):** `IdempotencyMiddleware` runs right after authorization (`UseServiceModuleAsync`); the
  fingerprint is SHA-256 of method, path, query and body. `IdempotencyStore` works in a DI scope of its own, so the key
  rows never mix with the endpoint's unit of work; take-overs are conditional updates on the lock time, so of two
  takers only one wins. `IdempotencyCleanup` deletes expired keys hourly. The table is mapped by `ServiceDbContext`,
  like `processed_events`, so every service has it with its next migration.

### ADR-028 — Flaky tests: fix the cause, never retry the test

- **Context:** the suite grows (three test projects start containers, the integration tests start the whole system)
  and some failures only happen sometimes — a port conflict between containers, a wait that is too short on a cold
  machine. The easy answer is to retry a failed test automatically.
- **Decision:** tests are **never retried automatically** — no retry attribute, no "run failed tests again" step in
  CI. A red run is investigated and its cause written down (board or Sprint Notes) before anyone re-runs it; the CI
  summary shows the attempt number, so a re-run is visible.
- **Why:** in this project real bugs surfaced exactly as "sometimes red" tests — the owned-parts version gap (T-056),
  events arriving in reverse order (T-040), lost stock updates (T-016). An automatic retry would have turned each of
  them into a green run and a bug in production.
- **Allowed:** fixing the cause in the **test infrastructure**, including a bounded retry of an infrastructure step
  whose failure is not about our code (e.g. starting a container that lost a port race), named in its task.
- **Cost:** a flaky test costs attention every time until its cause is found — intended.

### ADR-029 — Where a rule is tested: the lowest layer that can break it

- **Context:** most business rules were proven only through the gateway. Such a test starts the whole system and
  answers in minutes; a broken rounding rule should be seen in milliseconds (T-046).
- **Decision:** each rule gets all its cases at the lowest layer that can break it:
  - a **pure rule** (a calculation, a state transition, input cleaning) → a plain unit test: no Docker, no network,
    no fixture;
  - a rule that lives in **atomic SQL or a lock** (the Block stock policy, "no lost update") → a handler test on a
    real database (`ServiceDatabase`, T-047);
  - the **wiring** (routing, authentication, plan and permission checks, the status code a rule maps to) → one
    representative case through the gateway.
- **Why:** a unit test can't see a race, and a gateway test is too slow to hold every case; each layer tests what only
  it can see.
- **Existing tests:** gateway tests that already cover a rule are kept — they prove the wiring. New cases go down.
- **Cost:** a rule's cases are split over two places; the test class names say which rule they cover.

### ADR-030 — `main` changes only through a pull request with a green CI, squash-merged by the owner

- **Context:** "CI green and the owner approved before merge" was a rule we kept by hand: Claude merged locally after a
  "yes" in the chat, and housekeeping commits went straight to `main`. Nothing stopped a red or unapproved change.
- **Decision:** a GitHub ruleset on `main` requires a pull request and the green `Build & test` check, blocks direct
  pushes, force pushes and deletion, and has **no bypass** — the owner included. Only squash merges are allowed; the
  squash commit takes the PR's title and description; the branch is deleted on merge. Required approvals: 0 — GitHub
  doesn't let an author approve their own PR, so the owner's approval is pressing **Squash and merge**. Claude opens the
  PR with the GitHub CLI, signed in once by the owner (the token stays in the OS credential store, never in the repo).
  Every change goes this way, housekeeping too.
- **Why:** a rule GitHub enforces can't be skipped by mistake or in a hurry; the merge button stays with the owner.
- **Not required:** "branch up to date before merging" — with one developer it only adds update rounds.
- **Cost:** a PR per change, even for a sprint note; a CI run before every merge.

### ADR-031 — The web client: a React + TypeScript single-page app, served through the gateway

- **Context:** the product had no user interface; everything was reached through the gateway's API. The owner wants
  a first screen, and to learn the skill the job market asks for most.
- **Decision:**
  - **React + TypeScript**, built with **Vite** as a single-page app in `src/MyWorkplace.Web`. No server-side
    framework (Next.js): the back end is already .NET, and a second server layer would add nothing here.
  - **Served through the gateway**, same origin: a lowest-priority catch-all route sends everything that isn't a
    service route to the web app. No CORS, and the gateway stays the only door. In development Aspire starts the Vite
    dev server, so `dotnet run --project src/MyWorkplace.AppHost` still starts the whole system.
  - **The access token** is kept in memory and in `sessionStorage`: a reload keeps the session, closing the tab ends
    it; when the 15-minute token expires the user signs in again, until refresh tokens (T-020). Never in
    `localStorage`, never in a cookie readable by scripts.
  - **Libraries:** MUI for components, TanStack Query for API calls, React Router, `react-i18next` (Turkish first; every
    text in a translation file from the start). Tests: Vitest + React Testing Library.
  - **CI:** a web job runs lint, type check, tests and build; it is a required check on `main` (ADR-030), like
    `Build & test`.
- **Why:** React is the most asked-for front-end skill; a client that only talks to the public API proves the API-first
  design (sign-in, plans, permissions, paging) from the outside. React Native can reuse the language, the API client
  and the types for a mobile app later.
- **Options not taken:** Blazor (C# end to end and quicker to start, but a much smaller market and ecosystem); a
  back-end-for-frontend with an httpOnly cookie (the safest token handling, but a large piece of infrastructure before
  the first screen — revisit with T-020).
- **Cost:** a second toolchain (Node.js, npm) to install and keep up to date; scripts in the page can read the token
  while it lives — mitigated by its 15-minute lifetime, a strict Content Security Policy and no third-party scripts.

### ADR-032 — Signing keys encrypted with a master key kept outside the database, and rotated (amends ADR-012)

- **Context:** ADR-012 stored the RSA private key unencrypted in `identity-db` and never replaced it; its cost line said
  production must do better. A leaked backup would let anyone sign valid tokens for any company, forever.
- **Decision:**
  - **Envelope encryption.** Each signing key is stored encrypted with AES-GCM under a 256-bit **master key** that is
    **not in the database**: an Aspire secret parameter (user-secrets in development, generated per run in tests
    and CI). The encryption sits behind `ISigningKeyProtector`, so production can swap in a key vault (T-069).
  - **Rotation.** A new key every 90 days. It is **published in the JWKS 24 hours before it signs**, so every service
    knows it in advance; a key that stopped signing stays in the JWKS for 24 more hours (tokens live 15 minutes),
    then it is deleted. Checked at start-up and daily; a database lock lets only one instance create a key.
  - **The existing plaintext key is retired, not encrypted:** a key that sat in clear text counts as exposed. On
    upgrade a new encrypted key signs at once (no 24-hour wait — verifiers refetch the JWKS on an unknown `kid`); the
    old key verifies for 24 hours, then is deleted. The plaintext column is dropped.
  - **Fail loudly.** If the keys can't be decrypted (missing or wrong master key), Identity stops at start-up with a
    clear message. It never silently creates a new key — that would hide the misconfiguration and sign everyone out
    without a reason. Recovery is documented: delete the keys on purpose and restart (everyone signs in again).
  - **Verifiers** refetch the JWKS every `Auth:MetadataRefreshInterval` (default 12 hours; at least 5 minutes — the JWT
    library refuses less, so a shorter value stops a service at start-up). The AppHost refuses a `PublishAhead` that
    isn't longer. Our side of the chain is tested link by link (T-066); that the library refetches on that schedule is
    its own contract — it refetches lazily, on traffic, so an idle verifier may answer one `401` for a brand-new key.
- **Operations:** the master secret is the AppHost parameter `signing-key-master-secret`, kept in the AppHost's
  user-secrets in development. If it is lost: delete the rows of `signing_keys` in `identity-db` and restart Identity —
  a new key is created and every user signs in again.
- **Why:** a backup alone is useless without the master key; rotation limits how long any one key matters; publishing
  ahead keeps every token valid during a rotation.
- **Option not taken:** ASP.NET Data Protection — the idiomatic choice, but its own key ring must be protected too:
  stored in the database it brings back the same problem, protected with a certificate it needs certificate
  management on every platform. Reconsider with a key vault (T-069).
- **Cost:** the master key is one more secret to keep — lost, it signs everyone out (by the documented recovery).

---

## 4. Solution layout (planned)

```text
MyWorkplace.slnx
src/
  MyWorkplace.AppHost/            # Aspire: starts the whole system
  MyWorkplace.ServiceDefaults/    # Shared: OpenTelemetry, health checks, resilience
  MyWorkplace.Gateway/            # YARP
  MyWorkplace.BuildingBlocks/     # Shared technical infrastructure (ADR-011) — no domain types
  MyWorkplace.Abstractions/       # Generic contracts of the core: claim names, event base, permission convention (ADR-026)
  MyWorkplace.Contracts/          # This product's contracts: permission catalog, events (on Abstractions only)
  Services/
    MyWorkplace.Identity/
    MyWorkplace.Customers/
    MyWorkplace.Products/
    MyWorkplace.Orders/
    MyWorkplace.Inventory/
    MyWorkplace.Reporting/
tests/
  MyWorkplace.IntegrationTests/
  MyWorkplace.<Service>.Tests/
docs/
```

Repository-wide: `Directory.Build.props` (shared settings), `Directory.Packages.props` (central package versions),
`global.json` (pinned SDK and test runner), `.editorconfig` (code style), nullable enabled, warnings as errors.
