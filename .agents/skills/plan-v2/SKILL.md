---
name: plan-v2
description: Investigate requested changes and produce implementation-ready plans for repository work while treating root AGENTS.md as the repository instruction authority.
---

# Plan V2

Use this skill when a user requests an implementation plan or when planning is required before a multi-step change.

Read and follow the [repository AI instructions](../../../AGENTS.md), including their [planning rules](../../../AGENTS.md#writing-plans). AGENTS.md takes precedence over this skill for shared repository guidance.

## Workflow

1. Investigate the relevant files and trace dependencies and data flow before designing changes.
2. Resolve the objective, acceptance criteria, scope, and constraints with the user; surface material ambiguity and alternatives before finalizing decisions.
3. Identify the exact files/components to change, implementation steps, verification commands, compatibility effects, and risks.
4. Write the implementation-ready plan to the artifact location specified by the shared planning rules or active tool/user instructions, without implementing the planned changes.
