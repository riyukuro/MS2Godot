#!/bin/bash
# Refuses translated UI code: it is made on the player's machine from their own Gfx data and is never committed.
# Fails on any path under a generated/ folder, any *.ms2gen.cs file, and any file whose first line is the
# translator's "// <ms2-generated>" marker.
#   check-generated.sh            checks the staged files (pre-commit)
#   check-generated.sh --tracked  checks every tracked file (CI)
if [ "$1" = "--tracked" ]; then list() { git ls-files -z; }; show() { head -n 1 -- "$1" 2>/dev/null; }
else list() { git diff --cached --name-only --diff-filter=ACMR -z; }; show() { git show ":$1" 2>/dev/null | head -n 1; }; fi
bad=0
list | {
  while IFS= read -r -d '' f; do
    case "$f" in
      generated/*|*/generated/*|*.ms2gen.cs) echo "generated UI code must not be committed: $f"; bad=1; continue ;;
    esac
    case "$f" in *.cs) if show "$f" | grep -q '^// <ms2-generated>'; then echo "generated UI code must not be committed: $f"; bad=1; fi ;; esac
  done
  exit $bad
}
