#!/usr/bin/env python3
"""Diff lint for Godot C# interop costs. Scans ONLY the lines added by <base>..HEAD (default origin/main..HEAD) in C# under
src/ and game/; existing code is never flagged. Prints file:line, the rule and the helper/fix to use; exits 1 on a hit.
A same-line `// perf-ok: <reason>` marker allows a hit. Run in the tree being merged (cwd = repo root).
Rules: see RULES below; the skill godot-csharp-perf has the measured cost of each."""
import re, subprocess, sys

base = sys.argv[1] if len(sys.argv) > 1 else "origin/main"
range_ = base if "--tree" in sys.argv else base + "..HEAD"   # --tree: base against the working tree (a trial)
HOT = re.compile(r"^(_Process|_PhysicsProcess|_Draw|_Input|_UnhandledInput|Tick|Advance|\w+Tick|\w+Advance)$")
NAMEFN = r"(SetMeta|GetMeta|HasMeta|RemoveMeta|SetShaderParameter|GetShaderParameter|EmitSignal|Call|CallDeferred|Connect|Disconnect|IsConnected|IsActionPressed|IsActionJustPressed|IsActionReleased|GetNode|GetNodeOrNull|HasNode|FindChild|HasSignal|HasMethod)"
LINQ = re.compile(r"\.(Select|SelectMany|Where|OfType|Cast<\w+>|Any|All|First|FirstOrDefault|Last|LastOrDefault|Single|ToList|ToArray|ToDictionary|OrderBy|OrderByDescending|GroupBy|Distinct|Skip|Take|Sum|Zip|Concat)\s*[(<]")
HEADER = re.compile(r"^\s*(?:(?:public|private|protected|internal|static|override|virtual|sealed|async|unsafe|new|extern|partial)\s+)*[\w<>\[\],.?()\s]+?\s+(\w+)\s*(?:<[^>]*>)?\s*\([^;]*$")
NOTHDR = re.compile(r"^\s*(if|for|foreach|while|switch|using|lock|return|else|catch|case|new|yield|throw|var|await|do)\b|=>|=")
SHAPE = re.compile(r"new\s+(Box|Sphere|Capsule|Cylinder|Concave|Convex)\w*Shape3D\b")

def sh(*a):
    return subprocess.run(a, capture_output=True, text=True, check=True).stdout

def added():
    out, f, n = {}, None, 0
    for l in sh("git", "diff", "-U0", "--no-color", range_, "--", "*.cs").splitlines():
        if l.startswith("+++ "):
            f = l[6:] if l.startswith("+++ b/") else None
            if f and not (f.startswith("src/") or f.startswith("game/")): f = None
        elif l.startswith("@@"):
            m = re.search(r"\+(\d+)", l); n = int(m.group(1)) if m else 0
        elif l.startswith("+") and f:
            out.setdefault(f, []).append(n); n += 1
    return out

def indent(s): return len(s) - len(s.lstrip())

def enclosing(lines, i):
    """(header index, name) of the method containing line i: nearest earlier line with smaller indent that looks like a method header."""
    ind = indent(lines[i]); j = i
    while j > 0:
        j -= 1
        s = lines[j]
        if not s.strip() or s.strip().startswith(("//", "{", "}", "[")): continue
        if indent(s) < ind:
            ind = indent(s)
            if not NOTHDR.search(s.split("//")[0]) and HEADER.match(s.rstrip()): return j, HEADER.match(s.rstrip()).group(1)
            if NOTHDR.search(s.split("//")[0]) and "=>" not in s: continue
            if indent(s) == 0: break
    return None, None

def body(lines, h):
    ind = indent(lines[h]); end = h + 1
    while end < len(lines) and (not lines[end].strip() or indent(lines[end]) > ind or lines[end].strip() in ("{",)):
        end += 1
    return lines[h:end + 1]

def in_loop(lines, i):
    ind = indent(lines[i]); j = i
    while j > 0:
        j -= 1
        if lines[j].strip() in ("", "{", "}"): continue
        if indent(lines[j]) < ind:
            ind = indent(lines[j])
            if re.match(r"\s*(foreach|for|while)\b", lines[j]): return True
            if HEADER.match(lines[j].rstrip()) and not NOTHDR.search(lines[j]): return False
    return False

hits = 0
def hit(f, n, rule, fix):
    global hits; hits += 1
    print(f"{f}:{n}: {rule} -> {fix}")

for f, ns in added().items():
    lines = open(f, encoding="utf-8").read().split("\n")
    for n in ns:
        raw = lines[n - 1]; s = raw.split("//")[0]
        if "perf-ok:" in raw or not s.strip(): continue
        h, name = enclosing(lines, n - 1)
        hot = bool(name and HOT.match(name))
        isgame = f.startswith("game/")
        if isgame and re.search(r"(?<![\w])%s\s*\(\s*\"" % NAMEFN, s) or isgame and re.search(r"\b(Set|Get)\(\s*\"", s):
            hit(f, n, "string literal name passed to a Godot call (new StringName + wrapper each call)", "a static readonly StringName/NodePath (see the Meta/PhysicsKeys tables)")
        if re.search(r"new\s+(StringName|NodePath)\s*\(", s) and "static readonly" not in s and "const" not in s:
            hit(f, n, "new StringName/NodePath outside a static field", "static readonly StringName/NodePath")
        if re.search(r"\bGetChildren\(\)", s):
            hit(f, n, "GetChildren() allocates a Godot array and a managed array", "foreach (var c in node.Kids()) or GetChildCount()/GetChild(i)")
        if hot and LINQ.search(s):
            hit(f, n, f"LINQ in {name} (allocates every call)", "an indexed loop over a reused list")
        if re.search(r"=\s*[\w.()]*\.(IntersectRay|GetRestInfo|IntersectShape|CastMotion|IntersectPoint)\(", s) and "using" not in s:
            b = "\n".join(body(lines, h)) if h is not None else ""
            if ".Dispose()" not in b and "using" not in b:
                hit(f, n, "physics query result never disposed", "`using var hit = ...` or hit.Dispose() after reading (query objects stay reused)")
        if re.search(r"new\s+RDUniform\b", s):
            b = "\n".join(body(lines, h)) if h is not None else ""
            if ".Dispose()" not in b and "using" not in b:
                hit(f, n, "new RDUniform without Dispose (tracked until a GC finalizes it)", "Dispose after UniformSetCacheRD.GetCache copied it")
        if h is not None and name == "_RenderCallback" and n - 1 > h:
            b = "\n".join(body(lines, h))
            if not re.search(r"using\s+var\s+\w+\s*=\s*renderData\b|renderData\.Dispose\(\)", b):
                hit(f, h + 1, "_RenderCallback does not dispose its RenderData (one wrapper per frame)", "`using var data = renderData;` as the first line")
        if SHAPE.search(s) and (in_loop(lines, n - 1) or re.search(r"\b(foreach|for|while)\s*\(.*\)\s*\{?.*new\s+\w+Shape3D", s)):
            ctx = "\n".join(lines[max(0, n - 5):n])
            if "TryGetValue" not in ctx and "??=" not in ctx:
                hit(f, n, "new Shape3D inside a loop (a wrapper and engine object per entity of a repeated size)", "share one shape per size (FieldBuilder.BoxOf pattern) when nothing mutates it")
print(f"perf-lint: {hits} hit(s)" if hits else "perf-lint: clean")
sys.exit(1 if hits else 0)
