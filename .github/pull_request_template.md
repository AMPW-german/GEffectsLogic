## Related Issues

<!-- Link related issues: Fixes #123, Relates to #456 -->

Link an issue for non-trivial changes. `docs`, `style`, `build`, `ci`, `chore`, and `revert` PRs are exempt; otherwise use the `no-issue` label if there is no issue.

## Description

<!-- Describe your changes and the motivation behind them. -->

Use a Conventional Commit PR title (`type(scope): lowercase subject`). PRs over 800 changed lines (excluding generated `CHANGELOG.md`) need the `large-change` label and an explanation.

## Type of Change

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)
- [ ] Refactoring (no functional changes)
- [ ] Documentation update
- [ ] Other (please describe):

## Testing

- [ ] I have tested all changes locally and they work as expected
- [ ] I have added/updated tests that cover my changes
- [ ] Existing tests pass with my changes (`dotnet test`)

## Checklist

- [ ] My code follows the project's coding style and passes `dotnet format --verify-no-changes`
- [ ] New C# files have the GPL header (`scripts/enforce-license-headers.sh --check`)
- [ ] I have performed a self-review of my code
- [ ] I have commented my code where necessary (non-obvious logic only)
- [ ] My changes generate no new warnings

## CLA Agreement

> By submitting this pull request, you must confirm that you have read and agree to the project's [Contributor License Agreement (CLA)](../CLA.md).

- [ ] I have read, understood, and agree to the [Contributor License Agreement (CLA)](../CLA.md).

**Name / GitHub Username:** <!-- Write your full name or GitHub username here -->
