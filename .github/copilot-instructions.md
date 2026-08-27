# Copilot Instructions

## General Guidelines
- If there are ambiguities, ASK. Don't assume, and don't hide confusion. Surface tradeoffs. If multiple interpretations exist, present them - don't pick silently. If a simpler approach exists, say so. Push back when warranted. If something is unclear, stop. Name what's confusing. Ask.
- If changes break legacy compatibility and it's not explicitly stated how it should be handled, ASK. Don't assume.
- Never use emojis, slang, or informal language in code comments or documentation. Use clear, professional language.
- Never add yourself to the list of authors in code comments or documentation.
- Think critically about the requested changes and determine if they are a good solution/change or if there's a better approach. If you think there is a better approach, explain and ASK. Don't assume.

## Writing Plans
- When writing a plan:
  - Never use emojis, slang, or informal language.
  - Write a checkmark list with narrowly defined steps.
  - Use markdown checkboxes for each step. DO NOT ADD EMOJIS TO SHOW THEM AS DONE, e.g. "- [x] Step 1"
  - Don't add a progress bar or percentage completion to the plan. Use the checkboxes to indicate completed steps.

## Code Changes
- Avoid duplicating existing code. If you think a new function or class is needed, check if it already exists. If it does, use it instead of creating a new one.
- Custom agent profiles should avoid duplicating repository Copilot instructions, reference the instruction file instead, and explicitly treat those instructions as overriding the agent profile.

## CI/CD Workflow
- In GitHub Actions for this repo, tests should run after DLL artifact build, and workflow ordering should reflect that.
- Performance tests must run in the CI pipeline as blocking checks.

---
name: karpathy-guidelines
description: Behavioral guidelines to reduce common LLM coding mistakes. Use when writing, reviewing, or refactoring code to avoid overcomplication, make surgical changes, surface assumptions, and define verifiable success criteria.
license: MIT
---

# Karpathy Guidelines

Behavioral guidelines to reduce common LLM coding mistakes, derived from [Andrej Karpathy's observations](https://x.com/karpathy/status/2015883857489522876) on LLM coding pitfalls.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## Project Guidelines
- For this G-effects model, the desired behavior is that a 1→5 Gz+ ramp over 5 seconds should reach loss-of-consciousness between 25 and 35 seconds.
- Negative-G impairment should emerge from head overfill pressure and baroreceptor-induced bradycardia. Approximately −1 Gz should remain stable without runaway feedback, while sustained −4 to −5 Gz should cause loss of consciousness within approximately 4–6 seconds.
- Cerebral perfusion remains normalized to a maximum of 1; excess head blood contributes pressure impairment rather than additional oxygen delivery.
- All newly added physiological-model methods must be designed for minimal time-step dependence and stability at large dt; older methods may be reworked later, so unrelated existing compartment methods can be ignored for now.
- The physiological model must avoid fixed G-force deadzones and hand-authored effect ranges. Negative-G impairment should primarily emerge from baroreceptor-induced bradycardia, including extreme near-stop or irregular-heart behavior, and this response must be preserved for push-pull effects; push-pull may require a somewhat reduced positive-G heart-rate increase rate.
- Physiological model state integration should be as time-step independent as practical and remain stable without overshoot at large dt values.

## Visibility Semantics
- TunnelVisionLevel semantics: 1 means no visibility left, and 0.5 means half of the field of view is still free. TunnelVisionLevel should reach 1 when ConsciousnessLevel is close to 0, and tunnel vision should not be driven too directly by short perfusion recovery dips.