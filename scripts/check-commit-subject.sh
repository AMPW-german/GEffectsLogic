#!/usr/bin/env bash
set -euo pipefail

pattern='^(feat|fix|perf|refactor|test|docs|style|build|ci|chore|revert)(\([a-z0-9/_-]+\))?!?: [a-z]($|.*[^.]$)'
subject="${1:-}"
if ! printf '%s\n' "$subject" | grep -Eq "$pattern"; then
    printf 'Invalid Conventional Commit subject: %s\nExpected type(scope)!: lowercase subject without a trailing period.\n' "$subject" >&2
    exit 1
fi
