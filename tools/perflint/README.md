# perf-lint

Lint for Godot C# interop costs. The gate form reads only the lines a branch added (`git diff -U0 <base>...HEAD`, three-dot: since
the merge base; C# under `src/` and `game/`, not tests), so existing code is never flagged. It prints `file:line: rule -> fix`.
Exit 0 clean, 1 on a hit, 2 on an internal error or a bad flag (a crash is never a finding and never clean). The diff is one
`git diff -U0 -M` over all files, so a pure `git mv` adds no lines. Any directory of the repo works.

    python3 -I tools/perflint/perf-lint.py origin/main          # the branch against its base, run from the repo root
    python3 -I tools/perflint/perf-lint.py HEAD --tree          # a trial: merge base against the working tree, untracked files included
    python3 -I tools/perflint/perf-lint.py origin/main --head <rev>   # replay another tip
    python3 -I tools/perflint/perf-lint.py --all > backlog.tsv  # report only: every line of src/ and game/
    python3 -I tools/perflint/selftest.py                       # the lint's own test, in a throwaway repo

`--all` prints a TSV (file, line, rule, enclosing method, per_frame), per-frame rows first, and exits 0. A method counts as
per-frame when it is `_Process`/`_PhysicsProcess`/`_Draw`/`Tick`/`Advance` (or a name ending in Tick/Advance), or is called by bare
name from one in the same file (transitively); the same closure runs in diff mode.

The merge gate runs in a tree where `origin/main` is the pre-pick sha, so the default base stays `origin/main`.

Allow a hit with a same-line `// perf-ok: <reason>` (why the cost is fine there: a probe, a once-per-load path).

## Why (measured, Tria field walk, GodotSharp 4.7)
Every Godot object, `StringName`, `NodePath`, `Variant`, non-generic `Godot.Collections.Array/Dictionary` the C# side touches gets a
wrapper that `Godot.DisposablesTracker` holds until a GC finalizes it. When its dictionaries grow they rebuild every entry in one
frame (an 8-10 MB spike). Cutting the live wrapper count from 40.2k to 25.0k (shared box shapes, static keys, disposed per-frame
handles) cut the engine object count from 41.9k to 30.5k. Each implicit `string` -> `StringName`/`NodePath`/`Variant` conversion
allocates a new wrapper per call. `Array<T>`, `Dictionary<K,V>`, `Rid`, `Callable` and `Signal` are not disposable. The probe is
`MS2_FRAMEPROBE=1` (bytes per frame, object count, frame time and a wrapper census by type every 120 frames).

## Rules
Scope: the literal-name and `GetChildren` rules fail only inside per-frame methods; elsewhere they print as `note:` lines and do not
fail. LINQ counts in per-frame methods and in `_Input`/`_UnhandledInput`/`_GuiInput`. The ray, uniform, RenderData, shape and array
rules apply tree-wide.
- a string literal passed to SetMeta/GetMeta/HasMeta/RemoveMeta/SetShaderParameter/EmitSignal/CallDeferred/Connect/IsAction*/GetNode
  (also `GetNode<T>("..")`)/FindChild/AddToGroup/..., or to a bare `Get(`/`Set(`/`Call(` (game/ only): use a static readonly
  StringName/NodePath. `x.Get("..")` on a C# type is not matched, and a bare `Get("..")` is skipped in a file that declares its own
  `Get` method (Settings.cs)
- a string literal key on a Godot Dictionary (`g["advance"]`, a variable declared `Godot.Collections.Dictionary`, or `Dictionary` in a file that uses Godot.Collections): each read builds a Variant with a finalizer-owned disposer; use a static readonly Variant key (PhysicsKeys, GlyphKeys). Also caught: a variable set from `IntersectRay`/`GetRestInfo`, a `foreach` variable over `GetPropertyList`/`GetMethodList`/`GetSignalList`/`ShapedTextGetGlyphs`, and `((Godot.Collections.Dictionary)x)["k"]`. Not caught (the lint reads text, not types): a Dictionary returned by another call or field and read in one expression (`Obj.Foo()["k"]`), a Dictionary passed in as a parameter of an unqualified type in a file that also uses System's, and the typed `Dictionary<K,V>` (skipped: it is System's or Godot's typed one, and the typed one's reads have not been measured here). A name after a dot (`System.Collections.Generic.Dictionary`) is not Godot's. Cold sites print as notes; mark a deliberate one `// perf-ok: reason`
- `StringName x = "lit"` (also NodePath/Variant) that is not static/const; `new StringName("lit")`/`new NodePath("lit")` outside a
  static field (a computed name cannot be hoisted and is not flagged)
- `GetChildren(...)`: use `node.Kids()` (game/scripts/NodeKids.cs) or GetChildCount()/GetChild(i)
- LINQ: `.Select(` ... `.Count(` with parentheses (a `Count` property is not LINQ); `Mathf.Min/Max`, and `ToArray/Count` on a variable
  declared as a List/Stack/Queue/Dictionary/HashSet in the file, are not LINQ
- an `IntersectRay`/`GetRestInfo` (Dictionary) or `IntersectShape`/`IntersectPoint`/`CollideShape` (Array<T>) result not disposed:
  `using var hit = ...`, `hit.Dispose()` or `hit?.Dispose()` for that variable in the method. An `Array<T>` only wraps a
  non-generic Array, and that one is what the tracker holds, so dispose it through the cast:
  `using var r = (Godot.Collections.Array)space.IntersectShape(q, 1);`. A result used without assignment (`IntersectRay(q).Count`)
  is a hit; a `return`ed result is left to the caller. `CastMotion` returns a `float[]`: nothing to dispose
- `new Godot.Collections.Array(`/`new Array<T>(`/`new Array<T> {..}` per call that is neither `using`, disposed, `return`ed nor a static
  field. A typed wrapper over a disposed raw array (`using var raw = new Godot.Collections.Array(); ... new Array<RDUniform>(raw)`)
  passes
- `new RDUniform` whose variable is neither disposed nor returned in its method
- a `_RenderCallback` that does not dispose its RenderData parameter (any parameter name, one-line bodies too)
- a new primitive `*Shape3D` (Box/Sphere/Capsule/Cylinder) inside a loop without a TryGetValue/`??=` cache, also the target-typed
  `x.Shape = new() {..}`: share one shape per size (FieldBuilder.BoxOf); mesh shapes carry their own data and are not flagged

The method around a line is found by indentation, so it is a heuristic: a miss is possible, a false hit is silenced with perf-ok.
The receiver's type is not known, so a rule that cannot tell a Godot receiver from a C# one is limited to the forms it can.

## What the lint cannot see
Calls across files (a per-frame method that calls a helper in another file), signal and timer callbacks, and delegates stored and
invoked later. The frame probe (`MS2_FRAMEPROBE=1`) and the perf gate cover those.

## Guards for the fixes
Godot-bound fixes cannot run in the test host. `AppliedChildListsTests` and `DummyMemoTests` test the helpers; `WiringGuardTests`
(tests/Ms2.Ui.Tests) read As3View.cs and DummyAnchor.cs and fail when the applied-order check or the memo lookup order is reverted.
The shared StringName/PhysicsKeys/dispose fixes have no test of their own: the lint is their guard.
