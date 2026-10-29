---
name: plan
description: Investigate requested changes and produce implementation-ready plans as project-local Markdown artifacts, without implementing them. Use when a user requests an implementation plan or when planning is required before a multi-step change.
---

# Plan

Follow the repository's planning rules (root `AGENTS.md` or equivalent) where present; they take precedence over the defaults in this skill.

## Workflow

1. Investigate the relevant files and trace dependencies and data flow before designing changes.
2. Resolve the objective, acceptance criteria, scope, and constraints with the user; surface material ambiguity and implementation alternatives with their tradeoffs before finalizing decisions.
3. Identify the exact files/components to change, implementation steps, verification commands, compatibility effects, and risks.
4. Write the plan to the artifact path required by the active tool, user instructions, or repository planning rules — default a project-local Markdown file under `.agents/plans/` when unspecified, never a global or tool-internal directory. Use narrowly scoped Markdown checkbox steps (`- [ ]`, `- [x]` when done); no emojis, progress bars, or percentages.
5. Write only the plan artifact during planning; do not implement the planned changes until the user approves the plan.
