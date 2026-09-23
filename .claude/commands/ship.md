---
description: Hand over the current task — verify it, move it to In Review, and after approval squash-merge, push and check CI
argument-hint: (uses the current branch)
---

Hand over the task on the current branch following [docs/process/workflow.md](../../docs/process/workflow.md).

## 1. Identify the task

Read the task id from the branch name (`<type>/T-xxx-...`). If the current branch is `main` or has no task id, stop.

## 2. Verify — stop and report on the first failure

1. `dotnet build MyWorkplace.slnx` — must finish with **0 warnings, 0 errors**.
2. `dotnet test --solution MyWorkplace.slnx` — every test must pass.
3. Every acceptance criterion of the task is met and covered by a test where it is a business rule.
   Tick the met criteria in **both** boards; if any is not met, list it and stop.
4. Conventions (see `CLAUDE.md` and `docs/process/code-conventions.md`):
   - New or changed types and members have bilingual (`EN:` / `TR:`) XML doc comments.
   - No secrets, connection strings with passwords, or personal paths are committed.
   - Every changed file under `docs/` or `CLAUDE.md` has its `tr/` mirror updated; `README.md` and `README.tr.md` match.
   - New architectural decisions are recorded as ADRs.

## 3. Move to In Review

1. Set the state to **In Review** in both boards and add **Notes**: decisions made, scope added or dropped,
   anything the reviewer should know.
2. Commit all work on the task branch (a short `wip:` message is fine — it is squashed later).

## 4. Report and wait

Reply to the owner **in Turkish** with:

- What was done and **why** (key decisions, trade-offs)
- How to try it (commands, URLs, sample requests)
- Problems met on the way and how they were solved
- The proposed squash commit message (Conventional Commits, body with the main changes, `Refs: T-xxx`,
  and the same `Co-Authored-By` trailer as earlier commits)

Then **stop and ask for approval**. Do not merge or push without the owner's explicit approval.

## 5. After approval only

1. Set the task to **Done** in both boards and commit on the task branch.
2. `git switch main`, `git merge --squash <branch>`, commit with the approved message, delete the branch.
3. `git push origin main`.
4. Poll the latest run of the `CI` workflow for the pushed commit through the public GitHub API
   (`https://api.github.com/repos/<owner>/<repo>/actions/runs`) in the background and report every step's result.
5. If CI fails: explain the cause, prepare the fix on a `fix/T-xxx-...` branch and ask for approval again.
