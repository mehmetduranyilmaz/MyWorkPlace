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
- **Tools:** xUnit v3 on Microsoft.Testing.Platform (the .NET 10 default direction; set in `global.json`),
  Aspire.Hosting.Testing.

---

## 4. Solution layout (planned)

```text
MyWorkplace.slnx
src/
  MyWorkplace.AppHost/            # Aspire: starts the whole system
  MyWorkplace.ServiceDefaults/    # Shared: OpenTelemetry, health checks, resilience
  MyWorkplace.Gateway/            # YARP
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
