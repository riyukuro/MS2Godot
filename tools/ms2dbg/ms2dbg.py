#!/usr/bin/env python3
"""ms2dbg: client for the MS2Godot in-game debug endpoint (read-only inspection of a running game).

Run it isolated:  python3 -I tools/ms2dbg/ms2dbg.py [--file PORTFILE] <command> [name=value ...]
                  python3 -I tools/ms2dbg/ms2dbg.py [--file PORTFILE] mcp      (stdio MCP server)

The game side starts only with env MS2_DEBUG=1 (Debug builds) and writes {port, token, pid} to the file named by
MS2_DEBUG_FILE (default <user dir>/debug.json). This tool reads that file, sends one request, prints the JSON result.
Standard library only, a single file (python -I drops the script directory from sys.path).
"""
import glob
import json
import os
import socket
import sys

TIMEOUT = 30.0

# command -> (description, {arg: (type, description)}); type: s string, i int, n number, b bool, l list of strings
COMMANDS = {
    "ping": ("Pid, frame, fps and the endpoint's own per-frame cost.", {}),
    "tree": ("Node names, types, visibility and transforms below a node.", {
        "path": ("s", "node path, default /root"), "depth": ("i", "levels to expand (default 3)"),
        "filter": ("s", "keep only nodes whose name or type contains this")}),
    "node": ("Engine properties and/or the C# state (fields, auto-properties, base-class privates) of one node.", {
        "path": ("s", "node path"), "props": ("l", "engine property names to read"),
        "csharp": ("b", "dump the C# object"), "member": ("s", "reach into it, e.g. 'meshes[0].world'"),
        "depth": ("i", "C# dump depth (default 3, max 8)"), "items": ("i", "collection items shown (default 24)"),
        "statics": ("b", "include static fields")}),
    "material": ("Every material a node draws with: shader, render mode, every parameter's current value, instance uniforms.", {
        "path": ("s", "MeshInstance3D / CanvasItem path"), "surface": ("i", "one mesh surface only")}),
    "find": ("Node paths by engine/C# type, name pattern or group.", {
        "type": ("s", "class name"), "name": ("s", "name, * allowed"), "group": ("s", "group name"),
        "under": ("s", "search below this path")}),
    "pick": ("What is at screen point x,y: 3D nodes along the camera ray, UI controls and AS3 display objects.", {
        "x": ("n", "pixel x"), "y": ("n", "pixel y")}),
    "screenshot": ("Save the viewport as a PNG named file under MS2_DEBUG_OUT (default the game's user dir).", {
        "file": ("s", "plain file name, e.g. door.png")}),
    "monitors": ("Godot Performance monitors plus managed GC counts and bytes.", {
        "names": ("l", "monitor enum names; none = all")}),
    "input": ("Send a key, mouse or action event (the only command that changes the game).", {
        "key": ("s", "key name, e.g. Space, Enter, A"), "mouse": ("s", "move|left|right|middle|wheel_up|wheel_down"),
        "action": ("s", "InputMap action"), "x": ("n", "mouse x"), "y": ("n", "mouse y"),
        "pressed": ("b", "omit = press and release / click"), "strength": ("n", "action strength"),
        "shift": ("b", "shift held"), "ctrl": ("b", "ctrl held"), "alt": ("b", "alt held")}),
    "log": ("Game log lines (print, push_error, push_warning) after sequence number since.", {
        "since": ("i", "last seq already seen (default 0)"), "max": ("i", "most lines to return")}),
    "as3": ("AS3 movies: no args lists the views; movie (+ dotted instance path) shows a display-list / variable view.", {
        "movie": ("s", "movie name"), "path": ("s", "dotted instance names from the movie root")}),
}
LIST_ARGS = {"props", "names"}


class DebugError(Exception):
    pass


def info_path(explicit=None):
    p = explicit or os.environ.get("MS2_DEBUG_FILE")
    if p:
        return p
    found = sorted(glob.glob(os.path.expanduser("~/.local/share/godot/app_userdata/*/debug.json")), key=os.path.getmtime, reverse=True)
    for f in found:  # newest first; a port file whose game has exited is skipped
        try:
            with open(f) as fh:
                os.kill(int(json.load(fh)["pid"]), 0)
            return f
        except (OSError, ValueError, KeyError, TypeError):
            continue
    raise DebugError("no port file: set MS2_DEBUG_FILE (the same value the game was started with) or pass --file")


def load_info(path):
    try:
        with open(path) as f:
            info = json.load(f)
        return {"port": int(info["port"]), "token": str(info["token"])}
    except (OSError, ValueError, KeyError) as e:
        raise DebugError("cannot read port file %s: %s (is the game running with MS2_DEBUG=1?)" % (path, e))


