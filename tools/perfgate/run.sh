#!/bin/bash
# tools/perfgate/run.sh [--full] SCRATCH [OUT.json]: the perf regression gate's run, headless (CPU side only).
#   1. a fresh install: setup.tscn into SCRATCH/xdg (the UI code and caches), stopped once the flow scene starts;
#   2. sessions of the offline field + HUD (hud.tscn, MS2_PERF_GATE): the first as "install", then one "session"
#      (--full: two, as the baseline runs use);
#   3. tools/PerfGate summarize: one JSON (OUT, default SCRATCH/run.json) of them.
# Needs MS2_DATA and MS2_NIFXML (the merge gate's: the raw KR Data and the external/nifxml submodule's nif.xml; the
# baseline holds the fingerprint of the inputs it was made with, see fingerprint below); GODOT (default godot-mono).
# SCRATCH must not exist or be empty; it is left for the caller, and holds the run's temporary files too.
# Compare with: dotnet run --project tools/PerfGate -- compare tools/perfgate/baseline.json OUT.json
set -u
n=2; [ "${1-}" = --full ] && { n=3; shift; }
[ $# -ge 1 ] || { echo "usage: tools/perfgate/run.sh [--full] SCRATCH [OUT.json]" >&2; exit 2; }
scratch=$(realpath -m "$1"); out=$(realpath -m "${2-$scratch/run.json}")
root=$(cd "$(dirname "$0")/../.." && pwd)
godot=${GODOT-godot-mono}
[ -n "${MS2_DATA-}" ] && [ -n "${MS2_NIFXML-}" ] || { echo "perfgate: set MS2_DATA and MS2_NIFXML" >&2; exit 2; }
mkdir -p "$scratch"
[ -z "$(ls -A "$scratch")" ] || { echo "perfgate: $scratch is not empty" >&2; exit 2; }
mkdir -p "$scratch/tmp"
# DOTNET_GCgen0size: the gen0 budget, which the runtime otherwise derives from the CPU's cache size, so the collection
# counts would differ between machines. 48 MB is the runtime's default cap (GCGen0MaxBudget), the value a large-cache CPU gets.
export XDG_DATA_HOME=$scratch/xdg TMPDIR=$scratch/tmp DOTNET_ROLL_FORWARD=Major DOTNET_GCgen0size=0x3000000
# The inputs' fingerprint, written into every session's result: nif.xml, Xml.m2d, and the pack indexes (every .m2h
# under the data folder, by path and content, in C-locale order). compare exits 2 when it is not the baseline's.
fp() { sha256sum < "$1" | cut -c1-16; }
[ -f "$MS2_NIFXML" ] && [ -f "$MS2_DATA/Xml.m2d" ] || { echo "perfgate: no $MS2_NIFXML or $MS2_DATA/Xml.m2d" >&2; exit 2; }
idx=$(cd "$MS2_DATA" && find . -name '*.m2h' -type f | LC_ALL=C sort | while IFS= read -r f; do printf '%s %s\n' "$f" "$(sha256sum < "$f" | cut -c1-64)"; done | sha256sum | cut -c1-16)
export MS2_PERF_GATE_INPUTS="{\"nif.xml\":\"$(fp "$MS2_NIFXML")\",\"Xml.m2d\":\"$(fp "$MS2_DATA/Xml.m2d")\",\"pack_index\":\"$idx\"}"
echo "perfgate: inputs $MS2_PERF_GATE_INPUTS"
# PERFGATE_BASELINE (default the committed baseline; empty: no check, for the runs a new baseline is built from): a run
# on other inputs than the baseline's could only end in compare's exit 2, so it stops here, before the build
baseline=${PERFGATE_BASELINE-$root/tools/perfgate/baseline.json}
if [ -n "$baseline" ]; then
  python3 -c 'import json,sys; b=json.load(open(sys.argv[1])).get("inputs"); r=json.loads(sys.argv[2]); print("perfgate: baseline inputs", json.dumps(b, separators=(",", ":"))); sys.exit(0 if b == r else 1)' "$baseline" "$MS2_PERF_GATE_INPUTS" \
    || { echo "perfgate: inputs differ from $baseline's, rebuild the baseline (or set MS2_DATA and MS2_NIFXML to the merge gate's)" >&2; exit 2; }
fi
t0=$SECONDS
(cd "$root/game" && dotnet build -nologo -v q > "$scratch/build.log" 2>&1) || { echo "perfgate: game build failed ($scratch/build.log)" >&2; exit 2; }
dotnet build -nologo -v q "$root/tools/PerfGate" > "$scratch/build-tool.log" 2>&1 || { echo "perfgate: tool build failed" >&2; exit 2; }
cd "$root/game"
# setup: stops at its first "flow:" line (the launcher), as a player's first launch would go on to log in
timeout 600 "$godot" --headless --audio-driver Dummy --path . res://setup.tscn > "$scratch/setup.log" 2>&1 & p=$!
for _ in $(seq 600); do sleep 1; grep -q "^flow:" "$scratch/setup.log" && break; kill -0 $p 2>/dev/null || break; done
kill $p 2>/dev/null; wait $p 2>/dev/null
grep -q "^flow:" "$scratch/setup.log" || { echo "perfgate: setup did not finish ($scratch/setup.log)" >&2; exit 2; }
echo "perfgate: setup $((SECONDS - t0)) s"
files=()
for i in $(seq $n); do
  c=session; [ $i = 1 ] && c=install
  MS2_PERF_GATE=$scratch/s$i.json MS2_PERF_GATE_CASE=$c timeout 300 "$godot" --headless --audio-driver Dummy --fixed-fps 60 \
    --path . res://ui/hud.tscn > "$scratch/s$i.log" 2>&1
  [ -s "$scratch/s$i.json" ] || { echo "perfgate: session $i wrote no result ($scratch/s$i.log)" >&2; exit 2; }
  files+=("$scratch/s$i.json")
  echo "perfgate: session $i ($c) done, $((SECONDS - t0)) s"
done
dotnet "$root/tools/PerfGate/bin/Debug/net8.0/PerfGate.dll" summarize "$out" "${files[@]}" || exit 2
echo "perfgate: $out, $((SECONDS - t0)) s"
