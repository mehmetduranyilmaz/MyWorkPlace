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
- **Token claims:** `sub` (user), `tenant_id`, `plan` (`basic` | `pro`); short lifetime (15 min).

### ADR-006 — Plan enforcement in two layers

- **Decision:** (1) A per-route authorization policy at the gateway (`ProPlan`). (2) The same check inside the service.
- **Why:** Defense in depth — protection holds even if the gateway is misconfigured or a service is reached from the internal network.
- **Known behavior on upgrade:** The plan lives in the token, so after an upgrade the old plan stays in effect
  **until a new token is issued**. The short token lifetime bounds this window. Accepted trade-off.

### ADR-007 — Cross-service communication: events first (asynchronous)

- **Decision:** When one service *depends on* another, it publishes an **event** instead of calling it
  synchronously over HTTP (e.g. `OrderPlaced` → Inventory decreases stock). Broker: **RabbitMQ**.
  A **transactional outbox** keeps the database write and the message publish consistent.
- **Why:** Orders can still be placed while Inventory is down; stock catches up when it comes back.
- **Open decision:** The .NET messaging library (Wolverine / MassTransit / plain RabbitMQ.Client) will be chosen
  in the sprint that needs it, after checking licensing and maintenance status.
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
  - `TimeProvider` for all timestamps (UTC, testable) and an `ICurrentUser` abstraction (filled from the JWT in T-008).
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

- **Email is unique across the whole system**; each user belongs to exactly one tenant, so login needs only email + password.
  Users working for several tenants are out of scope.
- **Passwords** are hashed with ASP.NET Core's `PasswordHasher` (salted PBKDF2, 100k+ iterations);
  minimum length 8. Full ASP.NET Core Identity is not used — too heavy for our needs and it hides the mechanics.
- **No user enumeration:** a wrong email and a wrong password return the **same** `401` response.

### ADR-014 — Errors as ProblemDetails (RFC 9457)

- **Decision:** Every service returns errors in the standard `application/problem+json` format
  (`400` validation with per-field errors, `401`, `403`, `404`, `409`).
- **Why:** Clients handle errors from every service the same way.

### ADR-015 — Schema changes with EF Core migrations

- **Decision:** Each service owns its EF Core migrations, committed to the repository.
  In Development, a service applies pending migrations at startup.
- **Cost:** Startup migration is unsafe with multiple instances. Production must run migrations as a separate step
  (a migration job) before deploying.

---

## 4. Solution layout (planned)

```text
MyWorkplace.slnx
src/
  MyWorkplace.AppHost/            # Aspire: starts the whole system
  MyWorkplace.ServiceDefaults/    # Shared: OpenTelemetry, health checks, resilience
  MyWorkplace.Gateway/            # YARP
  MyWorkplace.BuildingBlocks/     # Shared technical infrastructure (ADR-011) — no domain types
  MyWorkplace.Contracts/          # Cross-service event definitions (data only)
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