def request(info_file, cmd, args=None, timeout=TIMEOUT):
    """Send one command, return the result object. Raises DebugError on any failure."""
    info = load_info(info_file)
    msg = dict(args or {})
    msg.update({"id": 1, "token": info["token"], "cmd": cmd})
    try:
        s = socket.create_connection(("127.0.0.1", info["port"]), timeout=timeout)
    except OSError as e:
        raise DebugError("cannot connect to the game on port %d: %s (stale port file?)" % (info["port"], e))
    with s:
        s.settimeout(timeout)
        s.sendall((json.dumps(msg) + "\n").encode())
        buf = b""
        try:
            while not buf.endswith(b"\n"):
                chunk = s.recv(1 << 16)
                if not chunk:
                    break
                buf += chunk
        except OSError as e:
            raise DebugError("no answer: %s" % e)
    try:
        resp = json.loads(buf.decode())
    except ValueError:
        raise DebugError("bad answer from the game")
    if not isinstance(resp, dict):
        raise DebugError("bad answer from the game (not an object)")
    if not resp.get("ok"):
        raise DebugError(str(resp.get("error")))
    return resp.get("result")


def coerce(name, raw):
    spec = COMMANDS_ARGS_ANY.get(name)
    if name in LIST_ARGS and not raw.startswith("["):
        return [x for x in raw.split(",") if x]
    if spec == "s":
        return raw
    try:
        return json.loads(raw)
    except ValueError:
        return raw


COMMANDS_ARGS_ANY = {}
for _d, _a in COMMANDS.values():
    for _k, (_t, _) in _a.items():
        COMMANDS_ARGS_ANY.setdefault(_k, _t)


def parse_pairs(pairs):
    args = {}
    for p in pairs:
        if "=" not in p:
            raise DebugError("argument '%s' is not name=value" % p)
        k, v = p.split("=", 1)
        args[k] = coerce(k, v)
    return args


# ---- MCP (stdio, newline-delimited JSON-RPC 2.0) -------------------------------------------------------------------

def _schema(args):
    kinds = {"s": {"type": "string"}, "i": {"type": "integer"}, "n": {"type": "number"}, "b": {"type": "boolean"},
             "l": {"type": "array", "items": {"type": "string"}}}
    return {"type": "object", "properties": {k: dict(kinds[t], description=d) for k, (t, d) in args.items()}}


def mcp_tools():
    return [{"name": c, "description": d + " Read-only except input.", "inputSchema": _schema(a)} for c, (d, a) in COMMANDS.items()]


def mcp_serve(explicit):
    def send(o):
        sys.stdout.write(json.dumps(o) + "\n")
        sys.stdout.flush()
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            m = json.loads(line)
            mid, method = m.get("id"), m.get("method")
        except (ValueError, AttributeError):
            send({"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "parse error"}})
            continue
        if mid is None:
            continue    # notification (initialized, cancelled): no answer
        if method == "initialize":
            send({"jsonrpc": "2.0", "id": mid, "result": {
                "protocolVersion": (m.get("params") or {}).get("protocolVersion", "2024-11-05"),
                "capabilities": {"tools": {}}, "serverInfo": {"name": "ms2dbg", "version": "1"}}})
        elif method == "ping":
            send({"jsonrpc": "2.0", "id": mid, "result": {}})
        elif method == "tools/list":
            send({"jsonrpc": "2.0", "id": mid, "result": {"tools": mcp_tools()}})
        elif method == "tools/call":
            p = m.get("params") or {}
            name, args = p.get("name"), p.get("arguments") or {}
            if name not in COMMANDS or not isinstance(args, dict):
                send({"jsonrpc": "2.0", "id": mid, "error": {"code": -32602, "message": "unknown tool " + str(name)}})
                continue
            try:
                text, err = json.dumps(request(info_path(explicit), name, args), indent=1), False
            except Exception as e:  # one bad call must not end the server
                text, err = str(e) or type(e).__name__, True
            send({"jsonrpc": "2.0", "id": mid, "result": {"content": [{"type": "text", "text": text}], "isError": err}})
        else:
            send({"jsonrpc": "2.0", "id": mid, "error": {"code": -32601, "message": "method not found"}})


def usage():
    out = ["usage: ms2dbg.py [--file PORTFILE] <command> [name=value ...] | --json '{...}'", "       ms2dbg.py [--file PORTFILE] mcp", ""]
    for c, (d, a) in COMMANDS.items():
        out.append("  %-10s %s" % (c, d))
        for k, (t, desc) in a.items():
            out.append("      %-8s %s" % (k, desc))
    out.append("\nvalues are JSON when they parse (depth=2 csharp=true), else strings; props= and names= take a,b,c")
    return "\n".join(out)


def main(argv):
    argv = list(argv)
    explicit = None
    if len(argv) >= 2 and argv[0] == "--file":
        explicit, argv = argv[1], argv[2:]
    if not argv or argv[0] in ("-h", "--help", "help"):
        print(usage())
        return 0
    cmd, rest = argv[0], argv[1:]
    try:
        if cmd == "mcp":
            mcp_serve(explicit)    # the port file is looked up per call: the game may start (or restart) later
            return 0
        if cmd not in COMMANDS:
            raise DebugError("unknown command '%s' (try --help)" % cmd)
        path = info_path(explicit)
        if rest[:1] == ["--json"]:
            if len(rest) != 2:
                raise DebugError("--json takes one JSON object")
            args = json.loads(rest[1])
            if not isinstance(args, dict):
                raise DebugError("--json takes one JSON object")
        else:
            args = parse_pairs(rest)
        print(json.dumps(request(path, cmd, args), indent=1))
        return 0
    except (DebugError, ValueError) as e:
        print("ms2dbg: %s" % e, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
