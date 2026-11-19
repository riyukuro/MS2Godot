using System.Diagnostics;
using System.Text;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;

// As3Run: runs UI movies' own ActionScript 3 headless (Ms2.Avm2 interpreter + flash.* runtime).
//   As3Run run <movie> [--frames N] [--trace] [--tree]   one movie: errors, missing APIs, display tree
//   As3Run drive [--frames N] [--after N] [--max-objects N] [--out FILE] [--only SUBSTR]  every movie driven by input: pointer, text, keys
//   As3Run scan [--frames N] [--out FILE] [--only SUBSTR] every movie: construct the document class, run N frames, coverage report
string data = Environment.GetEnvironmentVariable("MS2_DATA");
bool hostMode = true; int frames = 60, after = 30, maxObjects = 200; bool trace = false, tree = false; string outFile = null, only = null;
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
        case "--gen": Console.Error.WriteLine($"compiled bodies: {GeneratedCode.LoadAssembly(args[++i])} blocks"); break;
        case "--nohost": hostMode = false; break;
        case "--after": after = int.Parse(args[++i]); break;
        case "--max-objects": maxObjects = int.Parse(args[++i]); break;
        case "--oracle": Interp.UseOracle = true; break;
        default: pos.Add(args[i]); break;
    }
}
if (pos.Count == 0 || string.IsNullOrEmpty(data)) { Console.Error.WriteLine("usage: As3Run run <movie> | scan  [--frames N] (MS2_DATA)"); return 2; }
var packs = new PackSet(data);
var gfx = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).ToList();
var byKey = gfx.GroupBy(e => Path.GetFileNameWithoutExtension(e).ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
var src = new MovieSource(k => byKey.TryGetValue(k.ToLowerInvariant(), out var e) ? packs.Read("Resource/Gfx", e) : null);

// What each controller loads beside its movie (UiController.Libraries), by movie key.
var libraries = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var t in typeof(Ms2.Ui.UiController).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(Ms2.Ui.UiController).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null))
{
    try { var c = (Ms2.Ui.UiController)Activator.CreateInstance(t); if (c.Libraries != null) libraries[c.Movie] = c.Libraries.ToList(); }
    catch (Exception) { }
}

// The host's statics (UiHost.InstallShared) answering "no data": the movie reaches them through MS2DataModel, DataBinding and
// GameDataModel, which the client implements natively. They are the host's, not runtime gaps.
void InstallHostStatics(Runtime rt)
{
    void Static(string cls, params string[] names)
    {
        if (rt.GetClass(cls) is not { } c) return;
        foreach (var n in names) rt.SetProperty(c, Name.Public(n), new AsPlainFunction(rt, new NativeMethod(cls + "." + n, (s, a) => null)));
    }
    Static("scaleform.ms2.Data.MS2DataModel", "getUserData", "playSound");
    Static("scaleform.clik.data.DataBinding", "createBindingImpl", "deleteBindingImpl");
    Static("com.scaleform.mmo.GameDataModel", "notifySlotUse", "notifySlotShiftClick", "notifySlotDragStart", "notifySlotDragEnd", "getSlotTooltip", "getBagSlotBindings");
}

