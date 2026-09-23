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
7. **Close:** After approval it is merged and the task becomes *Done*. **Claude never marks a task Done on its own.**

## Project commands

The recurring steps above are packaged as Claude Code slash commands in [.claude/commands/](../../.claude/commands/):

| Command | Lifecycle steps | What it does |
| --- | --- | --- |
| `/refine T-xxx` | before 1 | Finds gaps in the acceptance criteria, asks the owner, rewrites them as testable checkboxes |
| `/task T-xxx` | 1–3 | Checks readiness, moves the task to *In Progress*, creates the branch, proposes a plan and waits |
| `/ship` | 5–7 | Builds, tests, checks criteria and conventions, moves to *In Review*, reports; after approval squash-merges, pushes and checks CI |

The commands never skip a human checkpoint: `/task` waits for plan approval, `/ship` waits for merge approval.

## Unplanned work

Anything that comes up outside the current task's scope:

> "Open a task for it, put it in the backlog, and let's continue."

This is how scope creep is prevented.

## Human checkpoints

| Checkpoint | Who decides |
| --- | --- |
| Architectural decisions | Owner |
| Sprint scope | Owner |
| Code review | Owner |
| Commit / push / merge | With the owner's approval |

## End of sprint

A "what we did, what's left, what we learned" summary is added to the sprint notes in `docs/tasks.md`.
