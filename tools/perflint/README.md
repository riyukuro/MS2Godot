# perf-lint

Diff lint for Godot C# interop costs. It reads only the lines a branch added (`git diff -U0 <base>..HEAD`, C# under `src/` and
`game/`), so existing code is never flagged. It prints `file:line: rule -> fix` and exits 1 on a hit.

    python3 -I tools/perflint/perf-lint.py origin/main          # the branch against its base, run from the repo root
    python3 -I tools/perflint/perf-lint.py HEAD --tree          # a trial: HEAD against the working tree

Allow a hit with a same-line `// perf-ok: <reason>` (say why the cost is fine there: a probe, a once-per-load path).

Rules (the measured cost of each is in the godot-csharp-perf skill):
- a string literal passed to Set/Get/SetMeta/GetMeta/HasMeta/SetShaderParameter/EmitSignal/Call/Connect/IsAction*/GetNode (game/ only): use a static readonly StringName/NodePath
- `new StringName(...)` / `new NodePath(...)` outside a static field
- `GetChildren()`: use `node.Kids()` (game/scripts/NodeKids.cs) or GetChildCount()/GetChild(i)
- LINQ inside _Process, _PhysicsProcess, _Draw, _Input, _UnhandledInput, Tick, Advance (and names ending in Tick/Advance)
- an IntersectRay/GetRestInfo/IntersectShape/CastMotion result with no `using` or Dispose in its method
- `new RDUniform` with no Dispose in its method
- a `_RenderCallback` that does not dispose its RenderData
- a new `*Shape3D` inside a loop without a TryGetValue/`??=` cache: share one shape per size (FieldBuilder.BoxOf)

The method around a line is found by indentation, so it is a heuristic: a miss is possible, a false hit is silenced with perf-ok.
