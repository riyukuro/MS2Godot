#!/usr/bin/env python3
"""Self-test of perf-lint.py in a throwaway git repo: python3 -I tools/perflint/selftest.py (exit 0 = all pass)."""
import os, subprocess, sys, tempfile
LINT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "perf-lint.py")
G = ["git", "-c", "user.name=t", "-c", "user.email=t@t"]
def run(cwd, *a): return subprocess.run(a, cwd=cwd, capture_output=True, text=True)
def lint(cwd, *a): r = run(cwd, sys.executable, "-I", LINT, *a); return r.returncode, r.stdout + r.stderr

BASE = '''using Godot;
public partial class T : Node
{
    static string Get(string a, string b) => a;
    static string Cfg => Get("MS2_DATA", "data");
    public override void _Process(double d)
    {
        Cold();
    }
    void Cold()
    {
    }
    void Setup()
    {
    }
}
'''
def check(name, ok):
    print(("ok   " if ok else "FAIL ") + name); return ok

def main():
    d = tempfile.mkdtemp(prefix="perflint-selftest-")
    run(d, "git", "init", "-q", "-b", "main"); os.makedirs(d + "/game/scripts")
    p = d + "/game/scripts/T.cs"; open(p, "w").write(BASE)
    run(d, *G, "add", "."); run(d, *G, "commit", "-qm", "base")
    run(d, "git", "branch", "base"); run(d, "git", "checkout", "-qb", "work")
    res = []
    # a pure rename (also to a path with a space) has no added lines
    run(d, "git", "mv", "game/scripts/T.cs", "game/scripts/T t.cs"); run(d, *G, "commit", "-qm", "mv")
    rc, out = lint(d, "base"); res.append(check("pure rename to a path with a space: clean", rc == 0 and "clean" in out))
    p = d + "/game/scripts/T t.cs"; s = open(p).read()
    s = s.replace("        Cold();\n", '        Cold();\n        var a = GetNode("A");\n        var h = space.IntersectRay(q);\n        var arr = new Godot.Collections.Array();\n')
    s = s.replace("    void Cold()\n    {\n", '    void Cold()\n    {\n        var k = GetChildren();\n')
    s = s.replace("    void Setup()\n    {\n", '    void Setup()\n    {\n        var b = GetNode("B");\n        using var r = (Godot.Collections.Array)space.IntersectShape(q, 1);\n        var c = Get("x", "y");\n')
    open(p, "w").write(s); run(d, *G, "commit", "-qam", "edit")
    rc, out = lint(d, "base")
    if os.environ.get("SELFTEST_VERBOSE"): print(out)
    res.append(check("rename + edit: per-frame literal name fails", rc == 1 and 'T t.cs:9: string literal' in out))
    res.append(check("callee of _Process: GetChildren fails", "T t.cs:15: GetChildren" in out))
    res.append(check("undisposed IntersectRay fails", "T t.cs:10: physics query" in out))
    res.append(check("per-call new Array fails", "T t.cs:11: new Godot Array" in out))
    res.append(check("cold method: note, not a hit", "note: game/scripts/T t.cs:19: string literal" in out))
    res.append(check("using var cast IntersectShape passes", "T t.cs:20" not in out))
    res.append(check("bare Get next to a C# Get helper is not Godot", ":20:" not in out and "T t.cs:5" not in out))
    open(d + "/game/scripts/U.cs", "w").write('using Godot;\nusing Godot.Collections;\npublic partial class U : Node\n{\n    public override void _Process(double d)\n    {\n        foreach (Dictionary g in ts.Glyphs())\n        {\n            var a = (float)g["advance"];\n            var b = (float)g[Keys.Advance];\n        }\n    }\n}\n')
    run(d, *G, "add", "."); run(d, *G, "commit", "-qm", "u")
    rc, out = lint(d, "base")
    res.append(check("string literal key on a Godot Dictionary fails, a static key passes", "U.cs:9: string literal key" in out and "U.cs:10" not in out))
    res.append(check("flags: unknown rc 2, --head without value rc 2", lint(d, "--bogus")[0] == 2 and lint(d, "--head")[0] == 2))
    res.append(check("bad ref rc 2", lint(d, "nonexistent-ref")[0] == 2))
    os.makedirs(d + "/game/sub"); res.append(check("--all from a subdirectory", subprocess.run([sys.executable, "-I", LINT, "--all"], cwd=d + "/game/sub", capture_output=True, text=True).returncode == 0))
    return 0 if all(res) else 1
sys.exit(main())
