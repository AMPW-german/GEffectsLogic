# Contributing

Please read [CLA.md](CLA.md) before contributing. Use a short branch (`feat/...`, `fix/...`, or `chore/...`) and keep each PR focused on one issue.

## Commit messages

Every commit and PR title must use `type(scope): lowercase subject`. Scope is optional; `!` before the colon marks a breaking change. Allowed types: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `style`, `build`, `ci`, `chore`, `revert`. Do not end the subject with a period.

Examples: `feat(physics): model lateral acceleration`, `fix!: correct consciousness threshold`, `chore(release): v2.1.0`. `Update stuff` is invalid.

Install the local commit-message hook once per clone:

```powershell
./scripts/install-git-hooks.ps1
```

If PowerShell is unavailable, run `git config --local core.hooksPath .githooks`. The hook works in Visual Studio 2026 on Windows (Git for Windows) and Rider on macOS. CI validates every PR commit and its title independently of your local setup.

## Pull requests

Link a GitHub issue in the PR body, such as `Fixes #123`. Titles with type `docs`, `style`, `build`, `ci`, `chore`, or `revert` do not need an issue. If an issue is genuinely unnecessary for another type, apply the `no-issue` label. PRs exceeding 800 changed lines (excluding `CHANGELOG.md`) require the `large-change` label and an explanation. The branch must be squash-merged into `main`.

## Lint and tests

The .NET SDK and `.editorconfig` are shared by Visual Studio 2026 and Rider. From the repo root:

```bash
dotnet restore GEffectsLogic.slnx
dotnet format whitespace GEffectsLogic.slnx --verify-no-changes
dotnet format style GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet format analyzers GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet build GEffectsLogic.slnx -c Release -warnaserror
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category!=Experimental&Category!=Performance"
bash scripts/enforce-license-headers.sh --check
```

Remove `--verify-no-changes` from `dotnet format` to apply fixes. Run `bash scripts/enforce-license-headers.sh` to repair missing GPL headers; `DRY_RUN=1` previews changes without writing. Both IDEs use `.editorconfig` to insert the same header into new C# files. CI also checks YAML, workflows, Markdown, shell, PowerShell, JSON, and XML. The `.github/workflows/lint.yml` job pins the CLI linter versions and is the authoritative invocation for non-C# file types.

## AI instructions and IDE setup

[AGENTS.md](AGENTS.md) is the canonical repository-wide AI instruction source. Edit shared rules there, including the [commit and PR guidance](AGENTS.md#commit-messages-and-pull-requests). Repository skills live in [.agents/skills/](.agents/skills/) and reference the shared rules; the [Copilot instructions file](.github/copilot-instructions.md) is only a discovery adapter, not another policy source. Providers without automatic AGENTS.md or neutral skill discovery must explicitly load/read the relevant files. There is no separate CLAUDE.md entry point.

Agent instruction discovery and IDE commit-message buttons are different features:

- **Visual Studio 2026 / GitHub Copilot:** current [Microsoft documentation](https://learn.microsoft.com/en-us/visualstudio/version-control/git-make-commit?view=visualstudio) directs commit-message instructions to the repository Copilot file; the former global commit-message instruction field no longer applies. This repository uses a reference-only adapter to AGENTS.md. Whether the button follows that reference must be tested in the installed VS/Copilot version; do not assume it does because chat can read the file. Generate a message for actual pending changes without committing, record the version and raw subject, and validate it with `scripts/check-commit-subject.sh`. If the button does not load the linked rules, report the integration as blocked rather than copying rules into the adapter or claiming full support.
- **JetBrains Rider / AI Assistant:** in the Commit tool window's AI Assistant settings, configure **Prompt for generation**, or use **Settings → Tools → AI Assistant → Prompt Library → Commit Message Generation**. Copy the canonical AGENTS.md commit/PR section into that user-level prompt and refresh it whenever the source changes. A path-only reference is not a verified import mechanism. Generate a message for actual pending changes and validate the subject with the shared script. See the [JetBrains instructions](https://www.jetbrains.com/help/ai-assistant/ai-in-vcs-integration.html); this setup is manual and does not alter repository rules.
- **Devin:** agents support AGENTS.md and neutral skills. The separate [Desktop commit-message button](https://docs.devin.ai/desktop/ai-commit-message) does not document custom-instruction loading; its customization is skipped unless a supported mechanism is available. Agent support does not establish button support.

IDE-generated messages remain suggestions: review them against the canonical rules and validate their subjects using Bash as described in AGENTS.md. Do not bypass the existing commit hook or CI checks. Manual IDE setup and button smoke tests require the user's installed integrations; they are not verified by Markdown lint or agent discovery alone.

## Release

Open a PR titled `chore(release): vX.Y.Z` that increments `VersionPrefix` in `GEffectsLogic/GEffectsLogic.csproj`. CI generates and commits the corresponding `CHANGELOG.md` section to the PR branch. Merge after all checks pass; the build workflow publishes the zip and GitHub release on `main`.
