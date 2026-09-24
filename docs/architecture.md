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
3. The gateway validates the token. The route requires the `ProPlan` policy; if `plan` is not `pro`
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
  `iss = myworkplace-identity`, `aud = myworkplace-api`. Claim names live in `Contracts.Identity.TokenClaims`.
- **Discovery:** Identity publishes `/identity/.well-known/jwks.json` and a minimal
  `/identity/.well-known/openid-configuration`, so standard JWT middleware finds and refreshes the keys by itself.

### ADR-006 — Plan enforcement in two layers

- **Decision:** (1) A per-route authorization policy at the gateway (`ProPlan`). (2) The same check inside the service.
- **Why:** Defense in depth — protection holds even if the gateway is misconfigured or a service is reached from the internal network.
- **Known behavior on upgrade:** The plan lives in the token, so after an upgrade the old plan stays in effect
  **until a new token is issued**. The short token lifetime bounds this window. Accepted trade-off.
  The upgrade endpoint (T-011) therefore returns a fresh token carrying the new plan, so the caller doesn't wait.
- **Identity validates its own tokens** with the keys it holds in memory (static JwtBearer configuration +
  `IssuerSigningKeyResolver`), never by downloading its own discovery document — no network call to itself.
- **Secure by default:** the gateway's fallback policy requires a valid token. Public routes (sign-up, sign-in,
  `/.well-known/*`) are explicitly marked `anonymous`; Pro routes use the `pro-plan` policy. A route whose policy is
  forgotten is closed, never open. Health endpoints are explicitly anonymous (Development only).
- **Token validation (both layers):** `AddTokenAuthentication()` in ServiceDefaults — standard JwtBearer with the
  Identity discovery document, fetched through Aspire service discovery (`https+http://identity`) — is used by the
  gateway **and** every service, so both layers accept exactly the same tokens. `RequireHttpsMetadata` is off because
  that logical scheme is not literally `https://`; service discovery still prefers HTTPS. A production deployment
  should point at a real `https://` address.
- **Services are secure by default too:** the same fallback policy applies inside each service; Pro services put their
  endpoints in a group requiring `pro-plan`. A caller reaching a service directly, bypassing the gateway, still gets
  `401`/`403` (zero trust inside the network).
- **Dependency rule:** the gateway references ServiceDefaults (and through it Contracts), never BuildingBlocks —
  it has no business with databases.

### ADR-007 — Cross-service communication: events first (asynchronous)

- **Decision:** When one service *depends on* another, it publishes an **event** instead of calling it
  synchronously over HTTP (e.g. `OrderPlaced` → Inventory decreases stock). Broker: **RabbitMQ**.
  A **transactional outbox** keeps the database write and the message publish consistent.
- **Why:** Orders can still be placed while Inventory is down; stock catches up when it comes back.
- **Library:** decided in ADR-023 (T-015).
- **Unavoidable synchronous calls** use timeout + retry + circuit breaker (Aspire ServiceDefaults).

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
- **Default roles**, defined in code and available to every company:

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

---

## 4. Solution layout (planned)

```text
MyWorkplace.slnx
src/
  MyWorkplace.AppHost/            # Aspire: starts the whole system
  MyWorkplace.ServiceDefaults/    # Shared: OpenTelemetry, health checks, resilience
  MyWorkplace.Gateway/            # YARP
  MyWorkplace.BuildingBlocks/     # Shared technical infrastructure (ADR-011) — no domain types
  MyWorkplace.Contracts/          # Cross-service contracts: token claims, policy names, events (no dependencies)
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
