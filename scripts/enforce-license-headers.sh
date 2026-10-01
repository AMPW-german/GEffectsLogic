#!/usr/bin/env bash
#
# enforce-license-headers.sh
#
# Cross-platform (POSIX bash + awk) script that ENFORCES the GPL v3 license
# header on all source files under SRC_DIR.  For each file it:
#    1. strips a leading UTF-8 BOM, and
#    2. prepends or refreshes the canonical GPL v3 header so the copyright line
#       reflects the configured year range and author list.
#
# Files are skipped for header insertion (but still BOM-stripped) when they
# already carry an MIT or Apache-2.0 header, or contain a
# "// no-license-header" opt-out comment.
#
# Configuration (override via environment variables):
#   AUTHOR     comma-separated author list              (default: "AMPW")
#   PROJECT    header title / project name              (default: "GEffectsLogic")
#   YEAR       copyright year, or a "START-END" range   (default: "2026")
#   SRC_DIR    directory to scan recursively            (default: <repo>)
#   COMMENT    line comment prefix                      (default: "//")
#   DRY_RUN    set to "1" to report changes without writing
#
# Usage:
#    ./scripts/enforce-license-headers.sh
#   AUTHOR="Jane Doe, John Smith" YEAR="2020-2026" ./scripts/enforce-license-headers.sh
#   DRY_RUN=1 ./scripts/enforce-license-headers.sh
#
# Exit 0 on success (even when fixing files); --check exits 1 for invalid headers.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# --- configurable via environment ------------------------------------------------
AUTHOR="${AUTHOR:-AMPW}"
PROJECT="${PROJECT:-GEffectsLogic}"
YEAR="${YEAR:-2026}"
SRC_DIR="${SRC_DIR:-${REPO_ROOT}}"
COMMENT="${COMMENT:-//}"
DRY_RUN="${DRY_RUN:-0}"
CHECK=0
if [ "${1:-}" = "--check" ]; then
    CHECK=1
    DRY_RUN=1
elif [ "$#" -ne 0 ]; then
    echo "Usage: $0 [--check]" >&2
    exit 2
fi
FAILED=0
# --- end config ------------------------------------------------------------------

# Normalize the year field.  A bare "2026" stays as-is; a "2020-2026" range is
# passed through unchanged.  (No expansion needed -- the value is literal text.)
COPYRIGHT_YEARS="${YEAR}"

# The canonical header template.  The style mirrors the existing project header so
# refreshed files stay byte-identical in formatting.  Built as an array of lines
# and written to a temp file for awk to read (BSD awk cannot carry a multi-line
# string through -v).
HEADER_LINES=(
"${COMMENT} ${PROJECT}"
"${COMMENT} Copyright (C) ${COPYRIGHT_YEARS} ${AUTHOR}"
"${COMMENT}"
"${COMMENT} This program is free software: you can redistribute it and/or modify"
"${COMMENT} it under the terms of the GNU General Public License as published by"
"${COMMENT} the Free Software Foundation, either version 3 of the License, or"
"${COMMENT} (at your option) any later version."
"${COMMENT}"
"${COMMENT} This program is distributed in the hope that it will be useful,"
"${COMMENT} but WITHOUT ANY WARRANTY, without even the implied warranty of"
"${COMMENT} MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the"
"${COMMENT} GNU General Public License for more details."
"${COMMENT}"
"${COMMENT} You should have received a copy of the GNU General Public License"
"${COMMENT} along with this program.  If not, see <http://www.gnu.org/licenses/>."
)

