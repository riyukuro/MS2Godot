# Perf regression gate

One command measures the game's CPU-side cost, headless. A second compares the result with the stored baseline and exits 1 on a regression.

```sh
export MS2_DATA="<the raw KR client>/Data" MS2_NIFXML="$PWD/external/nifxml/nif.xml"
tools/perfgate/run.sh SCRATCH            # the merge gate's run: setup + 2 sessions
tools/perfgate/run.sh --full SCRATCH     # setup + 3 sessions, as the baseline runs use
dotnet run --project tools/PerfGate -- compare tools/perfgate/baseline.json SCRATCH/run.json
```

The inputs are the merge gate's, the same as every suite's: `MS2_DATA` is the raw KR client's `Data` folder (not a restored one), and `MS2_NIFXML` is the `external/nifxml` submodule's `nif.xml`. Other inputs give other counts: a restored Data folder and another nif.xml moved 13 metrics, for example worldmap's kept objects from 1,978 to 966. So each session records the inputs' fingerprint, and so does the baseline. The fingerprint covers the `nif.xml` and `Xml.m2d` hashes, plus one hash over the pack indexes (every `.m2h` under `Data`, by path and content, in C-locale order). `run.sh` checks it against the baseline's before it builds anything and exits 2 within a second when they differ ("inputs differ, rebuild the baseline"); `compare` checks it again and exits 2 the same way. `PERFGATE_BASELINE` names the baseline `run.sh` checks against (default `tools/perfgate/baseline.json`); the runs a new baseline is built from set it empty, which skips the check.

`SCRATCH` must be empty or must not exist. The run's temporary files go there too (`SCRATCH/tmp`, as `TMPDIR`). Run the gate in a heavy slot: it builds the game, then runs Godot three times (four with `--full`). It only records times (`PERFGATE_TIMES=1` lists them against the baseline).

Exit codes, for `run.sh` and every `PerfGate` command:

| Code | Meaning |
|---|---|
| 0 | pass |
| 1 | a regression (`compare` only): a count metric past its limit, or missing from the run |
| 2 | a gate error: a bad or missing file, a session that failed or wrote no result, inputs that differ from the baseline's, or a baseline entry without a limit |

## What a run measures

`run.sh` does a fresh install (`setup.tscn` into `SCRATCH/xdg`). Then it runs three sessions of the offline field with the HUD (`hud.tscn` with `MS2_PERF_GATE`; `game/ui/HudScene.PerfGate.cs`). The first session is `install` (the first launch after setup). The next is `session` (a later launch); `--full` runs two of those. Each session runs with `--fixed-fps 60`, so it follows the same frame schedule whatever the frames' wall time:

- `field.<case>.*`: the field load, from process start to the first HUD frame. It covers wall time, bytes allocated on all threads, collections, engine objects and nodes, and JIT-compiled methods.
- `idle.<case>.*`: 300 frames with no dialog open. `idle_3_open.<case>.*`: 300 frames with inventory, myinfo and skill open. Each records:
  - bytes per frame on the main thread and on all threads, and collections;
  - the field effects' bytes per effect frame (`EffectInstance.Count`) and their µs per effect frame;
  - the median and worst frame time.
- `idle_live.<case>.*` and `move.<case>.*`: the same 300 frames with the live HUD parts running: the nearest 4 field NPCs shown with HP bars and name tags, a damage number on an NPC every 20 frames and on the player every 45, a chat line every 30 frames from 4 fixed lines (`GateChurn`). `move` also walks the player (`ScriptDriven`) round a fixed square, one side per 60 frames. Same records as the idle windows.
- GC wrappers, in every idle, live, move and open window: `wrappers` (Godot wrappers the DisposablesTracker holds at the window's end minus at its start: created, not disposed, not yet finalized), `finalized_godot` and `finalized_other` (what the window's closing full collection removes from the tracker: GodotObjects, and Variant disposers, StringNames, NodePaths, Arrays, Dictionaries), and per frame for the 300-frame windows. A leak of Variant disposers or Dictionaries per call (a string key into a Godot Dictionary, a `Variant` made per read) shows here and not in bytes. `wrapper_types` in a window's JSON (not checked) names the types that grew. `MS2_PERF_GATE_ATTRIB=1` adds `attrib`, wrappers per C# node's process/physics call (a diagnostic; it adds nodes, so not for a baseline run). `gen0`/`gen1`/`gen2` are checked in every window, the live and move windows' included.
- `open.<dialog>.<install|session|repeat>.*`: covers inventory, myinfo, skill, quest, worldmap, shop and options. Each dialog gets its first open of the session (`install` or `session`), then 3 repeat opens. Each open records:
  - bytes, collections, JIT-compiled methods and time, from its toggle to the end of the third frame after it;
  - the engine objects and nodes it added;
  - `kept_objects`, the objects still alive 35 frames after its close.
- `opens.<install|session|repeat>.sum_ms`: every dialog's open time, summed per session.

Every window starts 15 frames after a full collection, and its object counts are read after one. So a window's collections come from its own allocation, its object counts are live objects, and finalizer frees from earlier garbage don't land in it. Two once-per-session costs are moved out of every window, in the gate session only:

