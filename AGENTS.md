# Repository AI Instructions

This file is the authoritative repository-wide guidance for AI agents, independent of provider. Repository skills and custom agent profiles must reference it instead of duplicating shared rules; these rules take precedence over repository skill/profile guidance, not platform or system instructions. Edit shared instructions here, not in provider discovery adapters.

## Project overview and architecture

GEffectsLogic is a C# framework for simulating physiological G-force effects on humans, originally developed for the KSA GEffects mod and usable as a general-purpose library.

The [solution](GEffectsLogic.slnx) contains three projects:

- **GEffectsLogic**: the production library, targeting .NET Framework 4.8.1 and .NET 10.
- **GEffectLogicTests**: .NET 10 xUnit tests that simulate force profiles through instance updates.
- **GraphicLogicTest**: a .NET 10 Avalonia/LiveCharts application for interactive visualization and tuning, built and linted in CI.

[GEffectsLogicInstance](GEffectsLogic/GEffectsLogicInstance.cs) is the per-entity interface. It owns a [PhysiologicalModel](GEffectsLogic/PhysiologicalModel.cs), manages time and last forces, exposes physiological/visual outputs, and handles stabilization. There is no internal instance registry or auto-generated identifier; consumers manage instances themselves.

The public entry point is `GEffectsLogicInstance.Update(deltaTime, currentGx, currentGy, currentGz)`. It splits positive time steps greater than 0.5 seconds and delegates to `PhysiologicalModel.Update(dt, gx, gy, gz)` with the same axis order. Stable conditions can skip physics updates. Gz affects circulation/perfusion, Gx includes respiratory hypoxia and tolerance effects, and Gy includes tolerance reduction, lung compression, and neck fatigue. The current model uses three blood compartments, not a cumulative Gz accumulator.

[LogicSettings](GEffectsLogic/LogicSettings.cs) is a sealed immutable record of `init`-only numeric physiological parameters. Each instance resolves numeric settings through its selected profile: the fixed `LogicSettings.Default`, or a caller-owned `with` copy supplied at construction or via `ApplySettings`. `DebugMode` and `SuppresInfoLogs` remain global static logging flags. The library does not register or manage profiles or instances. The library project produces framework-specific assemblies; its current project file does not define a post-build copy into mod content.

[Logger](GEffectsLogic/Logging/Logger.cs) is the abstract logging extension point. A consumer can pass a concrete logger to the instance constructor or set `Logger.Instance`; the instance logger takes precedence. Without either, logging safely does nothing, so assigning the singleton is not a prerequisite for `Update`. Tests use xUnit output-backed logging; the GUI uses console logging.

Read the [internal design documentation](GEffectsLogic/Logic.md) before changing the physiological model or G-axis behavior. It describes intended mechanisms, but some prose and test profiles differ from the desired constraints below. Surface material disagreements before tuning rather than silently changing the model, tests, or requirements.

## General behavior

