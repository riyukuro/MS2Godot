#!/usr/bin/env python3
"""Diff lint for Godot C# interop costs. Scans ONLY the lines added by <base>...HEAD (three-dot: since the merge base) in C#
under src/ and game/ (not tests); existing code is never flagged. Prints file:line, the rule and the fix; exit 0 clean,
1 on a hit, 2 on an internal error (a crash is never a finding and never clean). A same-line `// perf-ok: <reason>` allows a hit.
`--all`: report-only backlog of every line of src/ and game/ (TSV: file, line, rule, method, per_frame; per-frame rows first; exit 0).
Run in the repo root. `--tree` compares the merge base with the working tree and also reads untracked files (a trial).
`--head REV` replaces HEAD (to replay old batches). Rules: tools/perflint/README.md."""
import re, subprocess, sys

HOT = re.compile(r"^(_Process|_PhysicsProcess|_Draw|_Input|_UnhandledInput|Tick|Advance|\w+Tick|\w+Advance)$")
NAMEFN = (r"(SetMeta|GetMeta|HasMeta|RemoveMeta|SetShaderParameter|GetShaderParameter|EmitSignal|CallDeferred|Connect|Disconnect|"
          r"IsConnected|IsActionPressed|IsActionJustPressed|IsActionReleased|IsActionJustReleased|GetActionStrength|"
          r"GetNode|GetNodeOrNull|HasNode|FindChild|HasSignal|HasMethod|AddToGroup|IsInGroup|RemoveFromGroup|SetDeferred)")
LINQ = re.compile(r"\.(Select|SelectMany|Where|OfType|Cast<\w+>|Any|All|First|FirstOrDefault|Last|LastOrDefault|Single|SingleOrDefault|"
                  r"ToList|ToArray|ToDictionary|ToHashSet|OrderBy|OrderByDescending|ThenBy|GroupBy|Distinct|Skip|Take|Sum|Min|Max|Average|"
                  r"Zip|Concat|Count)\s*[(<]")
MODS = r"(?:(?:public|private|protected|internal|static|override|virtual|sealed|async|unsafe|new|extern|partial)\s+)*"
HEADER = re.compile(r"^\s*" + MODS + r"[\w<>\[\],.?]+(?:\s*<[^>]*>)?\s+(\w+)\s*(?:<[^>]*>)?\s*\((.*)$")
NOTKW = re.compile(r"^\s*(if|for|foreach|while|switch|using|lock|return|else|catch|case|new|yield|throw|var|await|do|fixed|try)\b")
PHYS = r"(IntersectRay|GetRestInfo)"   # Dictionary results; IntersectShape/IntersectPoint/CastMotion return an Array<T> or float[] (nothing to dispose)
SHAPE = re.compile(r"\bnew\s+(?:Box|Sphere|Capsule|Cylinder|WorldBoundary)\w*Shape3D\b|\b(?:Box|Sphere|Capsule|Cylinder)\w*Shape3D\s+\w+\s*=\s*new\s*\(")   # primitives only: a mesh shape carries its own data and cannot be shared

class Fail(Exception): pass

def git(*a, z=False):
    r = subprocess.run(("git",) + a, capture_output=True)
    if r.returncode != 0: raise Fail("git " + " ".join(a) + ": " + r.stderr.decode("utf-8", "replace").strip())
    return r.stdout

def dec(b): return b.decode("utf-8", "replace")

def wanted(f):
    return f.endswith(".cs") and (f.startswith("src/") or f.startswith("game/")) and "/tests/" not in "/" + f and not f.startswith("game/tests/")

def added(base, head, tree):
    """{path: [added line numbers]}, and a reader for each file's content."""
    mb = dec(git("merge-base", base, head)).strip()
    out = {}
    names = [dec(x) for x in git("diff", "-z", "--name-only", "--no-ext-diff", "--no-textconv", "--no-color", "--diff-filter=ACMR",
                                 mb, *([] if tree else [head]), "--", "*.cs").split(b"\0") if x]
    for f in names:
        if not wanted(f): continue
        d = dec(git("diff", "-U0", "--no-ext-diff", "--no-textconv", "--no-color", "--src-prefix=a/", "--dst-prefix=b/",
                    mb, *([] if tree else [head]), "--", f))
        n, lst = 0, []
        for l in d.split("\n"):
            if l.startswith("+++ "):
                if l.rstrip("\t") not in ("+++ b/" + f, "+++ /dev/null"): raise Fail("unknown +++ form: %r for %s" % (l, f))
            elif l.startswith("--- ") or l.startswith("diff ") or l.startswith("index "): continue
            elif l.startswith("@@"):
                m = re.match(r"@@ -\S+ \+(\d+)", l)
                if not m: raise Fail("bad hunk header: %r" % l)
                n = int(m.group(1))
            elif l.startswith("+"):
                lst.append(n); n += 1
        if lst: out[f] = lst
    if tree:
        for f in [dec(x) for x in git("ls-files", "-z", "-o", "--exclude-standard").split(b"\0") if x]:
            if wanted(f):
                out[f] = list(range(1, dec(open(f, "rb").read()).count("\n") + 2))
    return out

