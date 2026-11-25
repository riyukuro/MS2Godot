using System.Text;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using Ms2.Ui.Shop;
using Ms2.Ui.WorldMap;
using Ms2.UiGen;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The translated UI code (<see cref="Translator"/>, built in memory from every movie of MS2_DATA) against the
/// interpreter: each flow runs once per engine and must leave the same trace. The trace is every AS method entered,
/// every native call with its receiver and arguments, trace() output, the runtime's errors and missing APIs, the
/// player's warnings and the display list at the end; the first line that differs is reported. Each translator
/// version runs as its own fixture (3: the boxed stack and register array; 4: registers and scope stack in locals). By default
/// only the shipped version (<see cref="UiGenerator.Version"/>) runs: every fixture loads its own build of all the
/// movies, which the process keeps, so two of them overrun the test memory cap. MS2_EQ_VERSIONS=3,4 runs both.
/// Both engines share the player runtime, so a change there that reorders what a flow does would pass here; for that the
/// interpreter's traces of one build are checked against another's: MS2_EQ_TRACE_OUT=dir writes them, MS2_EQ_TRACE_BASE=dir
/// (written by the build before the change) compares with them. The traces hold client data and stay out of the tree.
/// </summary>
[NonParallelizable]
[TestFixtureSource(nameof(Versions))]
public class As3EquivalenceTests
{
    public static IEnumerable<int> Versions =>
        (Environment.GetEnvironmentVariable("MS2_EQ_VERSIONS") is { Length: > 0 } v ? v.Split(',').Select(int.Parse) : new[] { UiGenerator.Version });
    readonly int version;
    public As3EquivalenceTests(int version) { this.version = version; }
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static List<string> keys;
    static readonly List<string> hashes = new();
    static UiGenerator.Result build;
    static readonly string TraceOut = Environment.GetEnvironmentVariable("MS2_EQ_TRACE_OUT");
    static readonly string TraceBase = Environment.GetEnvironmentVariable("MS2_EQ_TRACE_BASE");

    [OneTimeSetUp]
    public void Build()
    {
        // the translated bodies are registered for every movie: no parallel fixture may open one until they are removed again
        Ms2Godot.Tests.ExclusiveFixturesAttribute.Hold();
        try { Build1(); }
        catch { Ms2Godot.Tests.ExclusiveFixturesAttribute.Unhold(); throw; }
    }

