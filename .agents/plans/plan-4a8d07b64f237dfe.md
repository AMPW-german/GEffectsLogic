---
agent: devin-local
session: dawn-class
created: 2026-09-30T00:00:00Z
---

# Standardized Commit Format, Changelog, PR Enforcement, Repo-Wide Linting

Adopt Conventional Commits enforced on every commit (local hook + CI), require issue links on non-trivial PRs, enforce narrow squash-merged PRs via a size check and the existing main ruleset, auto-generate a committed CHANGELOG.md in CI on release PRs via git-cliff, add repo-wide code linting (full .NET analyzers + `dotnet format` for C#; yamllint/actionlint/markdownlint/shellcheck/PSScriptAnalyzer/prettier for everything else; a check-mode license-header script), and rename `dotnet.yml` to `build.yml`. All workflows use **only official, still-maintained GitHub actions** (`actions/*`, `github/*`); every other check runs as a pinned CLI tool in `run:` steps. All local tooling works identically on Windows (VS2026, Git for Windows sh, PowerShell) and macOS (Rider, bash, pwsh).

## Summary

Introduce a Conventional Commits workflow to the repo: every commit message is validated (`type(scope): subject`), PRs must link a GitHub issue unless they are a trivial type, `main` only accepts narrow squash-merged PRs, releases get a categorized `CHANGELOG.md` entry generated automatically in CI via a pinned git-cliff binary, and every file type in the repo is linted. Third-party actions are not used anywhere — official GitHub actions plus CLI linters only.

## Current State (verified via `gh api` and file inspection)

- Solo repo `AMPW-german/GEffectsLogic`; history mixes WIP commits, multi-purpose commits, and inconsistent subjects on a single long-lived branch (`feat/multiAxialForces`).
- **Existing ruleset `main` (id `18064083`, active, `~DEFAULT_BRANCH`)**: deletion blocked, non-fast-forward blocked, pull request required (0 approvals, stale reviews dismissed, thread resolution required, all merge methods allowed). PRs are already mandatory; `GITHUB_TOKEN` cannot push to `main` (no bypass actors), so any automated commit must land on a PR branch.
- Repo merge config: `merge`/`squash`/`rebase` all enabled; `squash_merge_commit_title: COMMIT_OR_PR_TITLE`; `squash_merge_commit_message: COMMIT_MESSAGES`; `delete_branch_on_merge: true`.
- `.github/workflows/dotnet.yml` already creates a GitHub release when `<VersionPrefix>` in `GEffectsLogic/GEffectsLogic.csproj` changes on `main`, using `generate_release_notes: true` (uncategorized — no `.github/release.yml`). It already complies with the action policy: only `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `actions/download-artifact` + bash/curl/jq.
- **Action policy (per user): only official GitHub-maintained actions (`actions/*`, `github/*`). No third-party actions anywhere.** Everything else runs as CLI tools (pinned versions) in `run:` steps — this matches the existing hand-rolled bash + curl + jq style.
- `pull_request_template.md` and issue templates exist but are advisory; blank issues disabled.
- No hooks directory, no commit linting, no CONTRIBUTING.md, no `.githooks`.
- `gh` binary exists on PATH — repo settings can be applied via API instead of manual UI steps.
- **License headers verified:** all 24 `.cs` files across all three projects (`GEffectsLogic`, `GEffectLogicTests`, `GraphicLogicTest`) already carry the canonical GPL v3 header. `scripts/enforce-license-headers.sh` exists as a *fix-mode* script (inserts/refreshes headers, strips BOM; `DRY_RUN=1` reports but always exits 0 — needs a `--check` mode to become a CI gate).
- **Linting state:** `.editorconfig` exists but contains only `file_header_template` (the GPL v3 text — already IDE0073-ready and honored by both VS2026 and Rider). No analyzers enabled, no `Directory.Build.props`, no style rules, no linter configs.
- **Lint scope inventory** (tracked files, excluding .git/bin/obj/.vs/.idea): 24 `.cs`, 12 `.md`, 4 `.yml`, 4 `.axaml`, 3 `.csproj`, 1 `.sh`, 1 `.json`, plus `LICENSE`, `.slnx`, `.editorconfig`, `.gitignore`, `.gitattributes`.
- `GEffectsLogic.slnx` lists `.github/workflows/dotnet.yml` under Solution Items — the file path entry must be updated when the workflow is renamed.
- `GraphicLogicTest` is an **Avalonia** app (`.axaml`), not WPF as CLAUDE.md states.
- `.gitattributes` has `* text=auto` — line endings normalize to LF in the repo; `.editorconfig` should therefore not pin `end_of_line` (each platform keeps its native checkout style).
- Ubuntu runners already provide `shellcheck`, `pwsh`, `python3`/`pip`, `go`, `node`/`npx`, `jq`, `git`, `gh` — all CLI linters install via these without third-party actions.

## Options Comparison (evaluated)

### Commit format
- **Conventional Commits `type(scope): subject` — chosen.** Maximum tool support (git-cliff, commitlint, IDE plugins); `!`/`BREAKING CHANGE:` footer covers breaking changes.
- Simple `type: subject`: lighter but loses scope granularity for the same enforcement cost.
- Custom format (e.g. `[feat] ...`): needs bespoke changelog parsing; rejected.
- Free-form + label-driven changelog: works with GitHub notes but puts all discipline on labels; rejected since enforcement on commits was chosen.

### Enforcement point
- **Every commit — chosen.** commit-msg hook locally + CI validates all commit subjects in a PR plus the PR title (which becomes the squash-commit message).
- PR-title-only: lower friction (WIP commits allowed on branches) but branch history stays messy; user explicitly wants every commit.

### Commit/PR title validation mechanism
- **Bash regex, shared verbatim with the commit-msg hook — chosen.** One regex is the single source of truth; zero dependencies; the PR-title step and per-commit loop are each ~10 lines.
- commitlint via `npx @commitlint/cli` + `commitlint.config.mjs`: richer body/footer rules but pulls Node tooling into a .NET repo for marginal gain; rejected (revisit if rules outgrow the regex).
- Third-party commit/PR-title actions (`wagoid/commitlint-github-action`, `amannn/action-semantic-pull-request`): rejected — third-party actions are not allowed.

### Issue requirement
- **Required with exemptions — chosen.** CI fails the PR unless the body links an issue (`#N` / `Fixes #N`), exempted for `docs|style|build|ci|chore|revert` titles or a `no-issue` label. This forces issues only where traceability matters, keeping chores cheap.
- Always required: maximum traceability, too much overhead for solo work.
- Optional: loses the scoping benefit the user wants.
- Implementation: `actions/github-script` (official action, structured octokit access) rather than curl + jq against the REST API.

### Changelog generation point
- **Pinned git-cliff release binary via curl in `changelog.yml`, committing to the release PR — chosen.** Detects a `VersionPrefix` diff on `pull_request`, runs `git-cliff --unreleased --tag vX.Y.Z --prepend CHANGELOG.md`, pushes a `chore(release)` commit to the PR branch with plain `git`. Version-pinned download for reproducibility; no cargo install.
- `orhun/git-cliff-action`: cleaner but third-party — rejected per policy.
- Post-merge commit to `main`: impossible — the ruleset blocks direct pushes and `GITHUB_TOKEN` cannot be a bypass actor.
- Local maintainer script: adds a manual step and a git-cliff install; user wants full automation.
- GitHub-native release notes (labels + `.github/release.yml`): changelog only lives on GitHub; rejected (still usable as a complement later).
- release-please: automates version-bump PRs and changelog entirely; heavier, restructures the existing VersionPrefix trigger; not adopted.
- Retroactive seeding from existing tags: dropped — existing history untouched; `CHANGELOG.md` starts fresh.

### Release creation (build.yml internals)
- **Keep the existing hand-rolled curl/jq draft→upload→publish block — chosen.** It already works and is policy-compliant (bash, not a third-party action). The only changes to the file are the rename and `name:` update.
- `softprops/action-gh-release`: would shrink the workflow but is third-party — rejected per policy.
- Optional behavior improvement flagged for the PR: `needs: [build-artifacts, test]` on `release-artifacts` so a release never publishes ahead of green tests.

### License header linting
- **`scripts/enforce-license-headers.sh` gains a `--check` mode — chosen.** Same script stays the single header authority locally (fix) and in CI (check): `--check` exits 1 listing non-compliant files without writing. `SRC_DIR` default widened to repo root (all three projects; bin/obj already excluded). POSIX bash + awk runs identically under Git for Windows (VS2026 commits/hooks) and macOS bash/zsh (Rider).
- `dotnet_diagnostic.IDE0073` (file header) additionally enabled at `warning` via the existing `file_header_template` — a second, native enforcement path for `.cs` through `dotnet format style`, and it makes both IDEs auto-insert the header on new files. Redundant coverage is intentional (same header text in both configs).
- `apache/skywalking-eyes` (license-eye): dedicated header checker but third-party — rejected per policy.
- Checking via `DRY_RUN=1` + parsing output: fragile; an explicit exit code is cleaner.

### Code linting — C# (entire repo, all 24 .cs files / all 3 projects)
- **`dotnet format` + full .NET analyzers — chosen.** New root `Directory.Build.props` applies `EnableNETAnalyzers=true`, `AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild=true` to every project. `.editorconfig` is expanded with a standard .NET style rule set tuned to existing conventions plus `dotnet_diagnostic.IDE0073.severity = warning`. CI runs `dotnet format whitespace|style|analyzers --verify-no-changes` over the solution and `dotnet build -warnaserror` (catches analyzer diagnostics that have no code fix and thus escape `format --verify`). Requires a one-time repo-wide fix commit. Everything is SDK/CLI + .editorconfig — identical behavior in VS2026 and Rider.
- Whitespace/style only: minimal churn but leaves real issues (CA rules, header rule) unenforced; user chose full analyzers.
- StyleCop.Analyzers package: second opinionated rule universe overlapping built-in CA/IDE rules; more tuning surface for little gain on a solo repo; not adopted.
- Per-project analyzer config instead of `Directory.Build.props`: duplicates settings across three csproj files; rejected.

### Code linting — non-C# file types (no third-party actions available → CLI tools in `run:` steps)
- **Pinned CLI linters on the ubuntu runner — chosen**, one `misc` job in `lint.yml`:
  - **YAML** (`*.yml`, issue templates): `yamllint` via `pip install yamllint==<pin>`, config `.yamllint` at root.
  - **GitHub workflows**: `actionlint` via `go install github.com/rhysd/actionlint/cmd/actionlint@<pin>` (go is preinstalled).
  - **Markdown**: `markdownlint-cli2` via `npx --yes markdownlint-cli2@<pin>`, config `.markdownlint-cli2.jsonc` (rules + ignores for `.agents/`).
  - **Shell** (`.sh`, `.githooks/commit-msg`): `shellcheck` — preinstalled on ubuntu runners.
  - **PowerShell** (`scripts/*.ps1`): `pwsh` (preinstalled) → `Install-Module PSScriptAnalyzer -RequiredVersion <pin>` → `Invoke-ScriptAnalyzer`.
  - **JSON**: `npx --yes prettier@<pin> --check "**/*.json"`.
  - `.axaml`/`.csproj`/`.slnx` (XML): no standalone linter — `.editorconfig` indent rules keep them consistent in both IDEs.
- `super-linter/super-linter`: covers all of the above in one step but is a third-party action — rejected per policy.
- Per-file-type third-party actions (reviewdog, markdownlint-cli2-action, ...): same policy rejection, plus more pins to babysit than the CLI equivalent.
- Optional official-action follow-up: `github/codeql-action` (GitHub-maintained SAST for C#) — noted for later, not in scope.

## Commit Format Specification

- Header: `type(scope)!: subject` — scope optional, `!` marks breaking.
- Types: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `style`, `build`, `ci`, `chore`, `revert`.
- Suggested scopes (documented, not enforced): `physics`, `visuals`, `gui`, `tests`, `ci`, `release`.
- Subject: imperative, lowercase start, no trailing period.
- Validation regex (single source of truth, shared by hook and CI): `^(feat|fix|perf|refactor|test|docs|style|build|ci|chore|revert)(\([a-z0-9/_-]+\))?!?: .+`

## Implementation Steps

### A. Local hooks & tooling (Windows VS2026 + macOS Rider parity)
- [ ] Add `.githooks/commit-msg` — POSIX sh script that rejects non-conforming subjects with a helpful error listing valid types. Runs under Git for Windows' bundled sh (VS2026 commits) and macOS sh/bash.
- [ ] Add `scripts/install-git-hooks.ps1` — runs `git config core.hooksPath .githooks`; works under Windows PowerShell and macOS pwsh. CONTRIBUTING also documents the raw `git config` one-liner for shells without PowerShell.
- [ ] Update `scripts/enforce-license-headers.sh`:
  - Add `--check` mode: report non-compliant files and exit 1 without modifying anything (`DRY_RUN` already prints "WOULD CHANGE"; `--check` adds the failing exit code + summary count).
  - Widen default `SRC_DIR` to the repo root so all three projects are covered; the `*/Tests/*` exclusion does not match `GEffectLogicTests` — decide whether test files keep headers (recommended: yes, they already have them) and fix the exclusion accordingly.
  - Restore the executable bit lost in the working tree (`git update-index --chmod=+x`).
- [ ] Verify hook + scripts run on both Windows (Git bash) and macOS (bash) before merging.

### B. Lint & format configuration
- [ ] Add `Directory.Build.props` at repo root — `EnableNETAnalyzers=true`, `AnalysisLevel=latest-all` (full CA ruleset), `EnforceCodeStyleInBuild=true` (IDE style rules run at build time), `LangVersion=latest` (deduplicated from csproj files where already set).
- [ ] Expand `.editorconfig` — keep `file_header_template`; add `dotnet_diagnostic.IDE0073.severity = warning`; add a standard .NET style section matching existing conventions (indent 4 for `.cs`/`.csproj`/`.axaml`/`.slnx`, indent 2 for `.yml`/`.yaml`, `trim_trailing_whitespace = false` for `.md`, `insert_final_newline = true`; do **not** pin `end_of_line` — `.gitattributes` `text=auto` already normalizes on commit and both OSes keep native checkout endings); add `dotnet_diagnostic.CA1707.severity = none` scoped to `GEffectLogicTests/**` if test method names use underscores.
- [ ] Add `.yamllint` at repo root — 2-space indent, document-start optional (issue-template yml may lack `---`), relaxed line-length.
- [ ] Add `.markdownlint-cli2.jsonc` — relaxed rules for existing docs (disable `MD013` line-length, `MD033` inline HTML, plus `MD024`/`MD041` as needed by README/Logic.md/CLAUDE.md) and `ignores` for `.agents/**`.
- [ ] Add `.shellcheckrc` only if defaults flag the existing script — tune rather than disable checks wholesale.
- [ ] Add `cliff.toml` at repo root — Conventional Commit parser, tag pattern `v*`, groups: Breaking Changes, Features, Bug Fixes, Performance, Refactoring, Documentation, Tests, CI/Build, Miscellaneous; commit links to GitHub.
- [ ] Add `CHANGELOG.md` — header only; entries appended by CI per release.

### C. One-time baseline cleanup commit(s)
- [ ] Run `dotnet format` (whitespace + style + analyzers fix mode) over `GEffectsLogic.slnx`; build with `-warnaserror` and fix or tune remaining analyzer diagnostics in `.editorconfig` (prefer tuning severities over suppression files; allow a nested `.editorconfig` under `GraphicLogicTest/` if GUI-app rules need relaxing).
- [ ] Fix yamllint/markdownlint/shellcheck/PSScriptAnalyzer/prettier findings in existing files (expected mostly in `README.md`, `Logic.md`, `CLAUDE.md`, workflow yml).
- [ ] Commit as `style:`/`refactor:` (conforming message, exempt from issue-link via type) in a dedicated PR so the noise stays out of feature work.

### D. Workflows (official actions only)
- [ ] `git mv .github/workflows/dotnet.yml .github/workflows/build.yml`; set `name: Build`; update the Solution Items `<File Path>` in `GEffectsLogic.slnx`. Release logic (curl/jq draft→upload→publish) stays unchanged — already policy-compliant. Optionally add `test` to `release-artifacts`' `needs`.
- [ ] Add `.github/workflows/lint.yml` (on `push` + `pull_request`), three jobs:
  - `csharp`: `actions/checkout` → `actions/setup-dotnet@v4` (10.x) → `dotnet restore` → `dotnet format GEffectsLogic.slnx whitespace --verify-no-changes` → `dotnet format ... style --verify-no-changes --severity warn` → `dotnet format ... analyzers --verify-no-changes --severity warn` → `dotnet build -c Release --no-restore -warnaserror`. Fallback: run the `format` commands per-`csproj` if the pinned SDK's `dotnet format` cannot read `.slnx`.
  - `misc`: `actions/checkout` then sequential `run:` steps — `pip install yamllint==<pin>` + `yamllint -c .yamllint .` · `npx --yes markdownlint-cli2@<pin>` · `shellcheck scripts/*.sh .githooks/commit-msg` · `go install github.com/rhysd/actionlint/cmd/actionlint@<pin>` + `actionlint` · `pwsh -Command "Install-Module PSScriptAnalyzer -Force -RequiredVersion <pin>; Invoke-ScriptAnalyzer -Path scripts -Recurse"` · `npx --yes prettier@<pin> --check "**/*.json"`.
  - `license-headers`: `actions/checkout` → `run: bash scripts/enforce-license-headers.sh --check`.
- [ ] Add `.github/workflows/pr-checks.yml` (on `pull_request` incl. `edited`), three jobs, all `permissions: pull-requests: read`:
  - `title`: bash step matching `github.event.pull_request.title` against the shared regex; fails with a type list on mismatch.
  - `commits`: `actions/checkout` (`fetch-depth: 0`) + bash loop validating every subject in `origin/<base>..HEAD` against the same regex.
  - `pr-metadata`: `actions/github-script@v7` (official) — (a) extract type from PR title, require `#\d+` in the body unless the type is exempt or `no-issue` label set; (b) sum `changes` over `pulls.listFiles` excluding `CHANGELOG.md`, fail above ~800 lines unless `large-change` label set.
- [ ] Add `.github/workflows/changelog.yml` (on `pull_request`, `permissions: contents: write`):
  - `actions/checkout` with `ref: ${{ github.head_ref }}`, `fetch-depth: 0`, `fetch-tags: true`.
  - Bash step: exit early unless `<VersionPrefix>` differs from the base branch (release PR detection); exit early if `CHANGELOG.md` already contains `## v<version>` (idempotent).
  - Bash step: download the pinned git-cliff release binary via curl (version pinned in the file; bumping it is a deliberate change), run `git-cliff --unreleased --tag v<VersionPrefix> --prepend CHANGELOG.md`.
  - Bash step: commit as `github-actions[bot]` with `chore(release): update changelog for v<VersionPrefix>` and push to `HEAD:${{ github.head_ref }}`.

### E. Docs & repo settings
- [ ] Add `CONTRIBUTING.md` — clone setup (`install-git-hooks.ps1` / raw `git config` fallback), commit format with good/bad examples, one-issue-per-PR rule, size limit and `large-change`/`no-issue` escape labels, **lint section**: `dotnet format` (fix) vs `--verify-no-changes` (check), license headers auto-fixed by `scripts/enforce-license-headers.sh` (both IDEs also auto-insert via `file_header_template`), optional local runs of the CLI linters, release procedure (open `chore(release): vX.Y.Z` PR that only bumps `VersionPrefix`; CI appends the changelog commit automatically; merge publishes the release).
- [ ] Update `.github/pull_request_template.md` — move "Related Issues" to the top, note that issue links and title format are CI-enforced, add `no-issue`/`large-change` label hints, note the license header + format checks.
- [ ] Fix `CLAUDE.md` drift while touching docs: `GraphicLogicTest` is Avalonia (`.axaml`), not WPF; add `dotnet format`/lint commands to the Commands section.
- [ ] Apply repo settings via `gh api` (binary at `D:\programms\GHCLI\gh.exe`, requires user approval since it mutates repo config):
  - [x] `PATCH repos/AMPW-german/GEffectsLogic` — `allow_merge_commit=false`, `allow_rebase_merge=false`, `squash_merge_commit_title=PR_TITLE`, `squash_merge_commit_message=PR_BODY` (auto-delete branches already on).
  - [ ] After the new workflows have reported at least once, add `required_status_checks` for the check contexts (`Build / test`, `Lint / csharp`, `Lint / misc`, `Lint / license-headers`, `PR Checks / title`, `PR Checks / commits`, `PR Checks / pr-metadata`) to the existing `main` ruleset (`PUT rulesets/18064083` preserving existing rules).

## Files to Modify/Create

- `.githooks/commit-msg` — new, local commit validation hook (POSIX sh, Windows + macOS)
- `scripts/install-git-hooks.ps1` — new, one-time hook setup (pwsh on both OSes)
- `scripts/enforce-license-headers.sh` — edit, add `--check` mode + widen `SRC_DIR` to repo root; restore `+x` bit
- `Directory.Build.props` — new, repo-wide analyzer/style build settings
- `.editorconfig` — expand with style rules, severity entries (incl. IDE0073), per-file-type indent
- `.yamllint` — new, yamllint config
- `.markdownlint-cli2.jsonc` — new, markdownlint rules + ignores
- `.shellcheckrc` — new (only if needed)
- `cliff.toml` — new, git-cliff configuration
- `CHANGELOG.md` — new, header-only seed
- `CONTRIBUTING.md` — new, contributor/release/lint workflow doc
- `.github/pull_request_template.md` — edit, surface enforced requirements
- `CLAUDE.md` — edit, fix Avalonia/WPF drift + add lint commands
- `GEffectsLogic.slnx` — edit, Solution Items path `dotnet.yml` → `build.yml`
- `.github/workflows/dotnet.yml` → `.github/workflows/build.yml` — rename + `name: Build` only; release block untouched
- `.github/workflows/lint.yml` — new, `csharp` + `misc` (CLI linters) + `license-headers` jobs
- `.github/workflows/pr-checks.yml` — new, title/commits regex jobs + github-script metadata job
- `.github/workflows/changelog.yml` — new, pinned git-cliff binary + changelog commit on release PRs
- Repo settings via `gh api` (apply once via script)

## Verification

- [ ] `git commit` with a non-conforming message is rejected locally on **both** Windows (VS2026/Git bash) and macOS (Rider/terminal); conforming messages pass.
- [ ] `dotnet format GEffectsLogic.slnx --verify-no-changes`, `dotnet build -warnaserror`, and `bash scripts/enforce-license-headers.sh --check` are clean locally after the baseline commit.
- [ ] Test PR: `pr-checks` fails on bad title, bad commit subject, missing issue link, oversized diff; `lint` fails on a format violation, a `.cs` file without the GPL header, a bad markdown/yml file; all pass when compliant (and with `no-issue`/`large-change` labels).
- [ ] Test release PR: bump `VersionPrefix` on a branch, open PR — `changelog.yml` pushes a `chore(release)` commit prepending the correct `CHANGELOG.md` section; a second push does not duplicate it.
- [ ] Merge the release PR — `build.yml` creates the GitHub release (existing curl/jq flow, unchanged).
- [ ] `actionlint` passes on all four workflow files; run locally via the same pinned version as CI.

## Risks/Considerations

- `core.hooksPath` is per-clone and not committed by git itself — mitigated by the install script + CONTRIBUTING docs; CI check is the authoritative enforcement anyway (hooks can be bypassed with `--no-verify`).
- Commits pushed by `GITHUB_TOKEN` do not re-trigger workflows: the bot's changelog commit won't re-run `pr-checks`, so the check status stays from the last maintainer push — acceptable since the bot commit is generated and conforming by construction.
- `CHANGELOG.md` on fresh clones has no entries until the first release PR lands; the file exists with a header from day one.
- The existing ruleset already requires PRs with 0 approvals; restricting repo-level merge methods to squash is the remaining history-cleanliness lever.
- Required status checks can only be registered after the workflows have reported at least once — sequencing note, not a blocker (checks still run and report from the start).
- The ~800-line size threshold is a heuristic; tuned via the `large-change` escape label rather than disabling the check.
- Issue-link check relies on a body regex, not GitHub's linked-issue graph; documented as a convention check (use `Fixes #N`).
- **Cross-platform (VS2026 + Rider) requirement**: all enforcement is POSIX sh, pwsh, or `dotnet` CLI — no Windows-only tooling. `.gitattributes` `text=auto` keeps LF in-repo so `dotnet format whitespace` on Linux CI agrees with files written on either OS; `.editorconfig` intentionally leaves `end_of_line` unset for the same reason.
- **Full analyzers = real churn.** `AnalysisLevel=latest-all` + `EnforceCodeStyleInBuild` will surface many diagnostics (naming, CA1707 underscores in test names, docs rules, ConfigureAwait in the Avalonia app). The baseline commit tunes severities in `.editorconfig` for rules that fight project conventions rather than suppressing broadly; expect the largest diff in `GraphicLogicTest` — a nested `.editorconfig` there is the escape hatch.
- **`dotnet format` vs `.slnx`**: support for `.slnx` inputs should exist in the .NET 10 SDK but is a known edge — the per-`csproj` fallback covers it.
- **markdownlint on existing docs**: README/Logic.md/CLAUDE.md use `\`-style hard breaks and occasional HTML; the relaxed `.markdownlint-cli2.jsonc` exists precisely to avoid reformatting prose.
- **CLI tool pinning**: every tool installed in CI is pinned (`yamllint==`, `actionlint@v…`, `markdownlint-cli2@…`, `prettier@…`, `PSScriptAnalyzer -RequiredVersion`, git-cliff release tag) for reproducibility and supply-chain hygiene; bumps are deliberate edits.
- **Header-check duplication**: `--check` script and IDE0073 overlap on `.cs` — intentional; the script additionally gives a clean per-file report and can cover non-`.cs` types later via its `COMMENT` variable.
- **No line length limit**: no check enforces a maximum line length anywhere (`MD013` off; no C# line-length rule) — linting covers formatting consistency, style, analyzers, and headers only.

## Implementation status (2026-09-30)

- Done: hooks and shared validator, non-mutating repo-wide license check and normalized headers, full SDK analyzers with per-project baseline exceptions, repo-wide CLI lint configuration, `build.yml` rename, three new workflows using only maintained official GitHub actions, changelog config, contributor docs, PR template, and `.gitattributes` LF rules for scripts/workflows.
- Verified on Windows: strict Release build; all three `dotnet format` checks; 32 non-experimental/non-performance tests; license check; yamllint, markdownlint, prettier, PSScriptAnalyzer, and actionlint with shellcheck for workflow scripts. ShellCheck against the local license script's current CRLF working copy is not representative of the LF Git index; validate after a fresh checkout or on Linux CI. git-cliff config loads and renders in a local dry run.
- Repository merge settings updated with explicit approval: squash-only merges, `PR_TITLE`/`PR_BODY` for the squash commit; no ruleset changes. Pending: macOS Rider validation and CI PR/release end-to-end runs. Required status checks cannot be configured until workflows report. Do not push or alter repo rules without the user's explicit approval.
- Deviations: `LangVersion` remains in the existing core csproj (no unnecessary deduplication); scoped analyzer exceptions live in per-project `.editorconfig` files; XML is checked using Python's standard XML parser; the existing release workflow now waits for tests before publishing; baseline cleanup is in the working tree, not a separate commit/PR.
