"""Tests for ms2dbg against a fake game: python3 -I tools/ms2dbg/test_ms2dbg.py"""
import importlib.util
import json
import os
import socketserver
import subprocess
import sys
import tempfile
import threading
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "ms2dbg.py")
spec = importlib.util.spec_from_file_location("ms2dbg", SCRIPT)
ms2dbg = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ms2dbg)

TOKEN = "t" * 32


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        req = json.loads(self.rfile.readline())
        self.server.seen.append(req)
        if req.get("mode") == "notobject" or req.get("cmd") == "notobject":
            self.wfile.write(b"[1, 2]\n")
            return
        if req.get("token") != TOKEN:
            out = {"id": req["id"], "ok": False, "error": "bad token"}
        elif req["cmd"] == "boom":
            out = {"id": req["id"], "ok": False, "error": "boom happened"}
        else:
            out = {"id": req["id"], "ok": True, "result": {"echo": req["cmd"], "args": {k: v for k, v in req.items() if k not in ("id", "token", "cmd")}}}
        self.wfile.write((json.dumps(out) + "\n").encode())


class Fake:
    def __enter__(self):
        self.srv = socketserver.ThreadingTCPServer(("127.0.0.1", 0), Handler)
        self.srv.seen = []
        threading.Thread(target=self.srv.serve_forever, daemon=True).start()
        self.dir = tempfile.TemporaryDirectory()
        self.file = os.path.join(self.dir.name, "dbg.json")
        with open(self.file, "w") as f:
            json.dump({"port": self.srv.server_address[1], "token": TOKEN, "pid": 1}, f)
        return self

    def __exit__(self, *a):
        self.srv.shutdown()
        self.srv.server_close()
        self.dir.cleanup()


class ArgTests(unittest.TestCase):
    def test_values(self):
        a = ms2dbg.parse_pairs(["depth=2", "csharp=true", "path=/root/A", "props=a,b", "x=1.5", "name=123", "movie=x y"])
        self.assertEqual(a, {"depth": 2, "csharp": True, "path": "/root/A", "props": ["a", "b"], "x": 1.5, "name": "123", "movie": "x y"})

    def test_bad_pair(self):
        with self.assertRaises(ms2dbg.DebugError):
            ms2dbg.parse_pairs(["nope"])


class ClientTests(unittest.TestCase):
    def test_round_trip_carries_token_and_args(self):
        with Fake() as f:
            r = ms2dbg.request(f.file, "tree", {"depth": 2})
            self.assertEqual(r["echo"], "tree")
            self.assertEqual(r["args"], {"depth": 2})
            self.assertEqual(f.srv.seen[0]["token"], TOKEN)

    def test_error_response_raises(self):
        with Fake() as f:
            with self.assertRaisesRegex(ms2dbg.DebugError, "boom happened"):
                ms2dbg.request(f.file, "boom")

    def test_wrong_token_refused(self):
        with Fake() as f:
            with open(f.file, "w") as h:
                json.dump({"port": f.srv.server_address[1], "token": "x", "pid": 1}, h)
            with self.assertRaisesRegex(ms2dbg.DebugError, "bad token"):
                ms2dbg.request(f.file, "ping")

    def test_missing_and_stale_port_file(self):
        with self.assertRaises(ms2dbg.DebugError):
            ms2dbg.request("/nonexistent/dbg.json", "ping")
        with Fake() as f:
            port = f.srv.server_address[1]
            file = f.file
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "stale.json")
            with open(p, "w") as h:
                json.dump({"port": port, "token": TOKEN}, h)
            with self.assertRaisesRegex(ms2dbg.DebugError, "cannot connect"):
                ms2dbg.request(p, "ping")


class NotObjectTests(unittest.TestCase):
    def test_non_object_reply_is_a_debug_error(self):
        with Fake() as f:
            with self.assertRaises(ms2dbg.DebugError):
                ms2dbg.request(f.file, "notobject")

    def test_info_path_skips_a_dead_game(self):
        with tempfile.TemporaryDirectory() as home:
            for name, pid in (("alive", os.getpid()), ("dead", 2 ** 22 + 12345)):
                d = os.path.join(home, ".local/share/godot/app_userdata", name)
                os.makedirs(d)
                with open(os.path.join(d, "debug.json"), "w") as fh:
                    json.dump({"port": 1, "token": "x", "pid": pid}, fh)
            # the dead game's file is the newest
            os.utime(os.path.join(home, ".local/share/godot/app_userdata/dead/debug.json"), (2e9, 2e9))
            old = {k: os.environ.get(k) for k in ("HOME", "MS2_DEBUG_FILE")}
            os.environ["HOME"] = home
            os.environ.pop("MS2_DEBUG_FILE", None)
            try:
                self.assertIn("alive", ms2dbg.info_path())
            finally:
                for k, v in old.items():
                    if v is None:
                        os.environ.pop(k, None)
                    else:
                        os.environ[k] = v