# Locate source files.  Exclude compiled output.
CS_FILES="$(find "$SRC_DIR" -type f -name "*.cs" \
     -not -path "*/bin/*" \
     -not -path "*/obj/*" \
     -not -path "*/.git/*" \
     -not -path "*/.vs/*" \
     | sort)"

if [ -z "$CS_FILES" ]; then
    echo "No .cs source files found under $SRC_DIR"
    exit 0
fi

# Temp file holding the canonical header (one line per element).
HEADER_FILE="$(mktemp)"
trap 'rm -f "$HEADER_FILE"' EXIT
printf '%s\n' "${HEADER_LINES[@]}" > "$HEADER_FILE"

# --- 1. strip a leading UTF-8 BOM ----------------------------------------------
# Runs in fix/dry-run modes; only rewrites when a BOM is present in fix mode.
strip_bom() {
    local file="$1"
    if [ "$(head -c 3 "$file" | od -An -tx1 | tr -d ' \n')" != "efbbbf" ]; then
        return
    fi
    local tmp; tmp="$(mktemp)"
    LC_ALL=C awk 'NR==1{sub(/\357\273\277/,"")} 1' "$file" > "$tmp"
    if ! cmp -s "$tmp" "$file"; then
        if [ "$DRY_RUN" = "1" ]; then
            rm -f "$tmp"
            [ "$CHECK" = "1" ] || echo "  WOULD STRIP BOM: ${file#"$SRC_DIR"/}"
        else
            mv "$tmp" "$file"
            echo "  BOM stripped: ${file#"$SRC_DIR"/}"
        fi
    else
        rm -f "$tmp"
    fi
}

# --- 2. insert / refresh the GPL header ----------------------------------------
fix_header() {
    local file="$1"
    local rel_path="${file#"$SRC_DIR"/}"

     # Skip header work for opt-outs and foreign licenses.
    if grep -qi "// no-license-header" "$file"; then
        echo "  SKIP (no-license-header): $rel_path"
        return
    fi
    local first_lines
    first_lines="$(head -5 "$file")"
     # Require a genuine MIT/Apache marker (word-boundary "MIT"/"Apache License")
     # so names like "John Smith" are not mistaken for a license.
    if printf '%s\n' "$first_lines" | grep -qiE '^//.* Copyright \(C\)[^M]*MIT License'; then
        echo "  SKIP (MIT header): $rel_path"
        return
    fi
    if printf '%s\n' "$first_lines" | grep -qiE '^//.* Licensed under the Apache License'; then
        echo "  SKIP (Apache header): $rel_path"
        return
    fi

    if [ "$CHECK" = "1" ]; then
        if ! awk -v N="${#HEADER_LINES[@]}" 'NR <= N { sub(/\r$/, ""); if (NR == 1) sub(/^\357\273\277/, ""); print }' "$file" | cmp -s "$HEADER_FILE" -; then
            echo "  MISSING OR INVALID HEADER: $rel_path"
            FAILED=$((FAILED + 1))
        fi
        return
    fi

     # The awk program:
     #   * reads the canonical header from HEADER_FILE,
     #   * if the first line is our project/copyright line, consumes the old
     #     header block (through the "along with this program" terminator plus
     #     one trailing blank separator) and emits the fresh header there,
     #   * otherwise prepends the fresh header to the file.
    local tmp; tmp="$(mktemp)"
    awk -v HF="$HEADER_FILE" -v P="$PROJECT" -v C="$COMMENT" '
    BEGIN {
        n = 0
        while ((getline line < HF) > 0) { n++; hdr[n] = line }
        close(HF)
    }
    { sub(/\r$/, "") }
    NR == 1 {
        # Match our header by prefix using index() to avoid regex escaping.
        has = (index($0, C " " P) == 1) || (index($0, C " Copyright (C) ") == 1)
        for (i = 1; i <= n; i++) print hdr[i]
        if (!has) {
            print ""
            print $0
            next
        }
        mode = "skip_hdr"
        next
    }
    mode == "skip_hdr" {
        if (index($0, "along with this program") > 0) mode = "skip_blank"
        next
    }
    mode == "skip_blank" {
         # Re-emit exactly one blank separator so the output matches the
         # canonical "header + blank + code" form produced by the prepend path.
        if ($0 != "") print $0
        print ""
        mode = ""
        next
     }
    { print }
    ' "$file" > "$tmp"

    if ! awk '{ sub(/\r$/, ""); print }' "$file" | cmp -s "$tmp" -; then
        if [ "$DRY_RUN" = "1" ]; then
            echo "  WOULD CHANGE: $rel_path"
            rm -f "$tmp"
        else
            mv "$tmp" "$file"
            echo "  FIXED: $rel_path"
        fi
    else
        rm -f "$tmp"
    fi
}

echo "Enforcing license headers under $SRC_DIR   (project='$PROJECT', years='$COPYRIGHT_YEARS', author='$AUTHOR')"
if [ "$DRY_RUN" = "1" ]; then
    echo "(dry run -- no files will be modified)"
fi

while IFS= read -r file; do
    if [ "$CHECK" != "1" ]; then
        strip_bom "$file"
    fi
    fix_header "$file"
done <<< "$CS_FILES"

echo ""
if [ "$FAILED" -ne 0 ]; then
    echo "$FAILED file(s) have invalid license headers." >&2
    exit 1
fi
echo "Done."
exit 0
