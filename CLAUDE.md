# CLAUDE.md

Guidance for Claude Code (and any contributor) working in this repository.
Keep this file short. Details live in `docs/` — read the relevant doc before acting.

## What this project is

**MyWorkplace** — a multi-tenant SaaS for small business management, built as a **learning project**
that demonstrates senior-level architecture: an API gateway, independent services, and
**Basic / Professional** subscription plans enforced at the gateway.

The repository is **public**. Code quality, documentation and commit history are part of the product.

## Read before you work

| Topic | File |
| --- | --- |
| Architecture & decisions (with reasons) | [docs/architecture.md](docs/architecture.md) |
| How we work (sessions, tasks, review) | [docs/process/workflow.md](docs/process/workflow.md) |
| Task board & conventions | [docs/tasks.md](docs/tasks.md), [docs/process/task-conventions.md](docs/process/task-conventions.md) |
| Branches & commits | [docs/process/git-conventions.md](docs/process/git-conventions.md) |
| Code conventions (naming, bilingual comments) | [docs/process/code-conventions.md](docs/process/code-conventions.md) |
| Adding a module (step-by-step recipe) | [docs/process/adding-a-module.md](docs/process/adding-a-module.md) |

## Non-negotiable rules

1. **The human decides.** Present options with trade-offs for architectural choices; never make them silently.
   New decisions go into `docs/architecture.md` as an ADR.
2. **Work from the task board.** Every change belongs to a task in `docs/tasks.md`.
   Unplanned work → add a task to the backlog, then continue the current task.
3. **Explain the why.** The owner is learning. Summarize what was done and why, in Turkish.
4. **Language:** everything committed is English — code, identifiers, commits, `CLAUDE.md`, `docs/`.
   Exception: the README is published in both languages — `README.md` (English) and `README.tr.md` (Turkish);
   keep them in sync. Conversation with the owner is in Turkish.
5. **Turkish mirrors (`tr/`):** `tr/` holds Turkish translations of `CLAUDE.md` and `docs/` for the owner.
   It is local only and **never committed**. **The English files are the source of truth.**
   Whenever an English doc changes, update its `tr/` counterpart in the same task.
6. **Bilingual code comments:** every class, method, property and field has a `/// <summary>` with an
   English and a Turkish line (`EN: ...` / `TR: ...`). See [code-conventions.md](docs/process/code-conventions.md).
7. **Tests are part of done.** No task moves to *In Review* with failing or missing tests for its business rules.
8. **Plan checks happen twice:** at the gateway (routing policy) and inside the service (defense in depth).
9. **Tenant isolation is mandatory:** every tenant-owned entity has `TenantId` and a global query filter.
10. **No secrets in the repo.** Use user-secrets / Aspire parameters.
11. Must build and run from both **VS Code** and **Visual Studio**.
12. Never commit, push or mark a task *Done* without the owner's approval.

## Commands

| Action | Command |
| --- | --- |
| Build (must be 0 warnings) | `dotnet build MyWorkplace.slnx` |
| Run the whole system | `dotnet run --project src/MyWorkplace.AppHost` |
| Run all tests | `dotnet test --solution MyWorkplace.slnx` |
| Restore local tools (`dotnet-ef`) | `dotnet tool restore` |
| Add a migration | `dotnet ef migrations add <Name> --project src/Services/<Service> --output-dir Persistence/Migrations` |

Notes:

- The first build of the AppHost needs the **Aspire CLI bundle** (DCP + dashboard). If the build fails with
  `ASPIRE009`, install it with `dnx aspire.cli -- setup`, then rebuild with `--no-incremental`.
- Tests use xUnit v3 on **Microsoft.Testing.Platform** (configured in `global.json`), hence `--solution`.
- Tests need **Docker** running: BuildingBlocks tests use a throw-away PostgreSQL via Testcontainers,
  integration tests start the real system through Aspire.
