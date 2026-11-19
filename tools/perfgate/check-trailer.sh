#!/bin/bash
# tools/perfgate/check-trailer.sh RANGE: every commit in RANGE (git rev-list syntax, e.g. origin/main..HEAD) that changes
# tools/perfgate/baseline.json must carry a "Perf-baseline: <reason>" trailer with a reason (the key matches in any case).
# Prints each such commit and its reason (for the reviewer to accept or not). Exits 1 when one lacks it, 2 on a missing
# or bad RANGE.
set -u
[ $# = 1 ] || { echo "usage: tools/perfgate/check-trailer.sh RANGE" >&2; exit 2; }
cd "$(dirname "$0")/../.." || exit 2
revs=$(git rev-list "$1" -- tools/perfgate/baseline.json) || { echo "perfgate: bad range: $1" >&2; exit 2; }
bad=0
for c in $revs; do
  reason=$(git log -1 --format='%(trailers:key=Perf-baseline,valueonly)' "$c" | sed '/^[[:space:]]*$/d' | head -1)
  if [ -z "$reason" ]; then echo "perfgate: $(git log -1 --format='%h %s' "$c"): changes the baseline without a Perf-baseline: trailer"; bad=1
  else echo "perfgate: $(git log -1 --format='%h' "$c") updates the baseline: $reason"; fi
done
exit $bad
