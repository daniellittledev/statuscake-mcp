#!/usr/bin/env bash
# Fails if any place that names the release version disagrees with the .fsproj.
# The skill and README pin `StatusCakeMcp@<version>`, so drift would make them run a
# version that doesn't exist (or isn't the one just released).
#
# Usage: scripts/check-versions.sh [expected-version]   # e.g. the tag, minus the "v"
set -euo pipefail
cd "$(dirname "$0")/.."

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/StatusCakeMcp/StatusCakeMcp.fsproj)
expected=${1:-$version}
status=0

fail() { echo "::error::$1"; status=1; }

[ "$version" = "$expected" ] ||
  fail "StatusCakeMcp.fsproj is $version but expected $expected (tag)."

for json in plugin/.claude-plugin/plugin.json mcpb/manifest.json; do
  found=$(sed -n 's/^  "version": "\(.*\)",$/\1/p' "$json")
  [ "$found" = "$expected" ] || fail "$json version is '$found', expected $expected."
done

# Every pinned StatusCakeMcp@x.y.z in the skill and README must match.
while IFS= read -r hit; do
  fail "Pinned version mismatch (expected $expected): $hit"
done < <(grep -rnoE 'StatusCakeMcp@[0-9]+\.[0-9]+\.[0-9]+[^ `"]*' plugin README.md |
         grep -v "StatusCakeMcp@$expected\$" || true)

[ "$status" -eq 0 ] && echo "All versions are $expected."
exit "$status"
