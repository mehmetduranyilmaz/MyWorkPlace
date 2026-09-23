# Task Conventions

Tasks live in [docs/tasks.md](../tasks.md). (They may later move to Linear or GitHub Issues;
the conventions stay the same.)

## Numbering

`T-001`, `T-002`, ... A number is assigned once and never reused.

## States

| State | Meaning |
| --- | --- |
| Backlog | Idea or deferred work, not planned yet |
| Todo | Planned for this sprint |
| In Progress | Being worked on (preferably one at a time) |
| In Review | Code and tests ready, waiting for the owner's review |
| Done | Approved and merged |

## Task template

```markdown
### T-012 — Create customer endpoint

- **State:** Todo
- **Module:** Customers (Basic)
- **Goal:** Let a company add a new customer.
- **Acceptance criteria:**
  - [ ] `POST /customers` creates a customer with name and email
  - [ ] The same email can't be added twice within a company (409)
  - [ ] The customer appears only in the creating company's list
- **Notes:** (added during implementation: decisions, PR link, summary)
```

## Definition of Ready (to start)

- Goal and acceptance criteria are clear
- Required architectural decisions are made

## Definition of Done (to finish)

- Acceptance criteria are met
- Tests written and passing; build has no warnings
- Docs updated if needed (including the `tr/` mirror)
- The owner approved
