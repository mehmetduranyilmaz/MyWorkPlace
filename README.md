# MyWorkplace

**English** | [Türkçe](README.tr.md)

A multi-tenant SaaS for small business management with **Basic** and **Professional** plans,
built on .NET 10 with an API gateway and independent services.

> **This is a learning project.** Its purpose is to show how a senior engineer designs such a system —
> and how a project can be run with an AI coding assistant ([Claude Code](https://claude.com/claude-code))
> as a disciplined team member rather than a code generator.
> Every decision is written down with its reasons.

[![CI](https://github.com/mehmetduranyilmaz/MyWorkPlace/actions/workflows/ci.yml/badge.svg)](https://github.com/mehmetduranyilmaz/MyWorkPlace/actions/workflows/ci.yml)
![Status](https://img.shields.io/badge/status-sprint%202%20%E2%80%94%20plug--and--play%20core-orange)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

---

## What it does

Every company (tenant) signs up on a plan. The gateway decides which modules the company can reach.

| Module | Basic | Professional |
| --- | :---: | :---: |
| Customers | ✅ | ✅ |
| Products | ✅ | ✅ |
| Orders | ✅ | ✅ |
| Inventory | — | ✅ |
| Reporting | — | ✅ |

A company can upgrade from Basic to Professional at any time.

## Architecture

```mermaid
flowchart LR
    Client([Client]) --> GW

    subgraph GW[API Gateway · YARP]
        direction TB
        A[JWT validation] --> P[Plan policy] --> R[Rate limiting]
    end

    GW --> ID[Identity]
    GW --> CU[Customers]
    GW --> PR[Products]
    GW --> OR[Orders]
    GW -. Pro only .-> IN[Inventory]
    GW -. Pro only .-> RE[Reporting]

    OR -- OrderPlaced --> MQ[[RabbitMQ]]
    MQ --> IN
    MQ --> RE

    ID --- DB1[(identity-db)]
    CU --- DB2[(customers-db)]
    PR --- DB3[(products-db)]
    OR --- DB4[(orders-db)]
    IN --- DB5[(inventory-db)]
    RE --- DB6[(reporting-db)]
```

Key ideas:

- **One entry point.** All traffic goes through the gateway, which validates the token and enforces the plan.
  A Basic company calling a Pro module gets `403` before the request ever reaches the service.
- **Defense in depth.** Each service re-validates the token and the plan on its own.
- **Failure isolation.** Each service owns its database and services talk through events,
  so when Inventory is down, orders are still accepted and stock catches up later.
- **Tenant isolation.** Every query is automatically scoped to the caller's company.

Full reasoning, alternatives and trade-offs: [docs/architecture.md](docs/architecture.md).

## Tech stack

| Area | Choice |
| --- | --- |
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| Gateway | YARP |
| Orchestration & observability | .NET Aspire, OpenTelemetry |
| Database | PostgreSQL (one database per service), EF Core |
| Messaging | RabbitMQ (transactional outbox) |
| Auth | JWT (RS256) issued by our own Identity service, validated via JWKS |
| Tests | xUnit v3, Testcontainers (real PostgreSQL), Aspire integration testing |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Aspire CLI](https://get.aspire.dev) — or run `dnx aspire.cli -- setup` once
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — runs PostgreSQL for the system and the tests
- VS Code with the C# Dev Kit extension, or Visual Studio 2026

### Run

```bash
git clone https://github.com/mehmetduranyilmaz/MyWorkPlace.git
cd MyWorkPlace
dotnet run --project src/MyWorkplace.AppHost
```

The Aspire dashboard opens in your browser and shows every service, its logs and traces.
From the dashboard:

- **Sign up and sign in:** open the `identity` endpoint and append `/scalar` — the API reference lets you call
  `POST /identity/register` and `POST /identity/login` from the browser. Paste the returned `accessToken` into
  [jwt.io](https://jwt.io) to see its claims. Through the gateway the URLs start with `<gateway>/identity/`.
- **Manage customers:** with the token as `Authorization: Bearer <token>`, call `POST <gateway>/customers`, then
  `GET`, `PUT` (send the `ETag` you read as `If-Match`) and `DELETE` on `<gateway>/customers/{id}`.
  The `customers` endpoint's `/scalar` page documents every status code.
- **Browse the database:** open **PgWeb** next to `postgres` to see the `tenants`, `users` and `audit_log` tables.

- **VS Code:** press **F5** (launch profile *MyWorkplace (Aspire AppHost)*).
- **Visual Studio:** open `MyWorkplace.slnx`, set `MyWorkplace.AppHost` as the startup project, press **F5**.

### Test

```bash
dotnet test --solution MyWorkplace.slnx
```

## How this project is built

The development process is part of what this repository demonstrates:

| File | Purpose |
| --- | --- |
| [CLAUDE.md](CLAUDE.md) | Rules for the AI assistant — its persistent memory between sessions |
| [docs/architecture.md](docs/architecture.md) | Architecture Decision Records: what we chose, why, and at what cost |
| [docs/tasks.md](docs/tasks.md) | Task board: sprints, acceptance criteria, progress |
| [docs/process/](docs/process/) | Workflow, task, Git and code conventions |

The human makes every architectural decision and approves every merge; the assistant proposes options,
implements, tests and explains.

## Roadmap

- [x] **Sprint 0 — Foundation:** repository, solution skeleton, CI
- [x] **Sprint 1 — MVP:** Identity, Gateway, Customers (Basic), Inventory (Pro), plan upgrade
- [ ] **Sprint 2 — Plug-and-play core:** roles and permissions, messaging with outbox, a module guide —
      proven by adding Products without touching core code
- [ ] Orders, event-driven stock updates, live resilience demo
- [ ] Reporting, per-plan rate limiting, refresh tokens
- [ ] User interface

Details: [docs/tasks.md](docs/tasks.md)

## License

[MIT](LICENSE)
