# Git Conventions

## Branches

- `main`: always builds and passes tests. No direct commits (except the initial setup).
- Task branch: `<type>/T-<no>-<short-description>`
  - Examples: `feat/T-012-create-customer`, `fix/T-020-tenant-filter`

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

## Merging

- When a task is done it is **squash merged** into `main`: every task becomes one meaningful commit in history.
- Build and tests must pass before merging (CI enforces this once the repo is on GitHub).
