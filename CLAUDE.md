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
6. **Tests are part of done.** No task moves to *In Review* with failing or missing tests for its business rules.
7. **Plan checks happen twice:** at the gateway (routing policy) and inside the service (defense in depth).
8. **Tenant isolation is mandatory:** every tenant-owned entity has `TenantId` and a global query filter.
9. **No secrets in the repo.** Use user-secrets / Aspire parameters.
10. Must build and run from both **VS Code** and **Visual Studio**.
11. Never commit, push or mark a task *Done* without the owner's approval.

## Commands

> Filled in once the solution skeleton exists (task T-003).
