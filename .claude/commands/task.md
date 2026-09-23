---
description: Pick up a task from the board — move it to In Progress, create its branch and propose a plan
argument-hint: T-xxx
---

Pick up task **$ARGUMENTS** following [docs/process/workflow.md](../../docs/process/workflow.md).

## 1. Preconditions — stop and report if any fails

- `$ARGUMENTS` is a task id (`T-` + three digits) that exists in `docs/tasks.md`.
- The working tree is clean (`git status --short` prints nothing).
- If the task is **In Progress** and its branch exists: switch to that branch, summarize what is already done
  (`git log main..HEAD --oneline`, `git diff main --stat`) and what is left, then stop. This is a resume.
- If the task is **In Review** or **Done**: report its state and stop.
- If another task is already **In Progress**: tell the owner and ask whether to continue anyway.

## 2. Definition of Ready

Read the task, the ADRs it depends on in `docs/architecture.md`, and `docs/process/code-conventions.md`.
If the goal or acceptance criteria are unclear, untestable, or depend on a decision that isn't recorded as an ADR:
recommend running `/refine $ARGUMENTS` first and stop.

## 3. Start the task

1. Switch to `main` and, if `origin` exists, `git pull --ff-only`.
2. Set the task's state to **In Progress** in **both** `docs/tasks.md` and `tr/docs/tasks.md`.
3. Create the branch described in `docs/process/git-conventions.md`: `<type>/T-xxx-<short-kebab-description>`.

## 4. Propose a plan — do not write code yet

Reply to the owner **in Turkish** with:

- The goal and acceptance criteria in one short list
- The implementation plan: which projects/files change, the approach, and which tests prove each criterion
- Any decision the owner must make, as options with trade-offs and your recommendation first
- New concepts this task introduces, explained briefly (the owner is learning)

End by asking for the go-ahead. Start coding only after the owner approves the plan.
