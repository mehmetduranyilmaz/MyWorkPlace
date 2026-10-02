# Git Conventions

## Branches

- `main`: always builds and passes tests. Protected: it changes only through a pull request (ADR-030).
- Task branch: `<type>/T-<no>-<short-description>`
  - Examples: `feat/T-012-create-customer`, `fix/T-020-tenant-filter`
- Housekeeping that belongs to no task (sprint notes, sprint planning): `docs/sprint-<no>-<short-description>`
  - Example: `docs/sprint-6-close`

## Commit messages: Conventional Commits

```text
<type>(<scope>): <summary>

<why this change — optional>

Refs: T-012
```

| type | When |
| --- | --- |
| `feat` | New feature |
| `fix` | Bug fix |
| `refactor` | Code improvement without behavior change |
| `test` | Tests only |
| `docs` | Documentation only |
| `build` / `ci` | Build, packages, CI |
| `chore` | Other maintenance |

`scope` is the module name: `gateway`, `identity`, `customers`, `inventory`...

Example: `feat(inventory): decrease stock on OrderPlaced event`

The body ends with `Refs: T-xxx` (housekeeping: the sprint, e.g. `Refs: Sprint 6`) and the `Co-Authored-By` trailer
when Claude wrote the change. Commits on a task branch may be short (`wip: …`) — only the squash commit stays.

## Merging (ADR-030)

- Every change reaches `main` through a **pull request**, merged as a **squash**: every task becomes one meaningful
  commit in history. Merge commits and rebase merges are switched off.
- The pull request's title and description become the squash commit's message, so they follow the format above.
- A ruleset on `main` requires the green `Build & test` check and blocks direct pushes, force pushes and deletion —
  with no bypass, the owner included. The owner presses **Squash and merge**; the branch is deleted on merge.