    void Build1()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = TestData.Packs(d);
        var byKey = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => Path.GetFileNameWithoutExtension(e).ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        src = new MovieSource(k => byKey.TryGetValue(k.ToLowerInvariant(), out var e) ? packs.Read("Resource/Gfx", e) : null);
        keys = byKey.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        tables = new GameTables(packs, "en");
        var blocks = new List<Ms2.Swf.Abc.AbcFile>();
        var unread = new List<string>();
        foreach (var k in keys)
            try { foreach (var (_, abc) in src.Abc(k)) blocks.Add(abc); } catch (Exception e) { unread.Add($"{k} ({e.GetType().Name})"); }
        if (unread.Count > 0) TestContext.Progress.WriteLine($"{unread.Count} movies not read: {string.Join(", ", unread)}");
        Assert.That(blocks, Is.Not.Empty, "the movies' code was read");
        hashes.Clear();
        build = UiGenerator.LoadInMemory(blocks, version);
        hashes.AddRange(blocks.Select(b => b.Hash).Distinct());
        TestContext.Progress.WriteLine($"v{version}: translated {build.Blocks} blocks: {build.Bodies} bodies ({build.Distinct} distinct), {build.Skipped} left to the interpreter; " +
                                       $"translate {build.TranslateSeconds:F1} s, compile {build.CompileSeconds:F1} s; errors: {string.Join("; ", build.Errors.Take(3))}" +
                                       (build.Fallbacks.Count > 0 ? "; as version 3: " + string.Join(", ", build.Fallbacks.Select(f => $"{f.Key} {f.Value}")) : ""));
        Assert.That(build.Ok, Is.True, "the translated code compiles");
    }

    [OneTimeTearDown]
    public void Drop()
    {
        foreach (var h in hashes) GeneratedCode.Remove(h);
        NativeMethod.Observer = null; Interp.OnCall = null; Interp.UseOracle = false;
        build = null; tables = null; src = null; packs = null;
        GC.Collect();
        Ms2Godot.Tests.ExclusiveFixturesAttribute.Unhold();
    }

    // ---- the trace ---------------------------------------------------------------------------------------------

    /// <summary>A value without running AS code (no toString/valueOf): primitives as they are, objects by class and name.</summary>
    static string Desc(object v) => v switch
    {
        null => "null",
        Undefined => "undefined",
        string s => "\"" + (s.Length > 200 ? s[..200] + "..." : s) + "\"",
        double x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        uint u => u + "u",
        bool b => b ? "true" : "false",
        AsDisplayObject o => o.GetType().Name + ":" + (o.Class?.Name ?? "") + ":" + o.InstanceName,
        AsFunction f => "function",
        AsClass c => "class " + c.Name,
        AsObject o => "object " + (o.Class?.Name ?? o.GetType().Name),
        _ => v.GetType().Name,
    };

    static void Dump(StringBuilder sb, AsDisplayObject d, int depth)
    {
        var m = d.Matrix;
        sb.Append(' ', depth).Append(d.GetType().Name).Append(' ').Append(d.Class?.Name).Append(' ').Append(d.InstanceName).Append(' ').Append(d.Depth)
          .Append($" [{m.A:R} {m.B:R} {m.C:R} {m.D:R} {m.Tx:R} {m.Ty:R}] {d.Color} {d.IsVisible}");
        if (d is AsMovieClip mc) sb.Append(" f").Append(mc.currentFrame);
        if (d is AsTextField t) sb.Append(" \"").Append(t.Text).Append('"');
        sb.Append('\n');
        if (d is AsContainer c) foreach (var ch in c.Children) Dump(sb, ch, depth + 1);
    }

    /// <summary>Runs <paramref name="flow"/> in one engine and returns its trace and the AS calls it made.</summary>
    static (string Trace, long Calls, long Compiled) Run(bool interpreter, Func<StringBuilder, (Runtime Rt, Player P)> flow)
    {
        var sb = new StringBuilder();
        Interp.UseOracle = interpreter;
        NativeMethod.Observer = (nm, self, args) =>
        {
            sb.Append("N ").Append(nm.Name).Append(' ').Append(Desc(self)).Append('(');
            for (int i = 0; i < args.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Desc(args[i])); }
            sb.Append(")\n");
        };
        Interp.OnCall = n => sb.Append("A ").Append(n).Append('\n');
        long c0 = Interp.CompiledCalls, i0 = Interp.InterpretedCalls;
        (Runtime Rt, Player P) r;
        try { r = flow(sb); }
        finally { NativeMethod.Observer = null; Interp.OnCall = null; Interp.UseOracle = false; }
        long compiled = Interp.CompiledCalls - c0, calls = compiled + Interp.InterpretedCalls - i0;
        foreach (var e in r.Rt.Errors) sb.Append("E ").Append(e).Append('\n');
        foreach (var (k, n) in r.Rt.Missing.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append("M ").Append(k).Append(' ').Append(n).Append('\n');
        foreach (var (k, n) in r.P.Warnings.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append("W ").Append(k).Append(' ').Append(n).Append('\n');
        Dump(sb, r.P.Stage, 0);
        return (sb.ToString(), calls, compiled);
    }

    /// <summary>The first line where the two traces differ, with a few lines before it.</summary>
    static string FirstDifference(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        int n = Math.Min(la.Length, lb.Length), i = 0;
        while (i < n && la[i] == lb[i]) i++;
        if (i == n && la.Length == lb.Length) return null;
        var ctx = string.Join("\n", la.Skip(Math.Max(0, i - 4)).Take(Math.Min(4, i)));
        return $"line {i + 1}:\n{ctx}\n- interpreter: {(i < la.Length ? la[i] : "<end>")}\n- translated:  {(i < lb.Length ? lb[i] : "<end>")}";
    }

    /// <summary>With MS2_EQ_TRACE_OUT, the interpreter's trace of the flow is written; with MS2_EQ_TRACE_BASE, the difference
    /// from the trace another build wrote (null when the same; a flow the base build lacks is reported, not failed).</summary>
    static string AgainstBase(string what, string trace)
    {
        var file = string.Concat(what.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')) + ".txt";
        if (!string.IsNullOrEmpty(TraceOut)) { Directory.CreateDirectory(TraceOut); File.WriteAllText(Path.Combine(TraceOut, file), trace); }
        if (string.IsNullOrEmpty(TraceBase)) return null;
        var path = Path.Combine(TraceBase, file);
        if (!File.Exists(path)) { TestContext.WriteLine($"{what}: no base trace"); return null; }
        var d = FirstDifference(File.ReadAllText(path), trace);
        return d == null ? null : d.Replace("- interpreter:", "- base:").Replace("- translated: ", "- this build:");
    }

    static void Same(string what, Func<StringBuilder, (Runtime, Player)> flow, bool needsCompiled = true)
    {
        var a = Run(true, flow);
        var b = Run(false, flow);
        Assert.That(AgainstBase(what, a.Trace), Is.Null, what + ": against the base build's trace");
        Assert.That(FirstDifference(a.Trace, b.Trace), Is.Null, what);
        Assert.That(b.Calls, Is.EqualTo(a.Calls), what + ": AS calls");
        if (needsCompiled) Assert.That(b.Compiled, Is.GreaterThan(b.Calls / 2), what + ": the translated run ran translated code");
        TestContext.WriteLine($"{what}: {a.Calls} AS calls ({b.Compiled} translated), trace {a.Trace.Length / 1024} KB");
    }

    // ---- flows -------------------------------------------------------------------------------------------------

    static UiModel Model()
    {
        // a fixed server time: the upper HUD's clock would otherwise differ between two runs a minute apart
        var m = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f), HasLocalPlayer = true,
                                      ServerTime = () => new DateTime(2026, 1, 1, 12, 0, 0) };
        UiFixture.Apply(m);
        return m;
    }

    static ItemBook Book()
    {
        var b = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
        b.Apply(new ItemMessage.LoadTab(0, UiFixture.InventoryItems().Where(i => i.Id / 10000000 == 0).ToArray()));
        b.Apply(new ItemMessage.LoadTab(1, UiFixture.InventoryItems().Where(i => i.Id / 10000000 == 1).ToArray()));
        b.Apply(new ItemMessage.LoadTab(2, UiFixture.InventoryItems().Where(i => i.Id / 10000000 == 2).ToArray()));
        b.Apply(new ItemMessage.LoadTab(3, UiFixture.InventoryItems().Where(i => i.Id / 10000000 == 3).ToArray()));
        return b;
    }

    static FieldMeta meta; static WorldMapTable wmt; static Ms2.Formats.World.NpcTable npcs; static DungeonRooms rooms;

    sealed record Dlg(string Name, Func<UiController> Make, Action<UiController> Pre = null, Action<UiController> Post = null);

    static readonly Dictionary<string, Dlg> Dialogs = new Dlg[]
    {
        new("inventory", () => new InventoryController { View = new InventoryView(Book()) }),
        new("myinfo", () => { var me = UiFixture.Character(); return new MyInfoController { Record = () => me.ToPcInfo(0), StatPoints = me.StatPoints }; }),
        new("skill", () =>
        {
            var t = tables.Skills; var jobs = new Ms2.Ui.Skills.JobSystem(t);
            jobs.Read(1, 50, t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)));
            return new SkillBookTreeController { JobChange01 = true, Jobs = jobs, SkillTooltip = (d, id, lv) => new AsTooltip { ["tooltip"] = "export_common_tooltip_kindsSkill", ["skillID"] = id, ["level"] = lv } };
        }),
        new("worldmap", () =>
        {
            if (meta == null)
            {
                meta = FieldMeta.Load(packs); npcs = new Ms2.Formats.World.NpcTable(packs);
                rooms = DungeonRooms.Load(packs, e => FeatureElements.IsAvailable(packs, e));
                wmt = WorldMapTable.Load(packs, tables.MapName, id => meta.ParentFields.TryGetValue(id, out int p) ? p : null);
            }
            return new WorldMapController { Table = wmt, Meta = meta, FieldId = 2000001, PlayerName = "Fixture", PlayerLevel = 21, JobGroupId = 10, VisitedFields = new[] { 2000001u }, Npcs = npcs, Rooms = rooms };
        }, null, c => { var w = (WorldMapController)c; for (int i = 0; i < 400 && w.QueuedTiles > 0; i++) w.Process(); }),
        new("shop", () => new ShopController { Categories = new ShopCategoryTable(packs), Npcs = new Ms2.Formats.World.NpcTable(packs) },
            c => ((ShopController)c).ReadShopInfo(new ShopInfo { NpcId = 11000004, ShopId = 106, ProductCount = 2, TabShopId = 5, ResetEnable = true, ResetPrice = 1000, Icon = "" }),
            c => ((ShopController)c).ReadProducts(new[] {
                new ShopProduct { Serial = 1, ItemId = 11300001, Rarity = 1, Price = 1830, OriginalPrice = 1830, Count = 1, Categories = "CP" },
                new ShopProduct { Serial = 16, ItemId = 11400003, Rarity = 1, Price = 2360, OriginalPrice = 2360, Count = 1, Categories = "CL" } })),
        new("mainplayerinfo", () => new MainPlayerInfoController()),
        new("upperhud", () => new UpperHudController()),
    }.ToDictionary(d => d.Name);

    /// <summary>A dialog the way MovieDialog opens it (open, UiHost.Load, ticks), then the mouse over and clicking a grid of the stage.</summary>
    static (Runtime, Player) OpenDialog(Dlg d, StringBuilder sb, bool sweep)
    {
        var c = d.Make();
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); }, c.Libraries);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        rt.Trace = s => sb.Append("T ").Append(s).Append('\n');
        var h = new UiHost(p, c, Model());
        d.Pre?.Invoke(c);
        sb.Append("load ").Append(h.Load()).Append('\n');
        d.Post?.Invoke(c);
        for (int i = 0; i < 10; i++) p.Tick();
        if (sweep)
        {
            for (int y = 8; y < 720; y += 48)
                for (int x = 8; x < 1280; x += 48) { sb.Append("move ").Append(x).Append(',').Append(y).Append('\n'); p.MouseMove(x, y); }
            p.Tick();
            for (int y = 40; y < 720; y += 96)
                for (int x = 40; x < 1280; x += 96) { sb.Append("click ").Append(x).Append(',').Append(y).Append('\n'); p.Click(x, y); p.Tick(); }
        }
        foreach (var call in h.Calls) sb.Append("H ").Append(call).Append('\n');
        return (rt, p);
    }

    public static IEnumerable<string> DialogNames => Dialogs.Keys;

    [TestCaseSource(nameof(DialogNames))]
    public void DialogOpensTheSame(string name) => Same(name, sb => OpenDialog(Dialogs[name], sb, false));

    [TestCaseSource(nameof(DialogNames))]
    public void DialogSweepIsTheSame(string name) => Same(name + " sweep", sb => OpenDialog(Dialogs[name], sb, true));

    /// <summary>
    /// The HUD's idle frame allocates (almost) nothing in the player runtime: the upper HUD and the main player info open in
    /// the translated code, warmed up, then the bytes this thread allocates over 300 ticks. Untraced (the observers
    /// allocate). The bound is per tick.
    /// </summary>
    [Test]
    public void HudTickAllocatesLittle()
    {
        var open = new List<Player>();
        foreach (var name in new[] { "upperhud", "mainplayerinfo" })
        {
            var d = Dialogs[name];
            var c = d.Make();
            var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); }, c.Libraries);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => k.TrimStart('$');
            var h = new UiHost(p, c, Model());
            d.Pre?.Invoke(c);
            h.Load();
            d.Post?.Invoke(c);
            open.Add(p);
        }
        for (int i = 0; i < 60; i++) foreach (var p in open) p.Tick();
        const int ticks = 300;
        long c0 = Interp.CompiledCalls;
        int windows = 0;
        long perTick = Alloc.Least(() => { windows++; for (int i = 0; i < ticks; i++) foreach (var p in open) p.Tick(); }) / ticks;
        TestContext.WriteLine($"HUD idle: {perTick} bytes per tick, {(Interp.CompiledCalls - c0) / (double)(ticks * windows):F1} translated calls per tick");
        Assert.That(perTick, Is.LessThan(MaxHudTickBytes), "bytes allocated per HUD tick");
    }

    const long MaxHudTickBytes = 3200;   // 3041 B at this commit, for the two movies together (5073 B when the play-list sort ran every tick)

    /// <summary>Every movie with code: its document class and 5 frames (no C++ side), then a mouse sweep.</summary>
    [Test]
    public void EveryMovieStartsTheSame()
    {
        var failed = new List<string>();
        var unread = new List<string>();
        var noCode = new List<string>();
        int movies = 0;
        foreach (var key in keys)
        {
            List<(string, Ms2.Swf.Abc.AbcFile)> abcs;
            try { abcs = src.Abc(key); } catch (Exception e) { unread.Add($"{key} ({e.GetType().Name})"); continue; }
            if (abcs.Count == 0) continue;
            movies++;
            (Runtime, Player) Flow(StringBuilder sb)
            {
                var (rt, p) = src.Open(key, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
                p.StageW = 1280; p.StageH = 720;
                p.External = (n, a) => null;
                rt.Trace = s => sb.Append("T ").Append(s).Append('\n');
                sb.Append("start ").Append(p.Start()).Append('\n');
                for (int i = 0; i < 5; i++) p.Tick();
                for (int y = 20; y < 720; y += 100)
                    for (int x = 20; x < 1280; x += 100) { p.MouseMove(x, y); p.Click(x, y); }
                p.Tick();
                return (rt, p);
            }
            var a = Run(true, Flow); var b = Run(false, Flow);
            var diff = FirstDifference(a.Trace, b.Trace);
            if (diff != null || a.Calls != b.Calls) failed.Add($"{key}: {diff ?? $"AS calls {a.Calls} vs {b.Calls}"}");
            else if (AgainstBase("movie " + key, a.Trace) is { } bd) failed.Add($"{key} against the base build's trace: {bd}");
            // not vacuous: a movie that ran AS code ran translated code in the translated run
            else if (b.Calls > 0 && b.Compiled == 0) failed.Add($"{key}: {b.Calls} AS calls, none translated");
            else if (b.Calls == 0) noCode.Add(key);
        }
        TestContext.WriteLine($"{movies} movies, {failed.Count} differ; {unread.Count} not read: {string.Join(", ", unread)}; " +
                              $"{noCode.Count} ran no AS code: {string.Join(", ", noCode)}");
        Assert.That(movies, Is.GreaterThan(0), "movies with code");
        Assert.That(failed, Is.Empty, string.Join("\n\n", failed.Take(5)));
    }
}
