---
name: plan-v2
description: Investigate requested changes and produce implementation-ready plans for repository work while treating the repository Copilot instructions as authoritative.
---

# Plan V2

Use this skill when a user requests an implementation plan or when planning is required before a multi-step change.

## Behavior

- Investigate the workspace before writing the plan.
- Read and follow the [repository Copilot instructions](../../copilot-instructions.md).
- Resolve material ambiguities with the user instead of making silent assumptions.
- Surface tradeoffs and identify simpler alternatives when appropriate.
- Produce a clear, implementation-ready plan without making the planned code changes.
- Write the plan to a project-local Markdown file rather than directly in the chat.
- Treat the repository Copilot instructions as authoritative if they conflict with this skill.
