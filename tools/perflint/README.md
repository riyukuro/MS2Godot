# perf-lint

Lint for Godot C# interop costs. The gate form reads only the lines a branch added (`git diff -U0 <base>...HEAD`, three-dot: since
the merge base; C# under `src/` and `game/`, not tests), so existing code is never flagged. It prints `file:line: rule -> fix`.
Exit 0 clean, 1 on a hit, 2 on an internal error (a crash is never a finding and never clean).

    python3 -I tools/perflint/perf-lint.py origin/main          # the branch against its base, run from the repo root
    python3 -I tools/perflint/perf-lint.py HEAD --tree          # a trial: merge base against the working tree, untracked files included
    python3 -I tools/perflint/perf-lint.py origin/main --head <rev>   # replay another tip
    python3 -I tools/perflint/perf-lint.py --all > backlog.tsv  # report only: every line of src/ and game/

`--all` prints a TSV (file, line, rule, enclosing method, per_frame), per-frame rows first, and exits 0. A method counts as
per-frame when it is `_Process`/`_PhysicsProcess`/`_Draw`/`_Input`/`_UnhandledInput`/`Tick`/`Advance` (or a name ending in
Tick/Advance), or is called by bare name from one in the same file; calls across files are not followed.

Allow a hit with a same-line `// perf-ok: <reason>` (why the cost is fine there: a probe, a once-per-load path).

## Why (measured, Tria field walk, GodotSharp 4.7)
Every Godot object, `StringName`, `NodePath`, `Variant`, non-generic `Godot.Collections.Array/Dictionary` the C# side touches gets a
wrapper that `Godot.DisposablesTracker` holds until a GC finalizes it. When its dictionaries grow they rebuild every entry in one
frame (an 8-10 MB spike). Cutting the live wrapper count from 40.2k to 25.0k (shared box shapes, static keys, disposed per-frame
handles) cut the engine object count from 41.9k to 30.5k. Each implicit `string` -> `StringName`/`NodePath`/`Variant` conversion
allocates a new wrapper per call. `Array<T>`, `Dictionary<K,V>`, `Rid`, `Callable` and `Signal` are not disposable. The probe is
`MS2_FRAMEPROBE=1` (bytes per frame, object count, frame time and a wrapper census by type every 120 frames).

## Rules
- a string literal passed to SetMeta/GetMeta/HasMeta/RemoveMeta/SetShaderParameter/EmitSignal/CallDeferred/Connect/IsAction*/GetNode
  (also `GetNode<T>("..")`)/FindChild/AddToGroup/..., or to a bare `Get(`/`Set(`/`Call(` (game/ only): use a static readonly StringName/NodePath.
  `x.Get("..")` on a C# type is not matched
- `StringName x = "lit"` (also NodePath/Variant) that is not static/const; `new StringName("lit")`/`new NodePath("lit")` outside a
  static field (a computed name cannot be hoisted and is not flagged)
- `GetChildren(...)`: use `node.Kids()` (game/scripts/NodeKids.cs) or GetChildCount()/GetChild(i)
- LINQ inside _Process, _PhysicsProcess, _Draw, _Input, _UnhandledInput, Tick, Advance (and names ending in Tick/Advance), also
  expression-bodied ones; `Mathf.Min/Max`, and `ToArray/Count` on a variable declared as a List/Stack/Queue/Dictionary/HashSet in the file, are not LINQ
- an `IntersectRay`/`GetRestInfo` result (a Dictionary) not disposed: `using var hit = ...`, or `hit.Dispose()` for that variable in
  the method; a result used without assignment (`IntersectRay(q).Count`) is a hit; a `return`ed result is left to the caller.
  `IntersectShape`/`IntersectPoint` (Array<T>) and `CastMotion` (float[]) have nothing to dispose
- `new RDUniform` whose variable is neither disposed nor returned in its method
- a `_RenderCallback` that does not dispose its RenderData parameter (any parameter name, one-line bodies too)
- a new primitive `*Shape3D` (Box/Sphere/Capsule/Cylinder) inside a loop without a TryGetValue/`??=` cache: share one shape per size
  (FieldBuilder.BoxOf); mesh shapes carry their own data and are not flagged

The method around a line is found by indentation, so it is a heuristic: a miss is possible, a false hit is silenced with perf-ok.
The receiver's type is not known, so a rule that cannot tell a Godot receiver from a C# one is limited to the forms it can.
