---
description: Hand over the current task — verify it, move it to In Review, open its pull request; after approval mark it Done, and after the owner's merge check CI
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

## 3. Move to In Review and open the pull request

`main` is protected (ADR-030): nothing is merged locally and nothing is pushed to `main`.

1. Set the state to **In Review** in both boards and add **Notes**: decisions made, scope added or dropped,
   anything the reviewer should know.
2. Commit all work on the task branch (a short `wip:` message is fine — only the squash commit stays).
3. Write the proposed squash commit message to a file in the scratchpad: Conventional Commits subject; a body with the
   why and the main changes; `Refs: T-xxx`; the same `Co-Authored-By` trailer as earlier commits. Its first line is
   the pull request's title, the rest its description — GitHub turns both into the squash commit.
4. Push the branch (`git push -u origin <branch>`) and open the pull request:
   `gh pr create --base main --title "<subject>" --body-file <body file>`. If one is already open, update it with
   `gh pr edit`.
5. Wait for the pull request's `Build & test` check (`gh pr checks --watch`). If it fails, read the failed step's log
   (`gh run view <id> --log-failed`), fix the cause on the branch and push again (ADR-028: write the cause down first).

## 4. Report and wait

Reply to the owner **in Turkish** with:

- The pull request's link and its CI result
- What was done and **why** (key decisions, trade-offs)
- How to try it (commands, URLs, sample requests)
- Problems met on the way and how they were solved
- The squash commit message (it is the pull request's title and description)

Then **stop and ask for approval**. Nothing is marked Done without the owner's explicit approval.

## 5. After approval only

1. Set the task to **Done** in both boards, commit on the task branch, push, and wait until the pull request's
   `Build & test` is green again.
2. Ask the owner to press **Squash and merge** on the pull request (the owner merges — Claude never does).
3. After the owner has merged: `git switch main`, `git pull --ff-only`, delete the local branch
   (`git branch -D <branch>`; GitHub deletes the remote one).
4. Watch `main`'s CI for the merge commit (`gh run list --branch main --limit 1`, `gh run watch <id>`) and report the
   result.
5. If CI fails: explain the cause, prepare the fix on a `fix/T-xxx-...` branch and open its pull request.
