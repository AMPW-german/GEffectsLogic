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

## Release

Open a PR titled `chore(release): vX.Y.Z` that increments `VersionPrefix` in `GEffectsLogic/GEffectsLogic.csproj`. CI generates and commits the corresponding `CHANGELOG.md` section to the PR branch. Merge after all checks pass; the build workflow publishes the zip and GitHub release on `main`.