- The HUD clock is fixed, so its once-a-minute redraw never lands in one.
- GodotSharp's wrapper tracker (`DisposablesTracker`, a `ConcurrentDictionary`) is grown past twice its size after the field load. Its one growth in a session rebuilds every entry, about 3.3 MB at the field's ~60,000 wrappers. Before this, that rebuild landed in one of the first 8 opens, whichever crossed the growth point. If the pre-grow fails (GodotSharp no longer has the tracker), the session quits without a result and `run.sh` exits 2.

`run.sh` also pins the gen0 budget (`DOTNET_GCgen0size=0x3000000`, 48 MB). The runtime otherwise derives it from the CPU's cache size, so the collection counts would depend on the machine. 48 MB is the runtime's default cap (`GCGen0MaxBudget`), the value a large-cache CPU gets anyway.

## What is checked

`compare` checks the **counts**: main-thread bytes, objects, nodes, kept objects, collections, and the methods the main thread JIT-compiled. It holds each at its worst value.

It records the following without checking them (`PERFGATE_TIMES=1` prints them against the baseline):

- **Times**: the machine's load moves them several times over.
- **JIT methods on every thread**: the tiering thread's recompiles land in a window or not. myinfo repeats saw 30 to 157.
- **Bytes on every thread** in an open or idle window: the install session's background cache build lands in whichever window it overlaps. Inventory's install-session first open saw 36 to 371 MB.
- **Collections in the install session's windows**, for the same reason: they are process-wide.

The field load keeps its all-thread bytes and collections checked. They are the only counts it has, and their spread is in the baseline.

## Limits (`PerfGate baseline`)

The baseline is built from several runs (`PerfGate baseline OUT RUN...`). A count's limit is the baseline max plus the spread the baseline runs showed plus a floor, so no limit is closer to the max than its floor. The floors are:

| Kind | Floor |
|---|---|
| bytes | 2% or 16 KB |
| bytes per frame (idle windows) | 5% or 8 KB |
| bytes per effect frame | 5% or 8 B |
| objects, nodes | 3% or 16 |
| kept objects: first opens, and each dialog's first repeat (`open.<dialog>.repeat1.kept_objects`) | 3% or 64 |
| kept objects: the later repeats (`open.<dialog>.repeat.kept_objects`) | 3% or 16 |
| JIT methods (main thread) | 2% or 8 |
| collections | 2 |

The noise behind the floors comes from the baseline's 10 runs (26 sessions) of the same tree on a loaded machine:

- **Main-thread bytes:** every dialog's first open stays within 0.4% of its median, and its repeats within 1.4%. No open was more than 300 KB above its dialog's median. The 3.3 MB tracker rebuild is the size of a regression the limits catch: the earlier runs, made before the pre-grow, fail on exactly that open. The idle windows' bytes per frame move the most, by up to 7% (idle with three dialogs open: 153 to 167 KB a frame).
- **JIT methods (main thread):** repeats compile 4 to 16 methods per open. First opens move by up to 14 (inventory 169 to 183), and the field load by up to 44 of 10,070.
- **Objects and nodes:** first opens are the same in every run. Repeats stay within ±6.
- **Kept objects:** first opens and first repeats are exact in every run. The later repeats move by up to 8, except shop's (−14 to 45). Skill's first repeat keeps 830 objects: that is a field NPC's emoticon balloon, which a gate session times from a fixed seed (`NpcEmoticons`), so it falls in that window every run. With the tick-count seed it fell in a random window (options' repeats moved by up to 129). Leaks the size of worldmap's first open (1,978 kept) fail either way.
- **Collections:** most are exact. The checked ones move by up to 3, and the field load by up to 8.

Leave-one-out over those 10 runs (a baseline from 9, compare the 10th) passes 10 of 10. The tightest is one install session's three-dialog idle window: 167 KB a frame, 5.4 KB (3.2%) under the other nine's limit, two thirds of its 8 KB floor. With the floor inside the spread instead of on top of it, and the idle windows' floor at 2% or 1 KB, it passed 9 of 10. Build a baseline from 10 runs, not fewer.

**Runtime:** about 120 s for `run.sh` and about 170 to 190 s for `--full` (setup about 30 s, then about 45 s per session). That excludes the build and any wait for a heavy slot.

## Changing the baseline

`tools/perfgate/baseline.json` changes only in a commit that carries a `Perf-baseline: <reason>` trailer, and the reviewer has to accept the reason. `tools/perfgate/check-trailer.sh RANGE` lists every commit in RANGE that touches the baseline, with its reason, and exits 1 if one has no reason. To make a new baseline, run the gate with `PERFGATE_BASELINE=` (empty) on the new tip several times (the current one has 10 runs; with fewer, the idle windows' spread is short) and build the baseline from those runs:

```sh
dotnet run --project tools/PerfGate -- baseline tools/perfgate/baseline.json R1/run.json R2/run.json R3/run.json
```

Use `--full` runs, or mix them with plain runs: either way every metric gets values from both cases.
