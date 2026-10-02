# Workflow

In this project Claude Code works as a **team member**. The owner (product owner / architect) decides;
Claude presents options, implements and explains.

## Memory model

Claude does not remember previous sessions. Persistent memory lives in files:

```text
Owner  ←→  docs/tasks.md (work: what's done, what's left)  ←→  Claude session
                        ↑
      CLAUDE.md + docs/architecture.md (rules and decisions)
```

## Starting a session

> "Look at docs/tasks.md; list In Progress and Todo tasks. Where were we?"

## Task lifecycle

1. **Pick:** The owner picks a task from Todo.
2. **Start:** Claude moves it to *In Progress* and creates a branch (see [git-conventions.md](git-conventions.md)).
3. **Clarify:** Unclear acceptance criteria are resolved before coding.
4. **Implement:** Code and tests. If an architectural decision is needed, stop and present options.
5. **Hand over:** Tests pass, the task moves to *In Review*, and Claude writes a Turkish summary:
   what was done, why, and how to try it.
6. **Review:** The owner reads the code and tries it.
7. **Close:** After the owner's approval Claude adds the *Done* state to the pull request, and the owner merges it by
   pressing **Squash and merge** on GitHub (ADR-030). **Claude never marks a task Done on its own.**

## Project commands

The recurring steps above are packaged as Claude Code slash commands in [.claude/commands/](../../.claude/commands/):

| Command | Lifecycle steps | What it does |
| --- | --- | --- |
| `/refine T-xxx` | before 1 | Finds gaps in the acceptance criteria, asks the owner, rewrites them as testable checkboxes |
| `/task T-xxx` | 1–3 | Checks readiness, moves the task to *In Progress*, creates the branch, proposes a plan and waits |
| `/ship` | 5–7 | Builds, tests, checks criteria and conventions, moves to *In Review*, opens the pull request and reports; after approval adds *Done* to it, and once the owner has merged it checks `main`'s CI |

The commands never skip a human checkpoint: `/task` waits for plan approval, `/ship` waits for the owner's approval
and the owner's merge.

## Pull requests (ADR-030)

`main` is protected by a GitHub ruleset: every change — a task, a sprint note, a typo — reaches it through a pull
request whose `Build & test` check is green, and only as a squash merge. Nobody can push to `main` directly, the owner
included. Claude opens pull requests and reads CI with the GitHub CLI (`gh`), which the owner signed in once; the
owner presses **Squash and merge**. The squash commit takes the pull request's title and description, so the message
prepared by `/ship` is the one that lands in history.

## Unplanned work

Anything that comes up outside the current task's scope:

> "Open a task for it, put it in the backlog, and let's continue."

This is how scope creep is prevented.

## A red CI run

A red run is a finding, not bad luck (ADR-028):

1. Read the failing tests (annotations and the job summary name them; `gh run view <id> --log-failed` shows the
   failed step's log) and find the cause.
2. Before re-running, write the cause down — on the task, or in the sprint notes if it belongs to no task. The CI
   summary shows the attempt number, and a re-run shows a warning, so a silent re-run is visible.
3. Fix the cause; if it is a flaky test, fix the test or its infrastructure. Tests are never retried automatically.

## Human checkpoints

| Checkpoint | Who decides |
| --- | --- |
| Architectural decisions | Owner |
| Sprint scope | Owner |
| Code review | Owner |
| Commit / push | With the owner's approval |
| Merge into `main` | The owner, by pressing **Squash and merge** (ADR-030) |

## End of sprint

A "what we did, what's left, what we learned" summary is added to the sprint notes in `docs/tasks.md`, through a
pull request from a branch like `docs/sprint-6-close`.