def content(f, head, tree):
    t = open(f, "rb").read() if tree else git("show", head + ":" + f)
    return dec(t).replace("\r\n", "\n").split("\n")

def indent(s): return len(s) - len(s.lstrip())
def code(s): return s.split("//")[0]

def header_name(s):
    """(name, params) when the line is a method header (also an expression-bodied one), else None."""
    c = code(s).rstrip()
    if NOTKW.match(c) or not c.strip(): return None
    m = HEADER.match(c)
    if not m or "=" in c.split("(")[0]: return None
    return m.group(1), m.group(2)

def enclosing(lines, i):
    """(header index, name, params) of the method containing line i; i itself counts when it is a header."""
    h = header_name(lines[i])
    if h: return i, h[0], h[1]
    ind = indent(lines[i]); j = i
    while j > 0:
        j -= 1
        s = lines[j]
        if not s.strip() or s.strip().startswith(("//", "{", "}", "[")): continue
        if indent(s) < ind:
            ind = indent(s)
            h = header_name(s)
            if h: return j, h[0], h[1]
            if ind == 0: break
    return None, None, None

def body(lines, h):
    ind = indent(lines[h]); end = h + 1
    while end < len(lines) and (not lines[end].strip() or indent(lines[end]) > ind or lines[end].strip() in ("{", "}")):
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
            if header_name(lines[j]): return False
    return False

def declared_lists(lines):
    return set(re.findall(r"\b(?:List|Stack|Queue|Dictionary|HashSet|SortedList)<[^;=()]*>\s+(\w+)", "\n".join(lines)))

hits = 0
rows = []     # --all: (file, line, rule, method, per-frame)
cur = ["", False]
ALL = False
def hit(f, n, rule, fix):
    global hits; hits += 1
    if ALL: rows.append((f, n, rule, cur[0] or "-", cur[1]))
    else: print(f"{f}:{n}: {rule} -> {fix}")

def perframe_methods(lines):
    """Names of the methods in this file that are per-frame: hot by name, or called (by bare name, transitively) from one."""
    meths = {}
    for i, l in enumerate(lines):
        h = header_name(l)
        if h and not l.rstrip().endswith(";"): meths.setdefault(h[0], []).append(i)
    hot = {m for m in meths if HOT.match(m)}
    todo = list(hot)
    while todo:
        m = todo.pop()
        text = "\n".join(code(x) for i in meths[m] for x in body(lines, i))
        for o in meths:
            if o not in hot and re.search(r"(?<![\w])%s\s*(?:<[^>()]*>)?\s*\(" % re.escape(o), text) and o != m:
                hot.add(o); todo.append(o)
    return hot

