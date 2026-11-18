using System.Diagnostics;
using System.Text;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;

// As3Run: runs UI movies' own ActionScript 3 headless (Ms2.Avm2 interpreter + flash.* runtime).
//   As3Run run <movie> [--frames N] [--trace] [--tree]   one movie: errors, missing APIs, display tree
//   As3Run scan [--frames N] [--out FILE] [--only SUBSTR] every movie: construct the document class, run N frames, coverage report
string data = Environment.GetEnvironmentVariable("MS2_DATA");
int frames = 60; bool trace = false, tree = false; string outFile = null, only = null;
var pos = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data": data = args[++i]; break;
        case "--frames": frames = int.Parse(args[++i]); break;
        case "--trace": trace = true; break;
        case "--tree": tree = true; break;
        case "--out": outFile = args[++i]; break;
        case "--only": only = args[++i]; break;
        default: pos.Add(args[i]); break;
    }
}
if (pos.Count == 0 || string.IsNullOrEmpty(data)) { Console.Error.WriteLine("usage: As3Run run <movie> | scan  [--frames N] (MS2_DATA)"); return 2; }
var packs = new PackSet(data);
var gfx = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).ToList();
var byKey = gfx.GroupBy(e => Path.GetFileNameWithoutExtension(e).ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
var src = new MovieSource(k => byKey.TryGetValue(k.ToLowerInvariant(), out var e) ? packs.Read("Resource/Gfx", e) : null);

(Runtime, Player, bool) RunMovie(string key, Action<string> traceOut)
{
    var (rt, p) = src.Open(key, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
    if (traceOut != null) rt.Trace = traceOut;
    p.External = (n, a) => { traceOut?.Invoke($"[ExternalInterface] {n}({string.Join(", ", a.Select(Rt.Describe))})"); return null; };
    p.ReadText = url => null;
    bool ok = p.Start();
    for (int f = 0; f < frames; f++) p.Tick();
    return (rt, p, ok);
}

switch (pos[0])
{
    case "run":
    {
        var (rt, p, ok) = RunMovie(pos[1], trace ? s => Console.WriteLine(s) : null);
        Console.WriteLine($"root: {p.Root?.Class?.Name} constructed={ok} frames={p.FrameCount}");
        foreach (var e in rt.Errors.Take(40)) Console.WriteLine("ERROR " + e);
        foreach (var (k, n) in rt.Missing.OrderByDescending(x => x.Value).Take(60)) Console.WriteLine($"MISSING {k} x{n}");
        if (tree && p.Stage != null) Dump(p.Stage, 0);
        return 0;
    }
    case "login":
    {
        // the decomp's UILoginDialog: Tab → Invoke("onTabKey"), Enter → Invoke("onEnterKey"); ExternalInterface OnConnect → read m_inputID / m_inputPW
        var (rt, p) = src.Open("uilogindialog", r => r.Budget = 20_000_000);
        string connected = null;
        AsDisplayObject dlg = null;
        p.External = (n, a) =>
        {
            Console.WriteLine($"[bridge] {n}({string.Join(", ", a.Select(Rt.Describe))})");
            if (a.Length > 0 && Rt.ToString(a[0]) == "OnConnect")
                connected = Rt.ToString(p.RT.GetProperty(p.RT.GetProperty(dlg, Name.Public("m_inputID")), Name.Public("text"))) + "/" +
                            Rt.ToString(p.RT.GetProperty(p.RT.GetProperty(dlg, Name.Public("m_inputPW")), Name.Public("text")));
            return null;
        };
        p.Start(); for (int i = 0; i < 5; i++) p.Tick();
        dlg = p.FindOwner("onEnterKey");
        Console.WriteLine($"dialog {dlg?.Class.Name} at {dlg?.GetBounds(null)} (stage {p.StageW}x{p.StageH})");
        var idField = (AsDisplayObject)p.RT.GetProperty(p.RT.GetProperty(dlg, Name.Public("m_inputID")), Name.Public("textField"));
        var b = idField.GetBounds(null);
        if (Environment.GetEnvironmentVariable("AS3_CALLS") is { } pat) Interp.OnCall = n => { if (n.Contains(pat)) Console.WriteLine("  call " + n); };
        p.Click(b.X + b.W / 2, b.Y + b.H / 2); p.Tick();
        Interp.OnCall = null;
        Console.WriteLine($"focus after click: {(p.Focus as AsDisplayObject)?.Parent?.InstanceName}.{(p.Focus as AsDisplayObject)?.InstanceName}");
        // select all and type over (ctrl-a is not a flash key; set selection like the dialog's initData does)
        if (p.Focus is AsTextField ft) ft.setSelection(0, ft.Text.Length);
        foreach (var ch in "tester") p.Key(char.ToUpperInvariant(ch), ch, true); p.Tick();
        foreach (var er in p.RT.Errors) Console.WriteLine("  err: " + er);
        Console.WriteLine($"before Tab: mouseChildren={p.RT.GetProperty(dlg, Name.Public("mouseChildren"))} id.focused={p.RT.GetProperty(p.RT.GetProperty(dlg, Name.Public("m_inputID")), Name.Public("focused"))}");
        p.Call(dlg, "onTabKey"); p.Tick();
        Console.WriteLine($"focus after Tab: {(p.Focus as AsDisplayObject)?.Parent?.InstanceName}.{(p.Focus as AsDisplayObject)?.InstanceName}");
        foreach (var ch in "secret") p.Key(char.ToUpperInvariant(ch), ch, true); p.Tick();
        p.Call(dlg, "onEnterKey"); p.Tick();
        Console.WriteLine($"OnConnect read id/pw = {connected ?? "(not reached)"}");
        foreach (var e in rt.Errors.Take(10)) Console.WriteLine("ERROR " + e);
        foreach (var (k, n) in rt.Missing) Console.WriteLine($"MISSING {k} x{n}");
        return connected == "tester/secret" ? 0 : 1;
    }
    case "minimap":
    {
        // the decomp's UIMinimapDialog: m_30->Invoke("createEntity", {mcname, frame, layer, x, y, visible, showtext, name, ...});
        // the movie's own createEntity sets hitArea = m_hitarea and hides it (what game/ui/MinimapEntities.cs did by hand)
        var (rt, p) = src.Open("uiminimapdialog", r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        p.Start(); for (int i = 0; i < 5; i++) p.Tick();
        var dlg = p.FindOwner("createEntity");
        if (p.FindOwner("startOpen") is { } op) { rt.SetProperty(op, Name.Public("visible"), Rt.True); p.Call(op, "startOpen"); for (int i = 0; i < 30; i++) p.Tick(); }
        Console.WriteLine($"dialog {dlg?.Class.Name} visible={dlg?.IsVisible}");
        int fails = 0;
        if (args.Contains("--hud"))
        {
            p.Call(dlg, "setFieldName", "Tria");
            p.Call(dlg, "createFieldEntity", 0, "map/minimap/02000001.dds");
            for (int i = 0; i < 120; i++) { p.Tick(); if (!dlg.IsVisible) { Console.WriteLine($"hidden at frame {i}"); break; } }
            Console.WriteLine($"after 120 frames visible={dlg.IsVisible} bounds={dlg.GetBounds(null)}");
        }
        foreach (var kind in new[] { "portal", "friendly", "other", "enemy" })
        {
            var ed = p.NewObject(("mcname", kind), ("frame", kind), ("layer", kind), ("x", 120), ("y", 80), ("visible", true), ("showtext", false), ("showguide", false), ("name", "test " + kind));
            var e = p.Call(dlg, "createEntity", ed) as AsDisplayObject;
            p.Tick();
            if (e == null) { Console.WriteLine($"{kind}: no entity (class missing in the movie)"); continue; }
            var hit = rt.GetProperty(e, Name.Public("m_hitarea")) as AsDisplayObject;
            var ha = (e as AsSprite)?.hitArea;
            var b = e.GetBounds(null);
            var top = p.TopMost(b.X + b.W / 2, b.Y + b.H / 2, true);
            bool ok = hit == null || (ReferenceEquals(ha, hit) && !hit.IsVisible);
            if (!ok) fails++;
            Console.WriteLine($"{kind}: {e.Class.Name} parent={e.Parent?.InstanceName} bounds={b} m_hitarea={(hit == null ? "none" : $"visible={hit.IsVisible} isHitArea={ReferenceEquals(ha, hit)}")} topmost={top?.InstanceName ?? top?.Class.Name} {(ok ? "OK" : "FAIL")}");
        }
        if (args.Contains("--tree")) Dump(p.Stage, 0);
        foreach (var er in rt.Errors) Console.WriteLine("ERROR " + er);
        return fails == 0 && rt.Errors.Count == 0 ? 0 : 1;
    }
    case "expbar":
    {
        // the decomp's UIExpBar: m_30->Invoke("setExp", fCur%, fRest%, text, flags)
        var (rt, p) = src.Open("uiexpbar", r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        p.Start(); for (int i = 0; i < 5; i++) p.Tick();
        var dlg = p.FindOwner("setExp");
        Console.WriteLine($"dialog {dlg?.Class.Name} stage {p.StageW}x{p.StageH}");
        if (p.FindOwner("alignment") is { } al) p.Call(al, "alignment");
        p.Call(dlg, "setExp", 42.5, 10.0, "42.50%", 3); for (int i = 0; i < 30; i++) p.Tick();
        if (args.Contains("--tree")) Dump(dlg, 0);
        foreach (var er in rt.Errors) Console.WriteLine("ERROR " + er);
        return rt.Errors.Count == 0 ? 0 : 1;
    }
    case "find":
    {
        // which movies export a linkage class name (substring match)
        foreach (var k in byKey.Keys.OrderBy(k => k))
        {
            System.Text.Json.Nodes.JsonObject lay;
            try { lay = src.Layout(k); } catch { continue; }
            if (lay?["symbols"] is System.Text.Json.Nodes.JsonObject sy)
                foreach (var (id, sn) in sy)
                    foreach (var f in new[] { "class", "export" })
                        if ((string)sn?[f] is string cn && cn.Contains(pos[1])) Console.WriteLine($"{k}\t{f}\t{cn}\t#{id}");
        }
        return 0;
    }
    case "scan":
    {
        var keys = byKey.Keys.Where(k => only == null || k.Contains(only)).OrderBy(k => k).ToList();
        int clean = 0, constructed = 0, total = 0, noCode = 0;
        var missingMovies = new Dictionary<string, HashSet<string>>();
        var errorKinds = new Dictionary<string, HashSet<string>>();
        var rows = new StringBuilder();
        var sw = Stopwatch.StartNew();
        foreach (var k in keys)
        {
            total++;
            Runtime rt; Player p; bool ok;
            var t0 = sw.ElapsedMilliseconds;
            try { (rt, p, ok) = RunMovie(k, null); }
            catch (Exception e) { rows.AppendLine($"{k}\tLOADFAIL\t{e.GetType().Name}: {e.Message}"); errorKinds.GetOrAdd("load: " + e.GetType().Name).Add(k); continue; }
            bool hasCode = src.Abc(k).Count > 0 || p.Layout["documentClass"] != null;
            if (!hasCode) noCode++;
            if (ok) constructed++;
            bool isClean = ok && rt.Errors.Count == 0 && rt.Missing.Keys.All(m => m.StartsWith("data: "));
            if (isClean) clean++;
            foreach (var m in rt.Missing.Keys) missingMovies.GetOrAdd(m).Add(k);
            foreach (var e in rt.Errors) errorKinds.GetOrAdd(Shorten(e)).Add(k);
            rows.AppendLine($"{k}\t{(isClean ? "CLEAN" : ok ? "RAN" : "FAILED")}\terrors={rt.Errors.Count}\tmissing={rt.Missing.Count}\tms={sw.ElapsedMilliseconds - t0}\t{string.Join(" | ", rt.Errors.Take(2))}");
            Console.Error.Write($"\r{total}/{keys.Count} clean={clean}   ");
        }
        var rep = new StringBuilder();
        rep.AppendLine($"movies {total}, document class constructed {constructed}, clean (no error, no missing API in {frames} frames) {clean}, movies without code {noCode}, {sw.Elapsed.TotalSeconds:0}s");
        rep.AppendLine();
        rep.AppendLine("## Missing APIs (by movies affected)");
        foreach (var (m, s) in missingMovies.OrderByDescending(x => x.Value.Count).Take(80)) rep.AppendLine($"{s.Count,4}  {m}");
        rep.AppendLine();
        rep.AppendLine("## Runtime errors (by movies affected)");
        foreach (var (m, s) in errorKinds.OrderByDescending(x => x.Value.Count).Take(60)) rep.AppendLine($"{s.Count,4}  {m}   e.g. {string.Join(", ", s.Take(3))}");
        rep.AppendLine();
        rep.AppendLine("## Per movie");
        rep.Append(rows);
        if (outFile != null) File.WriteAllText(outFile, rep.ToString());
        Console.Error.WriteLine();
        Console.WriteLine(rep.ToString().Split('\n').Take(60).Aggregate(new StringBuilder(), (b, l) => b.AppendLine(l)));
        return 0;
    }
}
return 2;

static string Shorten(string e)
{
    var s = System.Text.RegularExpressions.Regex.Replace(e, @"\d+", "N");
    return s.Length > 160 ? s[..160] : s;
}

static void Dump(AsDisplayObject o, int depth)
{
    if (depth > 12) return;
    var b = o.GetBounds(null);
    var extra = o switch { AsTextField t => $" text=\"{Trunc(t.Text)}\"", AsMovieClip mc => $" frame={mc.currentFrame}/{mc.totalFrames} scope={mc.Scope?.Key} labels={string.Join(",", (mc.TL?.Labels ?? new()).Take(6).Select(l => l.Name))}", _ => "" };
    Console.WriteLine($"{new string(' ', depth * 2)}{o.Class.Name} '{o.InstanceName}' vis={o.IsVisible} {b}{extra}{(o.HitTestDisabled ? " hitDisabled" : "")}{(o.Blend != "normal" ? " blend=" + o.Blend : "")}{(o.Filters != null ? " filters" : "")}");
    if (o is AsContainer c) foreach (var ch in c.Children) Dump(ch, depth + 1);
}

static string Trunc(string s) => s.Length > 40 ? s[..40] + "…" : s.Replace("\r", "\\r");

static class Ext
{
    public static HashSet<string> GetOrAdd(this Dictionary<string, HashSet<string>> d, string k) { if (!d.TryGetValue(k, out var s)) d[k] = s = new(); return s; }
}