The behavioral guidance here incorporates the MIT-licensed Karpathy Guidelines, derived from [Andrej Karpathy's observations](https://x.com/karpathy/status/2015883857489522876). It favors caution over speed; use judgment for trivial tasks.

- State assumptions explicitly. If intent is ambiguous, stop, name what is unclear, and ask; do not hide confusion or choose an interpretation silently.
- Surface tradeoffs and simpler alternatives. Think critically about whether the requested change is a suitable solution, explain a better approach when warranted, and ask before changing direction.
- If legacy compatibility would break and handling is not explicitly specified, ask rather than assume.
- Use clear, professional language. Never use emojis, slang, or informal language in code comments, documentation, or plans.
- Never add yourself to the list of authors in code comments or documentation.

## Code changes

- Make the minimum change that solves the request. No speculative features, single-use abstractions, unrequested flexibility/configurability, or error handling for impossible scenarios. If 200 lines can be 50, simplify.
- Check for existing functions/classes before adding new ones; reuse them instead of duplicating code.
- Match existing conventions and style. Do not improve unrelated code, comments, or formatting, refactor working code, or delete pre-existing dead code; mention unrelated findings instead.
- Remove imports, variables, or functions made unused by your own changes. Every changed line should trace to the request.
- Define verifiable success criteria and iterate until verified. For a bug, write a reproducing test where applicable, fix it, and confirm it passes. For validation, test rejected inputs. For refactoring, verify behavior before and after.
- For multi-step work, identify narrowly scoped steps and the verification for each; do not settle for an untestable goal such as "make it work."

## Writing plans

- Use the [planning skill](.agents/skills/plan-v2/SKILL.md) for implementation plans.
- Write narrowly defined steps as Markdown checkboxes (`- [ ]` and `- [x]`). Do not use emojis to indicate completion, progress bars, or percentages.
- Follow the active tool/user's specified plan artifact path. If none is specified, write a project-local Markdown file under `.agents/plans/`, rather than placing the full plan only in chat.
- During planning, investigate without implementing; write only the permitted plan artifact. Resolve material ambiguity before finalizing the plan, and wait for approval before starting implementation.

## Build and verification

Run commands separately from the repository root, using the .NET 10 SDK. The [contribution guide](CONTRIBUTING.md#lint-and-tests) and [lint workflow](.github/workflows/lint.yml) document the shared tooling and pinned non-C# linters.

```bash
dotnet restore GEffectsLogic.slnx
dotnet format whitespace GEffectsLogic.slnx --verify-no-changes
dotnet format style GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet format analyzers GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet build GEffectsLogic.slnx -c Release -warnaserror
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category!=Experimental&Category!=Performance"
bash scripts/enforce-license-headers.sh --check
```

Run performance tests in Release, separately from normal Debug tests:

```bash
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Release --filter "Category=Performance"
```

Experimental time-step stability tests are separate and non-blocking in current CI; report their results without conflating them with the normal test gate:

```bash
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category=Experimental"
```

To select a single test or launch the interactive GUI:

```bash
dotnet test GEffectLogicTests/GEffectLogicTests.csproj --filter "FullyQualifiedName~Test1"
dotnet run --project GraphicLogicTest
```

Choose verification appropriate to the change, following existing CI rather than disabling checks. New C# files require the canonical GPL header. A documentation-only change normally needs Markdown checks and relevant metadata/link validation, not a new physiological test run.

If Bash is not on Windows PowerShell's PATH, locate Git for Windows' `bin/bash.exe` and use PowerShell's call operator with its verified installation path. For example, only if that path exists:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' scripts/enforce-license-headers.sh --check
```

Do not assume a particular installation directory or treat `bash` as a PowerShell command guaranteed to be available.

## Commit messages and pull requests

These rules apply to agent-written and IDE-generated commit subjects and PR titles. Review the actual changes and their intent before choosing a message. Follow the [contribution policy](CONTRIBUTING.md); [scripts/check-commit-subject.sh](scripts/check-commit-subject.sh) is the executable format authority shared by the local hook and CI.

- Format every subject/title as `type(scope)!: lowercase subject`. Scope and `!` are optional; `!` before the colon marks a breaking change. Use the lowercase-subject writing convention, start with a lowercase letter, and do not end with a period.
- Allowed types: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `style`, `build`, `ci`, `chore`, `revert`.
- If a scope is supplied, use lowercase letters, digits, `/`, `_`, or `-` inside parentheses.
- Put only the subject in the subject/title field: no Markdown fences, bullet prefixes, or explanatory preamble. Optional commit body/footer follows a blank line; required tool attribution trailers are not prohibited by the code/documentation authorship rule.
- The validator checks the lowercase initial subject letter, not every subject character. Do not claim passing the script alone enforces all writing guidance.

Valid examples:

```text
docs(ai): centralize repository instructions
fix!: correct consciousness threshold
chore(release): v2.1.0
```

Invalid examples:

```text
Update stuff
docs: Centralize instructions
docs: centralize instructions.
```

Before committing or submitting a PR title, validate the intended subject/title and correct failures; do not bypass the hook or weaken CI:

```bash
bash scripts/check-commit-subject.sh 'docs(ai): centralize repository instructions'
```

In PowerShell, use a verified Git for Windows Bash path if necessary:

```powershell
& 'C:\Program Files\Git\bin\bash.exe' scripts/check-commit-subject.sh 'docs(ai): centralize repository instructions'
```

- Keep branches short (`feat/...`, `fix/...`, or `chore/...`) and each PR focused on one issue. Follow the existing PR template.
- Link an issue in the PR body, for example `Fixes #123`. Types `docs`, `style`, `build`, `ci`, `chore`, and `revert` are exempt; otherwise use the existing `no-issue` label if an issue is genuinely unnecessary.
- PRs exceeding 800 changed lines, excluding `CHANGELOG.md`, require the `large-change` label and an explanation. The branch must be squash-merged into `main`.
- See the contribution guide for hook installation, CLA requirements, and the release procedure; do not change those policies as part of message generation.

## CI/CD workflow

- Tests must run after DLL artifact build; workflow dependency ordering must reflect that.
- Performance tests must run as blocking CI checks. Experimental tests currently remain non-blocking.

## Physiological-model constraints

These are desired design constraints, not claims that every existing method or test already satisfies them:

- A 1→5 Gz+ ramp over five seconds should reach loss of consciousness between 25 and 35 seconds.
- Negative-G impairment should emerge from head-overfill pressure and baroreceptor-induced bradycardia. Approximately −1 Gz should remain stable without runaway feedback; desired sustained −4 to −5 Gz loss-of-consciousness timing is approximately 4–6 seconds.
- Cerebral perfusion remains normalized to a maximum of 1. Excess head blood contributes pressure impairment rather than additional oxygen delivery.
- Physiological state integration should be as time-step independent as practical and remain stable without overshoot at large dt. All new physiological-model methods must meet these design requirements; unrelated older compartment methods need not be reworked solely to satisfy new-method requirements.
- Avoid fixed G-force deadzones and hand-authored effect ranges in new model work. Negative-G impairment should primarily emerge from baroreceptor-induced bradycardia, preserving extreme near-stop or irregular-heart behavior and its role in push-pull effects. Push-pull may require a somewhat reduced positive-G heart-rate increase rate.

The current README/design prose instead describes a 20–30-second positive-G target. The 5 Gz duration test uses 25–35 seconds but ramps at approximately 1 G/s, not the exact five-second profile above. Current −4/−5 Gz test bounds are 6–11 seconds, rather than the desired 4–6 seconds. Do not silently retune the model or change test expectations to reconcile these differences; clarify timing/profile requirements before relevant changes.

## Visibility semantics

- `VisualTunnelVisionLevel` is the physiological hypoperfusion channel: 1 means no visibility remains and 0.5 means half of the field is still free. It must not be driven by consciousness or short perfusion recovery dips.
- `VisualRedoutLevel` independently represents the head-overfill symptom.
- `VisualLoCLevel` is the final full-screen blackout override while unconscious. Clients compose the independent visual channels, then apply this override last.

## Repository skills

- [Plan V2](.agents/skills/plan-v2/SKILL.md): investigate and produce implementation-ready plans without making the planned changes.
- [Karpathy Guidelines](.agents/skills/andrej-karpathy-skills/SKILL.md): apply the shared behavioral/code-change guidance during implementation, review, and refactoring.

The canonical skill location is `.agents/skills/`. When a provider cannot discover these skills or `AGENTS.md` automatically, explicitly read/load the relevant files; do not create independently maintained provider copies. See [AI instructions and IDE setup](CONTRIBUTING.md#ai-instructions-and-ide-setup) for the distinction between agent discovery and IDE commit-message buttons.
