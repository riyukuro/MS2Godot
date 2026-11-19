#!/bin/bash
# tools/perfgate/check-trailer.sh RANGE: every commit in RANGE (git rev-list syntax, e.g. origin/main..HEAD) that changes
# tools/perfgate/baseline.json must carry a "Perf-baseline: <reason>" trailer with a reason. Prints each such commit and
# its reason (for the reviewer to accept or not); exits 1 when one lacks it.
set -u
cd "$(dirname "$0")/../.." || exit 2
bad=0
for c in $(git rev-list "$1" -- tools/perfgate/baseline.json); do
  reason=$(git log -1 --format=%B "$c" | git interpret-trailers --parse | sed -n 's/^Perf-baseline: *//p' | head -1)
  if [ -z "$reason" ]; then echo "perfgate: $(git log -1 --format='%h %s' "$c"): changes the baseline without a Perf-baseline: trailer"; bad=1
  else echo "perfgate: $(git log -1 --format='%h' "$c") updates the baseline: $reason"; fi
done
exit $bad