def run(args, stdin=None, env_file=None):
    env = {k: v for k, v in os.environ.items() if k != "MS2_DEBUG_FILE"}
    if env_file:
        env["MS2_DEBUG_FILE"] = env_file
    return subprocess.run([sys.executable, "-I", SCRIPT] + args, input=stdin, capture_output=True, text=True, env=env, timeout=30)


class CliTests(unittest.TestCase):
    def test_cli_prints_json(self):
        with Fake() as f:
            p = run(["--file", f.file, "node", "path=/root", "csharp=true"])
            self.assertEqual(p.returncode, 0, p.stderr)
            self.assertEqual(json.loads(p.stdout)["args"], {"path": "/root", "csharp": True})

    def test_env_file_and_json_flag(self):
        with Fake() as f:
            p = run(["find", "--json", '{"type": "MeshInstance3D"}'], env_file=f.file)
            self.assertEqual(json.loads(p.stdout)["args"], {"type": "MeshInstance3D"})

    def test_failure_exit_code(self):
        with Fake() as f:
            p = run(["--file", f.file, "boom"])
            self.assertEqual(p.returncode, 1)
            self.assertIn("unknown command", p.stderr)
            p = run(["--file", f.file, "tree", "oops"])
            self.assertEqual(p.returncode, 1)


class McpTests(unittest.TestCase):
    def talk(self, f, msgs):
        p = run(["--file", f.file, "mcp"], stdin="".join(json.dumps(m) + "\n" for m in msgs))
        self.assertEqual(p.returncode, 0, p.stderr)
        return [json.loads(l) for l in p.stdout.splitlines()]

    def test_handshake_list_call(self):
        with Fake() as f:
            out = self.talk(f, [
                {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-03-26"}},
                {"jsonrpc": "2.0", "method": "notifications/initialized"},
                {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
                {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "tree", "arguments": {"depth": 1}}},
                {"jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": {"name": "nope", "arguments": {}}},
                {"jsonrpc": "2.0", "id": 5, "method": "bogus"},
            ])
            self.assertEqual([o["id"] for o in out], [1, 2, 3, 4, 5])   # the notification gets no answer
            self.assertEqual(out[0]["result"]["protocolVersion"], "2025-03-26")
            names = {t["name"] for t in out[1]["result"]["tools"]}
            self.assertEqual(names, set(ms2dbg.COMMANDS))
            self.assertFalse(out[2]["result"]["isError"])
            self.assertEqual(json.loads(out[2]["result"]["content"][0]["text"])["args"], {"depth": 1})
            self.assertEqual(out[3]["error"]["code"], -32602)
            self.assertEqual(out[4]["error"]["code"], -32601)

    def test_game_down_is_a_tool_error_not_a_crash(self):
        with tempfile.TemporaryDirectory() as d:
            class F: file = os.path.join(d, "none.json")
            out = self.talk(F, [{"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": "ping", "arguments": {}}},
                                {"jsonrpc": "2.0", "id": 2, "method": "ping"}])
            self.assertTrue(out[0]["result"]["isError"])
            self.assertEqual(out[1]["result"], {})

    def test_server_survives_a_bad_reply(self):
        with Fake() as f:
            out = self.talk(f, [{"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": "ping", "arguments": {"mode": "notobject"}}},
                                {"jsonrpc": "2.0", "id": 2, "method": "ping"}])
            self.assertTrue(out[0]["result"]["isError"])
            self.assertEqual(out[1]["result"], {})

    def test_garbage_line_gets_parse_error(self):
        with Fake() as f:
            p = run(["--file", f.file, "mcp"], stdin="not json\n")
            self.assertEqual(json.loads(p.stdout)["error"]["code"], -32700)


if __name__ == "__main__":
    unittest.main()