(Runtime, Player, bool) RunMovie(string key, Action<string> traceOut, int[] hostErrors = null, List<string> hostList = null)
{
    var (rt, p) = src.Open(key, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); },
        hostMode && libraries.TryGetValue(key, out var libs) ? libs : null);
    if (traceOut != null) rt.Trace = traceOut;
    p.External = (n, a) => { traceOut?.Invoke($"[ExternalInterface] {n}({string.Join(", ", a.Select(Rt.Describe))})"); return null; };
    p.ReadText = url => null;
    bool ok = p.Start();
    if (hostMode && ok)
    {
        InstallHostStatics(rt);
        // the controller's load call with nothing to show: initData({}) (an empty object, as a fresh account would give)
        if (p.FindOwner("initData") is { } owner)
        {
            int before = rt.Errors.Count;
            rt.Guard("initData", () => p.Call(owner, "initData", p.NewObject()));
            // errors an empty initData raises are the host's missing data (the movie reads a field the server would fill), kept apart
            if (hostErrors != null) hostErrors[0] = rt.Errors.Count - before;
            hostList?.AddRange(rt.Errors.Skip(before));
            if (rt.Errors.Count > before) rt.Errors.RemoveRange(before, rt.Errors.Count - before);
        }
    }
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
    case "popupmenu":
    {
        // the decomp's UIPopUpMenu::Open (uipartydialog.cpp:2503): m_30->Invoke("open", {type, menu, posx, posy, name, ..., jobcode})
        var (rt, p) = src.Open("uipopupmenu", r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        p.Start(); for (int i = 0; i < 5; i++) p.Tick();
        var dlg = p.FindOwner("open");
        var item = p.NewObject(("id", 0x66), ("label", "View Character"), ("enable", true));
        var o = p.NewObject(("type", int.Parse(Environment.GetEnvironmentVariable("PMTYPE") ?? "1")), ("posx", 300.0), ("posy", 200.0), ("name", "FixtureArcher"), ("dirx", 1.0), ("diry", 1),
            ("level", 23), ("imgpath", ""), ("trophy", 0), ("jobcode", int.Parse(Environment.GetEnvironmentVariable("JOBCODE") ?? "500")));
        o.SetDynamic("menu", new AsArray(rt, new object[] { item }));
        p.Call(dlg, "open", o); for (int i = 0; i < 10; i++) p.Tick();
        if (args.Contains("--tree")) Dump(dlg, 0);
        foreach (var er in rt.Errors) Console.WriteLine("ERROR " + er);
        return rt.Errors.Count == 0 ? 0 : 1;
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
    case "questlog":
    {
        // the quest log driven by its client controller (UIQuestDialog) over the QuestLog tree, the same data the hand
        // port draws: the list's renderers and the scroll bar after setList, then after scrolling (questlog TAB SUBTAB SCROLL)
        int tab = pos.Count > 1 ? int.Parse(pos[1]) : 1, sub = pos.Count > 2 ? int.Parse(pos[2]) : 1, scroll = pos.Count > 3 ? int.Parse(pos[3]) : 3;
        var langs = new Ms2.Formats.World.GameStrings(packs, "en").Languages;
        var table = Ms2.Quest.QuestTable.Load(packs, langs, "KR", _ => true);
        var meta = Ms2.Quest.FieldMeta.Load(packs);
        var fd = System.Xml.Linq.XDocument.Load(new MemoryStream(packs.Read("Xml", "table/fielddata.xml")));
        var codes = Ms2.Quest.QuestTable.FieldCodes(fd, table.Available);
        Ms2.Quest.IQuestState qstate = pos.Count > 5
            ? new Ms2.Quest.QuestShotState(table, int.Parse(pos[5].Split(':')[0]), pos[5].Split(':')[1].Split(',').Select(int.Parse))
            : new Ms2.Quest.QuestShotState(table, 30);
        var log = new Ms2.Quest.QuestLog(table, qstate) { Meta = meta, FieldCodes = codes };
        var c = new Ms2.Ui.Hud.QuestDialogController { Log = log, OpenTab = tab, OpenSubTab = sub };
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var model = new Ms2.Ui.UiModel(new Ms2.Ui.GameTables(packs, "en"));
        Ms2.Ui.UiFixture.Apply(model);
        model.FeatureEnabledByName = f => Ms2.Ui.LobbyFeatures.IsEnabled(packs, f);
        var h = new Ms2.Ui.UiHost(p, c, model);
        Console.WriteLine($"mode {(Interp.UseOracle ? "oracle" : GeneratedCode.Count > 0 ? "compiled" : "interpreter")} load={h.Load()} tab={c.GetTab()} sub={c.GetSubTab()}");
        for (int i = 0; i < 30; i++) p.Tick();
        var tabs = rt.GetProperty(h.Child("m_tab"), Name.Public("dataProvider"));
        Console.WriteLine($"tabs: {Rt.Describe(rt.GetProperty(tabs, Name.Public("length")))}");
        var list = (AsDisplayObject)h.Child("m_questList");
        void Show(string when)
        {
            Console.WriteLine($"-- {when}: scrollPosition={Rt.Describe(rt.GetProperty(list, Name.Public("scrollPosition")))} " +
                              $"length={Rt.Describe(rt.GetProperty(rt.GetProperty(list, Name.Public("dataProvider")), Name.Public("length")))}");
            var sb = new StringBuilder();
            DumpTo(sb, list, 0);
            Console.Write(sb);
        }
        Show("after setList");
        h.Set(list, "scrollPosition", scroll);
        for (int i = 0; i < 5; i++) p.Tick();
        Show($"after scrollPosition={scroll}");
        if (pos.Count > 4)
        {
            // questlog TAB SUBTAB SCROLL SELECT [LEVEL:ID,ID..]: the detail pane of a selected quest (SelectQuest)
            table.Words.FeatureEnabled = id => id >= 1 && id <= Ms2.Ui.FeatureTable.Names.Length && Ms2.Ui.LobbyFeatures.IsEnabledByName(packs, Ms2.Ui.FeatureTable.Names[id - 1]);
            var detail = Ms2.Quest.QuestDetail.Select(log, int.Parse(pos[4]), c.GetSubTabFilter(), c.Detail);
            foreach (var (k, v) in detail?.Members ?? new()) Console.WriteLine($"setData {k} = {(v is Ms2.Quest.QuestInfoObject o ? string.Join(", ", o.Members.Select(m => $"{m.Name}={m.Value}")) : v)}");
            c.SelectQuest(int.Parse(pos[4]));
            for (int i = 0; i < 10; i++) p.Tick();
            var sbi = new StringBuilder();
            DumpTo(sbi, (AsDisplayObject)h.Child("m_questInfo"), 0);
            Console.WriteLine($"-- detail of {pos[4]}");
            Console.Write(sbi);
        }
        foreach (var e in rt.Errors.Take(10)) Console.WriteLine("ERROR " + e);
        foreach (var (k, n) in rt.Missing.Take(20)) Console.WriteLine($"MISSING {k} x{n}");
        // the hand port's rows over the same tree (QuestLogTree.RequestItemRange, as QuestDialog draws them)
        log.Tab = tab; log.SubTab = sub;
        var root = log.Build();
        var qtree = new Ms2.Quest.QuestLogTree(root);
        Console.WriteLine($"-- hand port rows: length={qtree.Length}");
        int row = 0;
        foreach (var n in qtree.Range(0, qtree.Length - 1)) Console.WriteLine($"  {row++} {n?.Renderer} '{n?.Label}{n?.Code}{n?.Field}' type={n?.Type}");
        return 0;
    }
    case "parity":
    {
        // every movie twice: the interpreter (oracle) and the compiled bodies (--gen); same errors and display tree?
        var keys = byKey.Keys.Where(k => only == null || k.Contains(only)).OrderBy(k => k).ToList();
        int same = 0, total = 0;
        var rows = new StringBuilder();
        double tOracle = 0, tGen = 0;
        foreach (var k in keys)
        {
            total++;
            string Snap(bool oracle, out double secs)
            {
                Interp.UseOracle = oracle;
                var t = Stopwatch.StartNew();
                var sb = new StringBuilder();
                try
                {
                    var (rt, p, ok) = RunMovie(k, null);
                    secs = t.Elapsed.TotalSeconds;
                    sb.AppendLine($"constructed={ok} frames={p.FrameCount}");
                    foreach (var e in rt.Errors) sb.AppendLine("ERROR " + e);
                    foreach (var (m, n) in rt.Missing.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.AppendLine($"MISSING {m} x{n}");
                    if (p.Stage != null) DumpTo(sb, p.Stage, 0);
                }
                catch (Exception e) { secs = t.Elapsed.TotalSeconds; sb.AppendLine("LOADFAIL " + e.GetType().Name + ": " + e.Message); }
                return sb.ToString();
            }
            var a = Snap(true, out var ta); var b = Snap(false, out var tb);
            tOracle += ta; tGen += tb;
            if (a == b) { same++; rows.AppendLine($"{k}	SAME	{ta:F2}s	{tb:F2}s"); }
            else
            {
                var la = a.Split('\n'); var lb = b.Split('\n');
                int d = 0; while (d < la.Length && d < lb.Length && la[d] == lb[d]) d++;
                rows.AppendLine($"{k}	DIFF	line {d}	oracle: {(d < la.Length ? la[d] : "<end>")}	gen: {(d < lb.Length ? lb[d] : "<end>")}");
            }
            Console.Error.Write($"\r{total}/{keys.Count} same={same}   ");
        }
        Interp.UseOracle = false;
        var head = $"movies {total}, same errors+missing+display tree after {frames} frames: {same}; time oracle {tOracle:F0}s, compiled {tGen:F0}s; calls compiled {Interp.CompiledCalls}, interpreted {Interp.InterpretedCalls}";
        if (outFile != null) File.WriteAllText(outFile, head + "\n\n" + rows);
        Console.Error.WriteLine();
        Console.WriteLine(head);
        foreach (var l in rows.ToString().Split('\n').Where(l => l.Contains("\tDIFF")).Take(30)) Console.WriteLine(l);
        return 0;
    }
    case "where":
    {
        // where <name>...: the movies that link each symbol name (SymbolClass / ExportAssets), and the movies that import them
        var want = pos.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase); bool prefix = want.Remove("--prefix");
        var imports = new Dictionary<string, List<string>>();
        foreach (var k in byKey.Keys.OrderBy(k => k))
        {
            var m = src.Movie(k);
            if (m == null) continue;
            imports[k] = m.Imports.Select(g => Path.GetFileNameWithoutExtension(g.Url)).ToList();
            foreach (var n in m.SymbolClasses.Values.Concat(m.Exports.Values).Where(x => prefix ? want.Any(w => x.StartsWith(w, StringComparison.OrdinalIgnoreCase)) : want.Contains(x)).Distinct())
                Console.WriteLine($"{n}\tlinked by {k}");
        }
        foreach (var n in want) foreach (var (k, l) in imports) foreach (var i in l) { if (src.Movie(i)?.SymbolClasses.Values.Concat(src.Movie(i).Exports.Values).Contains(n) == true) Console.WriteLine($"{n}\t{k} imports {i}"); }
        return 0;
    }
    case "drive":
    {
        // The walk of `scan` only runs timelines; this one acts on the movie as the player does: the pointer over, down, up and out
        // of every enabled interactive object (real pointer when the object is the hit target, the events dispatched on it when
        // something covers it), then focus and keys in every input field. `after` frames run after each action.
        var keys = byKey.Keys.Where(k => only == null || k.Contains(only)).OrderBy(k => k).ToList();
        var actionCount = new Dictionary<string, long>();
        var errByKind = new Dictionary<string, (HashSet<string> Movies, long Hits, HashSet<string> Actions)>();
        var missByKey = new Dictionary<string, (HashSet<string> Movies, long Hits, HashSet<string> Actions)>();
        var rows = new StringBuilder(); int ran = 0, driven = 0, failed = 0, objectsTotal = 0, capped = 0;
        var sw = Stopwatch.StartNew();
        void Note(Dictionary<string, (HashSet<string> Movies, long Hits, HashSet<string> Actions)> d, string kind, string movie, string action, long n)
        {
            if (!d.TryGetValue(kind, out var v)) d[kind] = v = (new HashSet<string>(), 0, new HashSet<string>());
            v.Movies.Add(movie); v.Actions.Add(action); d[kind] = (v.Movies, v.Hits + n, v.Actions);
        }
        foreach (var k in keys)
        {
            ran++;
            Runtime rt; Player p; bool ok;
            try { (rt, p, ok) = RunMovie(k, null); }
            catch (Exception e) { rows.AppendLine($"{k}\tLOADFAIL\t{e.GetType().Name}"); failed++; continue; }
            if (!ok) { failed++; continue; }
            driven++;
            p.StageW = 1280; p.StageH = 720;
            int e0 = rt.Errors.Count; var m0 = new Dictionary<string, int>(rt.Missing);
            long acts = 0;
            void Action(string kind, Action act)
            {
                int eb = rt.Errors.Count; var mb = new Dictionary<string, int>(rt.Missing);
                actionCount[kind] = actionCount.GetValueOrDefault(kind) + 1; acts++;
                rt.Guard("drive " + kind, act);
                for (int f = 0; f < after; f++) p.Tick();
                for (int i = eb; i < rt.Errors.Count; i++) Note(errByKind, Shorten(rt.Errors[i]), k, kind, 1);
                foreach (var (mk, n) in rt.Missing) { int d = n - mb.GetValueOrDefault(mk); if (d > 0) Note(missByKey, mk, k, kind, d); }
            }
            var objs = new List<AsInteractiveObject>();
            void Collect(AsDisplayObject o)
            {
                if (o is AsInteractiveObject io && io is not AsStage && io.mouseEnabled && o.IsVisible && o.OnStage) objs.Add(io);
                if (o is AsContainer c) foreach (var ch in c.Children) Collect(ch);
            }
            Collect(p.Stage);
            objectsTotal += objs.Count;
            if (objs.Count > maxObjects) { capped++; objs = objs.Take(maxObjects).ToList(); }
            var mouseCls = rt.GetClass("scaleform.gfx.MouseEventEx") ?? rt.GetClass("flash.events.MouseEvent");
            void Send(AsInteractiveObject o, string type)
            {
                // the event the player builds for a real pointer (Player.MouseEv): the movie's MouseEventEx, left button
                var e = (AsMouseEvent)mouseCls.Construct(type);
                e.bubbles = type is not ("rollOver" or "rollOut"); e.cancelable = false; e.buttonDown = type == "mouseDown";
                if (mouseCls.Name.EndsWith("MouseEventEx"))
                {
                    rt.SetProperty(e, Name.Public("mouseIdx"), Rt.Box(0));
                    rt.SetProperty(e, Name.Public("buttonIdx"), Rt.Box(0));
                }
                o.Dispatch(e);
            }
            foreach (var o in objs)
            {
                if (!o.OnStage) continue;
                var b = o.GetBounds(null);
                if (b.IsEmpty) continue;
                double cx = b.X + b.W / 2, cy = b.Y + b.H / 2;
                if (p.MouseTarget(cx, cy) == o)
                {
                    Action("pointer-over", () => p.MouseMove(cx, cy));
                    Action("pointer-down", () => p.MouseButton(true));
                    Action("pointer-up", () => p.MouseButton(false));
                    Action("pointer-out", () => p.MouseMove(-100, -100));
                }
                else
                    foreach (var t in new[] { "mouseOver", "rollOver", "mouseDown", "mouseUp", "click", "mouseOut", "rollOut" })
                        Action("dispatch-" + t, () => Send(o, t));
            }
            // text: focus every input field, type, edit, enter
            var fields = new List<AsTextField>();
            void CollectText(AsDisplayObject o) { if (o is AsTextField tf && tf.type == "input" && o.OnStage) fields.Add(tf); if (o is AsContainer c) foreach (var ch in c.Children) CollectText(ch); }
            CollectText(p.Stage);
            foreach (var tf in fields)
            {
                if (!tf.OnStage) continue;
                Action("text-focus", () => p.SetFocus(tf, "mouse"));
                Action("text-type", () => { foreach (var ch in "Ab1 ") p.Key(char.ToUpperInvariant(ch), ch, true); });
                Action("text-key-backspace", () => { p.Key(8, 8, true); p.Key(8, 8, false); });
                Action("text-key-enter", () => { p.Key(13, 13, true); p.Key(13, 13, false); });
                Action("text-key-tab", () => { p.Key(9, 9, true); p.Key(9, 9, false); });
            }
            rows.AppendLine($"{k}\tobjects={objs.Count}\tfields={fields.Count}\tactions={acts}\terrors={rt.Errors.Count - e0}\tmissing={rt.Missing.Count - m0.Count}");
            Console.Error.Write($"\r{ran}/{keys.Count} {sw.Elapsed.TotalSeconds:0}s   ");
        }
        var rep = new StringBuilder();
        rep.AppendLine($"movies {ran}, driven {driven}, not constructed {failed}, interactive objects {objectsTotal} ({capped} movies over the {maxObjects}-object cap), {after} frames after each action, {sw.Elapsed.TotalSeconds:0}s");
        rep.AppendLine($"actions {actionCount.Values.Sum()}: " + string.Join(", ", actionCount.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));
        rep.AppendLine($"new runtime errors {errByKind.Values.Sum(v => v.Hits)} hits, {errByKind.Count} kinds; new missing APIs {missByKey.Count} distinct, {missByKey.Values.Sum(v => v.Hits)} hits");
        rep.AppendLine();
        rep.AppendLine("## Runtime errors (movies, hits, actions)");
        foreach (var (m, v) in errByKind.OrderByDescending(x => x.Value.Movies.Count).ThenByDescending(x => x.Value.Hits)) rep.AppendLine($"{v.Movies.Count,4} {v.Hits,7}  {m}   [{string.Join(",", v.Actions.Take(4))}] e.g. {string.Join(", ", v.Movies.Take(3))}");
        rep.AppendLine();
        rep.AppendLine("## Missing APIs (movies, hits, actions)");
        foreach (var (m, v) in missByKey.OrderByDescending(x => x.Value.Movies.Count).ThenByDescending(x => x.Value.Hits)) rep.AppendLine($"{v.Movies.Count,4} {v.Hits,7}  {m}   [{string.Join(",", v.Actions.Take(4))}] e.g. {string.Join(", ", v.Movies.Take(3))}");
        rep.AppendLine();
        rep.AppendLine("## Per movie");
        rep.Append(rows);
        if (outFile != null) File.WriteAllText(outFile, rep.ToString());
        Console.Error.WriteLine();
        Console.WriteLine(rep.ToString().Split('\n').Take(70).Aggregate(new StringBuilder(), (b, l) => b.AppendLine(l)));
        return 0;
    }
    case "scan":
    {
        var keys = byKey.Keys.Where(k => only == null || k.Contains(only)).OrderBy(k => k).ToList();
        int clean = 0, constructed = 0, total = 0, noCode = 0;
        var missingMovies = new Dictionary<string, HashSet<string>>();
        var missingHits = new Dictionary<string, long>();
        long totalErrors = 0, totalMissingHits = 0; int hostEntryErrors = 0, hostEntryMovies = 0; var hostLines = new StringBuilder();
        var perMovie = new List<(string Key, int Errors, int Missing, long Hits)>();
        var errorKinds = new Dictionary<string, HashSet<string>>();
        var rows = new StringBuilder();
        var sw = Stopwatch.StartNew();
        foreach (var k in keys)
        {
            total++;
            Runtime rt; Player p; bool ok;
            var t0 = sw.ElapsedMilliseconds;
            var he = new int[1]; var hl = new List<string>();
            try { (rt, p, ok) = RunMovie(k, null, he, hl); }
            catch (Exception e) { rows.AppendLine($"{k}\tLOADFAIL\t{e.GetType().Name}: {e.Message}"); errorKinds.GetOrAdd("load: " + e.GetType().Name).Add(k); continue; }
            bool hasCode = src.Abc(k).Count > 0 || p.Layout["documentClass"] != null;
            if (!hasCode) noCode++;
            if (ok) constructed++;
            bool isClean = ok && rt.Errors.Count == 0 && rt.Missing.Keys.All(m => m.StartsWith("data: "));
            if (isClean) clean++;
            if (he[0] > 0) { hostEntryErrors += he[0]; hostEntryMovies++; foreach (var e in hl) hostLines.AppendLine($"{k}\t{e}"); }
            foreach (var m in rt.Missing.Keys) missingMovies.GetOrAdd(m).Add(k);
            foreach (var (m, n) in rt.Missing) { missingHits[m] = missingHits.GetValueOrDefault(m) + n; totalMissingHits += n; }
            totalErrors += rt.Errors.Count;
            perMovie.Add((k, rt.Errors.Count, rt.Missing.Count, rt.Missing.Values.Sum(v => (long)v)));
            foreach (var e in rt.Errors) errorKinds.GetOrAdd(Shorten(e)).Add(k);
            rows.AppendLine($"{k}\t{(isClean ? "CLEAN" : ok ? "RAN" : "FAILED")}\terrors={rt.Errors.Count}\tmissing={rt.Missing.Count}\tms={sw.ElapsedMilliseconds - t0}\t{string.Join(" | ", rt.Errors.Take(2))}");
            Console.Error.Write($"\r{total}/{keys.Count} clean={clean}   ");
        }
        var rep = new StringBuilder();
        rep.AppendLine($"movies {total}, document class constructed {constructed}, clean (no error, no missing API in {frames} frames) {clean}, movies without code {noCode}, {sw.Elapsed.TotalSeconds:0}s");
        rep.AppendLine("(Missing entries from initData stay in the counts below.)");
        rep.AppendLine($"errors {totalErrors} in {perMovie.Count(x => x.Errors > 0)} movies; missing APIs {missingHits.Count} distinct, {totalMissingHits} hits, in {perMovie.Count(x => x.Missing > 0)} movies");
        if (hostMode) rep.AppendLine($"host entry (initData with an empty object): {hostEntryErrors} errors in {hostEntryMovies} movies, not counted above (the movie reads data the server fills)");
        if (hostLines.Length > 0) { rep.AppendLine("## Host-entry errors"); rep.Append(hostLines); }
        rep.AppendLine();
        rep.AppendLine("## Movies with the most errors + missing APIs (errors, distinct missing, missing hits)");
        foreach (var x in perMovie.OrderByDescending(x => x.Errors + x.Missing).ThenByDescending(x => x.Hits).Take(10)) rep.AppendLine($"{x.Key}\t{x.Errors}\t{x.Missing}\t{x.Hits}");
        rep.AppendLine();
        rep.AppendLine("## Missing APIs by hits (hits, movies)");
        foreach (var (m, n) in missingHits.OrderByDescending(x => x.Value).Take(20)) rep.AppendLine($"{n,8}  {missingMovies[m].Count,4}  {m}");
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

static void DumpTo(StringBuilder sb, AsDisplayObject o, int depth)
{
    if (depth > 12) return;
    var b = o.GetBounds(null);
    var extra = o switch { AsTextField t => $" text=\"{Trunc(t.Text)}\"", AsMovieClip mc => $" frame={mc.currentFrame}/{mc.totalFrames}", _ => "" };
    sb.AppendLine($"{new string(' ', depth * 2)}{o.Class.Name} '{o.InstanceName}' vis={o.IsVisible} {b}{extra}");
    if (o is AsContainer c) foreach (var ch in c.Children) DumpTo(sb, ch, depth + 1);
}

static string Trunc(string s) => s.Length > 40 ? s[..40] + "…" : s.Replace("\r", "\\r");

static class Ext
{
    public static HashSet<string> GetOrAdd(this Dictionary<string, HashSet<string>> d, string k) { if (!d.TryGetValue(k, out var s)) d[k] = s = new(); return s; }
}
