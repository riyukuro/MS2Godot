# In-game debug endpoint

Inspect a running MS2Godot client (scene tree, materials, C# state, AS3 movies, screenshots) without the editor, an
account, or extra probes and rebuilds.

## Turn it on

Debug builds (and any configuration that is not `Release`/`ExportRelease`) define `MS2_DEVTOOLS` and contain the endpoint.
Release exports do not contain it at all. It is off unless the environment has `MS2_DEBUG=1`; when off nothing is allocated
or ticked.

```
MS2_DEBUG=1 MS2_DEBUG_FILE=/path/to/dbg.json MS2_DEBUG_OUT=/path/to/shots godot-mono --path game ...
```

- `MS2_DEBUG_FILE`: where the game writes `{port, token, pid}` (mode 0600; default `debug.json` in the game's user dir).
  Give every game its own file when you run several.
- `MS2_DEBUG_OUT`: directory for screenshots (default the user dir). Requests name a plain file, never a path.
- The server listens on 127.0.0.1, port chosen by the OS, and every request must carry the token.

## Use it

```
python3 -I tools/ms2dbg/ms2dbg.py --file /path/to/dbg.json <command> [name=value ...]
```

Without `--file` the tool uses `MS2_DEBUG_FILE`. Values are JSON when they parse (`depth=2 csharp=true`), else strings;
`props=` and `names=` take `a,b,c`. `--json '{...}'` sends an object as is. The result is printed as JSON; errors go to
stderr with exit code 1. `--help` lists every command and argument.

| command | purpose |
| --- | --- |
| `ping` | pid, frame, fps, and the endpoint's own per-frame cost |
| `tree path= depth= filter=` | names, types, visibility, transforms |
| `node path= props= csharp=true member= depth= items= statics=` | engine properties and the C# state (fields, auto-properties, base-class privates; cycles cut, large collections truncated) |
| `material path= surface=` | shader, render mode, every parameter's current value, instance uniforms, priority, blend mode |
| `find type= name= group= under=` | node paths; `type` and `name` are case-insensitive substring matches (no `*`) |
| `pick x= y=` | 3D nodes along the camera ray at the pixel, UI controls and AS3 display objects |
| `screenshot file=door.png` | viewport PNG under `MS2_DEBUG_OUT` |
| `monitors names=` | Performance monitors (enum names, any case) and managed GC counts and bytes; with no names: fps, process times, memory, object and node counts, render objects, primitives, draw calls and video, texture and buffer memory |
| `input key=Space` / `mouse=left x= y=` / `action=` | send an input event (the only command that changes the game) |
| `log since=` | print output, `push_error` and `push_warning` lines after a sequence number |
| `as3 movie= path= depth=` | no args lists the movies; with `movie` a display-list and variable view; `depth` (default 1, at most 6) is how many child levels show |

There is no eval, no method call and no property write. A request that throws returns an error and never reaches the game.

### Example: what draws the portal arch

```
ms2dbg find type=MeshInstance3D under=/root/Hud/Field/FieldEffects     # candidate meshes
ms2dbg material path=/root/Hud/Field/FieldEffects/Effect_1/Z/Plane     # shader + parameter values this frame
ms2dbg node path=/root/Hud/Field/FieldEffects/Effect_1 csharp=true     # the EffectInstance state
ms2dbg screenshot file=arch.png
```

A mesh can carry a material override and a mesh material; `material` lists both with a `where` field, the override wins.

## MCP

`python3 -I tools/ms2dbg/ms2dbg.py [--file ...] mcp` is a stdio MCP server (JSON-RPC lines, no dependencies) that exposes the
same commands as tools. The port file is looked up on every call, so the server can be started before the game.

## Reading the numbers

- Requests run on the main thread inside `_Process`. The 4 ms budget only applies between requests: at least one request runs
  per frame, and a heavy `tree`/`node` dump delays that frame by its own cost.
- A request waits up to 15 s for the main thread; after that the client gets a timeout error and the request is skipped if it has not started.
- `ping` reports `endpoint.processMs` over `endpoint.frames`: the time spent in the endpoint's per-frame check, idle frames
  included. With no client it is a single volatile read.
- `pick` tests 3D nodes by bounding box: a `MultiMeshInstance3D` is one box for its whole batch.

## Safety

Loopback only, token per run, file mode 0600, 8 connections at most, 1 MiB per request, 15 s per request. A connection must
send a request with the token within 5 s and is closed after 30 s of silence. The `node` dump reads fields only and runs no
game code (no property getters, no game collection methods). Treat the port
file like a credential for that game process. The endpoint is compiled into Debug and ExportDebug builds and stays off unless `MS2_DEBUG=1` is set; it is absent from Release
and ExportRelease exports. Check with
`grep -c DevEndpoint MS2Godot.dll` on the exported assembly (0 expected).
