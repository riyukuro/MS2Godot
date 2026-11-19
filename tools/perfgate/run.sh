#!/bin/bash
# tools/perfgate/run.sh [--full] SCRATCH [OUT.json]: the perf regression gate's run, headless (CPU side only).
#   1. a fresh install: setup.tscn into SCRATCH/xdg (the UI code and caches), stopped once the flow scene starts;
#   2. sessions of the offline field + HUD (hud.tscn, MS2_PERF_GATE): the first as "install", then one "session"
#      (--full: two, as the baseline runs use);
#   3. tools/PerfGate summarize: one JSON (OUT, default SCRATCH/run.json) of them.
# Needs MS2_DATA and MS2_NIFXML; GODOT (default godot-mono). SCRATCH must not exist or be empty; it is left for the caller.
# Compare with: dotnet run --project tools/PerfGate -- compare tools/perfgate/baseline.json OUT.json
set -u
n=2; [ "${1-}" = --full ] && { n=3; shift; }
scratch=$1; out=${2-$1/run.json}
root=$(cd "$(dirname "$0")/../.." && pwd)
godot=${GODOT-godot-mono}
[ -n "${MS2_DATA-}" ] && [ -n "${MS2_NIFXML-}" ] || { echo "perfgate: set MS2_DATA and MS2_NIFXML" >&2; exit 2; }
mkdir -p "$scratch"
[ -z "$(ls -A "$scratch")" ] || { echo "perfgate: $scratch is not empty" >&2; exit 2; }
export XDG_DATA_HOME=$scratch/xdg DOTNET_ROLL_FORWARD=Major
t0=$SECONDS
(cd "$root/game" && dotnet build -nologo -v q > "$scratch/build.log" 2>&1) || { echo "perfgate: game build failed ($scratch/build.log)" >&2; exit 2; }
dotnet build -nologo -v q "$root/tools/PerfGate" > "$scratch/build-tool.log" 2>&1 || { echo "perfgate: tool build failed" >&2; exit 2; }
cd "$root/game"
# setup: stops at its first "flow:" line (the login scene), as a player's first launch would go on to log in
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