def lint(f, ns, lines):
    isgame = f.startswith("game/")
    lists = declared_lists(lines)
    seen_render = set()
    pf = perframe_methods(lines) if ALL else set()
    for n in ns:
        if n - 1 >= len(lines): continue
        raw = lines[n - 1]; s = code(raw)
        if "perf-ok:" in raw or not s.strip() or s.strip().startswith(("*", "/*")): continue
        h, name, params = enclosing(lines, n - 1)
        hot = bool(name and HOT.match(name))
        cur[0] = name or ""; cur[1] = name in pf
        b = "\n".join(body(lines, h)) if h is not None else ""
        if isgame:
            lit = (re.search(r"(?<![\w])%s\s*(?:<[^>()]*>)?\s*\(\s*\"" % NAMEFN, s) or
                   re.search(r"(?<![\w.])(?:this\.)?(?:Get|Set|Call)\(\s*\"", s) or
                   re.search(r"\.(?:Call)\(\s*\"", s))
            if lit:
                hit(f, n, "string literal name passed to a Godot call (new StringName + wrapper each call)",
                    "a static readonly StringName/NodePath (see the Meta/PhysicsKeys tables)")
            if re.search(r"\b(?:StringName|NodePath|Variant)\s+\w+\s*=\s*\"", s) and not re.search(r"\b(static|const)\b", s):
                hit(f, n, "StringName/NodePath/Variant built from a literal outside a static field", "static readonly StringName/NodePath/Variant")
        if (re.search(r"new\s+(StringName|NodePath)\s*\(\s*\"[^\"]*\"\s*\)", s) or re.search(r"\b(StringName|NodePath)\s+\w+\s*=\s*new\s*\(\s*\"[^\"]*\"\s*\)", s)) \
                and not re.search(r"\b(static|const)\b", s):
            hit(f, n, "new StringName/NodePath outside a static field", "static readonly StringName/NodePath")
        if re.search(r"(?<![\w])GetChildren\s*\(", s) and not re.search(r"\b(?:override|virtual|abstract)\b|\b(?:public|private|protected|internal|static)\s+[\w<>\[\].]+\s+GetChildren\b", s):
            hit(f, n, "GetChildren() allocates a Godot array and a managed array", "foreach (var c in node.Kids()) or GetChildCount()/GetChild(i)")
        lq = [x for x in LINQ.finditer(s) if not re.search(r"\b(Mathf|Math|MathF|Mathd)\s*$", s[:x.start()])]
        if hot and lq:
            m = lq[0]
            recv = re.search(r"(\w+)\s*\.\s*(?:ToArray|ToList|Count|Any)\s*[(<]", s)
            if not (recv and recv.group(1) in lists and m.group(1) in ("ToArray", "Count")):
                hit(f, n, f"LINQ in {name} (allocates every call)", "an indexed loop over a reused list")
        for m in re.finditer(r"\.%s\s*\(" % PHYS, s):
            if "using" in s.split(m.group(0))[0] or re.match(r"\s*return\b", s): continue
            a = re.match(r"\s*(?:(?:var|[\w.<>,\[\]]+)\s+)?(\w+)\s*=\s*[\w.()\[\]]*$", s[:m.start()])
            if a:
                v = a.group(1)
                if re.search(r"\b%s\.Dispose\(\)|using\s*\(\s*%s\b|using\s+var\s+%s\b" % (v, v, v), b): continue
            hit(f, n, "physics query result never disposed", "`using var hit = ...` or hit.Dispose() after reading (the Dictionary/Array wrappers stay tracked)")
            break
        if re.search(r"\bnew\s+RDUniform\b|\bRDUniform\s+\w+\s*=\s*new\s*\(", s):
            a = re.match(r"\s*(?:using\s+)?(?:var|RDUniform)\s+(\w+)\s*=", s)
            ok = (re.search(r"\busing\b", s) is not None) if a and "using" in s else False
            if a and not ok:
                v = a.group(1); ok = re.search(r"\b%s\.Dispose\(\)|\breturn\s+%s\s*;" % (v, v), b) is not None
            elif not a: ok = re.search(r"\.Dispose\(\)", b) is not None
            if not ok:
                hit(f, n, "new RDUniform without Dispose (tracked until a GC finalizes it)", "Dispose after UniformSetCacheRD.GetCache copied it")
        if h is not None and name == "_RenderCallback" and h not in seen_render:
            seen_render.add(h)
            pm = re.search(r"RenderData\s+(\w+)", params or "")
            rd = pm.group(1) if pm else "renderData"
            if not re.search(r"using\s+var\s+\w+\s*=\s*%s\b|\b%s\.Dispose\(\)|using\s*\(\s*\w*\s*=?\s*%s\b" % (rd, rd, rd), b):
                hit(f, h + 1, "_RenderCallback does not dispose its RenderData (one wrapper per frame)", f"`using var data = {rd};` as the first line")
        if SHAPE.search(s) and (in_loop(lines, n - 1) or re.search(r"\b(foreach|for|while)\s*\(.*\)", s)):
            ctx = "\n".join(lines[max(0, n - 5):n])
            if "TryGetValue" not in ctx and "??=" not in ctx:
                hit(f, n, "new Shape3D inside a loop (a wrapper and engine object per entity of a repeated size)",
                    "share one shape per size (FieldBuilder.BoxOf pattern) when nothing mutates it")

def all_files():
    out = {}
    for f in [dec(x) for x in git("ls-files", "-z", "-co", "--exclude-standard", "--", "*.cs").split(b"\0") if x]:
        if wanted(f): out[f] = None
    return out

def main():
    global ALL
    a = sys.argv[1:]
    if "--all" in a:
        ALL = True
        for f in sorted(all_files()):
            lines = dec(open(f, "rb").read()).replace("\r\n", "\n").split("\n")
            lint(f, list(range(1, len(lines) + 1)), lines)
        rows.sort(key=lambda r: (not r[4], r[0], r[1]))
        print("file\tline\trule\tmethod\tper_frame")
        for r in rows: print(f"{r[0]}\t{r[1]}\t{r[2]}\t{r[3]}\t{'yes' if r[4] else 'no'}")
        return 0
    tree = "--tree" in a
    head = "HEAD"
    if "--head" in a: head = a[a.index("--head") + 1]; a = [x for x in a if x != head]
    pos = [x for x in a if not x.startswith("--")]
    base = pos[0] if pos else "origin/main"
    for f, ns in sorted(added(base, head, tree).items()):
        lint(f, ns, content(f, head, tree))
    print(f"perf-lint: {hits} hit(s)" if hits else "perf-lint: clean")
    return 1 if hits else 0

try:
    sys.exit(main())
except SystemExit: raise
except BaseException as e:
    print(f"perf-lint: internal error: {type(e).__name__}: {e}", file=sys.stderr)
    sys.exit(2)
