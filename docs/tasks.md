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

- **State:** Todo
- **Acceptance criteria:**
  - [ ] `MyWorkplace.slnx`, `Directory.Build.props`, `Directory.Packages.props`
  - [ ] AppHost, ServiceDefaults, Gateway and one empty sample service
  - [ ] `dotnet build` passes with no warnings
  - [ ] Runs from VS Code (`launch.json` / `tasks.json`) and Visual Studio
  - [ ] "Commands" section in `CLAUDE.md` filled in

### T-004 — CI (GitHub Actions)

- **State:** Todo
- **Acceptance criteria:**
  - [ ] Build and tests run on every push and PR
  - [ ] Status badge in README

### T-005 — Claude Code project commands

- **State:** Todo
- **Goal:** Turn recurring rituals into commands (like "modus" in the reference article).
- **Acceptance criteria:**
  - [ ] `/task T-xxx` → pick up the task, move to In Progress, create branch
  - [ ] `/ship` → verify, commit (with approval), move to In Review, write summary
  - [ ] `/refine T-xxx` → clarify acceptance criteria

---

## Sprint 1 — MVP: Basic / Pro end to end

Goal: "A Basic tenant can't access Inventory, a Pro tenant can" works against the real system.

### T-006 — Identity: tenant sign-up and login

- **State:** Todo
- **Acceptance criteria:**
  - [ ] `POST /identity/register`: creates tenant + first user, plan = Basic
  - [ ] `POST /identity/login`: RS256-signed JWT (`sub`, `tenant_id`, `plan`), 15 min lifetime
  - [ ] `/.well-known/jwks.json` publishes the public key
  - [ ] Passwords stored hashed

### T-007 — Gateway: routing, authentication and plan policy

- **State:** Todo
- **Acceptance criteria:**
  - [ ] YARP routes: `/identity/*`, `/customers/*`, `/inventory/*`
  - [ ] JWT validated via JWKS
  - [ ] `ProPlan` policy: Basic token on a Pro route → 403

### T-008 — Shared: tenant context and plan check (service side)

- **State:** Todo
- **Acceptance criteria:**
  - [ ] `TenantId` read from the token; EF Core global query filter applied
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

---

## Sprint Notes

Added at the end of each sprint: what we did, what's left, what we learned.
