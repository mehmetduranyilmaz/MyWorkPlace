---
description: Clarify a task's goal and acceptance criteria before work starts (Definition of Ready)
argument-hint: T-xxx
---

Refine task **$ARGUMENTS** so it meets the Definition of Ready in
[docs/process/task-conventions.md](../../docs/process/task-conventions.md). Do not write code and do not change the task's state.

## 1. Understand

Read the task, related ADRs in `docs/architecture.md`, neighbouring tasks on the board, and the code it will touch.

## 2. Find the gaps

Check each acceptance criterion and the task as a whole for:

- Vague or untestable wording ("works", "fast", "secure")
- Missing cases: authentication (401), plan access (403), tenant isolation, validation errors (400),
  conflicts (409), not found (404)
- Decisions not yet recorded as an ADR
- Hidden dependencies on other tasks
- A task too large to review comfortably in one sitting

## 3. Ask

Ask the owner **in Turkish** at most five focused questions. For each, give your recommended answer first
and a one-line reason, so the owner can simply confirm.

## 4. Update the board

After the owner answers:

1. Rewrite the goal and acceptance criteria as testable checkboxes, in `docs/tasks.md` (English) and `tr/docs/tasks.md` (Turkish).
2. Record any new decision as an ADR in `docs/architecture.md` and its `tr/` mirror.
3. If the task is too large, split it; new tasks take the next free `T-` number.
4. Summarize the changes in Turkish and suggest `/task $ARGUMENTS` as the next step.
