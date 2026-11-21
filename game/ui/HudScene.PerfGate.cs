using System.Text.Json.Nodes;
using Godot;
using Ms2Godot.Net;

namespace Ms2Godot.Ui;

/// <summary>
/// MS2_PERF_GATE=&lt;file.json&gt;: one session of the perf regression gate (tools/perfgate/run.sh). After the field is
/// loaded, on a fixed frame schedule (run with --fixed-fps, so every frame's delta is the same whatever its wall time):
/// <list type="bullet">
/// <item>the field load: wall time, bytes allocated (all threads), collections, engine object count and JIT-compiled
/// methods, from process start;</item>
/// <item>the field idle (no dialog open) and the HUD with three dialogs open: bytes (main thread and all threads) and
/// collections per frame, and the field effects' bytes per effect frame (<see cref="EffectInstance.Count"/>);</item>
/// <item>each of <see cref="GateDialogs"/>: the first open this session, then <see cref="GateRepeats"/> repeat opens; each
/// open's bytes, collections, JIT-compiled methods and time from just before its toggle to the end of the <see cref="GateOpenFrames"/>th
/// frame after it, the engine objects it added, and the objects still there 35 frames after its close.</item>
/// </list>
/// MS2_PERF_GATE_CASE names the session ("install": the first after setup; "session": a later one). The file is written
/// and the scene quits at the end.
/// </summary>
public partial class HudScene
{
    static readonly string GatePath = System.Environment.GetEnvironmentVariable("MS2_PERF_GATE") is { Length: > 0 } g ? g : null;
    static readonly string[] GateDialogs = { "inventory", "myinfo", "skill", "quest", "worldmap", "shop", "options" };
    const int GateRepeats = 3, GateOpenFrames = 3, GateSettle = 300, GateIdle = 300, GateQuiet = 15, GatePer = 100 + GateRepeats * 100;

    /// <summary>
    /// The upper HUD's clock in a gate session: a fixed time, as offline it is the local time and its once-a-minute redraw
    /// would land in whichever window the wall clock reaches the minute in.
    /// </summary>
    static readonly DateTime GateClock = new(2026, 1, 1, 12, 0, 0);

    int gateFrame = -1;
    readonly JsonObject gateOut = new();
    readonly JsonArray gateOpens = new();
    GateMark gateOpen, gateIdle;
    string gateIdleName;
    readonly List<double> gateFrameMs = new();
    long gateLastTick;

    /// <summary>The counters a measured window starts from.</summary>
    readonly record struct GateMark(long Main, long Total, int Gc0, int Gc1, int Gc2, double Objects, double Nodes, long Ticks,
        long FxBytes, long FxInstances, long FxTicks, long Jit, long JitTicks, long JitAll, long WrapGodot, long WrapOther)
    {
        public static GateMark Now() => new(GC.GetAllocatedBytesForCurrentThread(), GC.GetTotalAllocatedBytes(true),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            Performance.GetMonitor(Performance.Monitor.ObjectCount), Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            System.Diagnostics.Stopwatch.GetTimestamp(), EffectInstance.CountBytes, EffectInstance.CountInstances, EffectInstance.CountTicks,
            System.Runtime.JitInfo.GetCompiledMethodCount(true), System.Runtime.JitInfo.GetCompilationTime(true).Ticks,
            System.Runtime.JitInfo.GetCompiledMethodCount(false), GateWrap(0), GateWrap(1));

        public static double Ms(long from, long to) => (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    static JsonObject GateDelta(GateMark a, GateMark b) => new()
    {
        ["main_bytes"] = b.Main - a.Main,
        ["total_bytes"] = b.Total - a.Total,
        ["gc0"] = b.Gc0 - a.Gc0, ["gc1"] = b.Gc1 - a.Gc1, ["gc2"] = b.Gc2 - a.Gc2,
        ["objects"] = (long)(b.Objects - a.Objects),
        ["nodes"] = (long)(b.Nodes - a.Nodes),
        // methods the main thread compiled (the ones its frames waited for), and every thread's (tier-up included)
        ["jit_methods"] = b.Jit - a.Jit,
        ["jit_all_methods"] = b.JitAll - a.JitAll,
        ["jit_ms"] = Math.Round(TimeSpan.FromTicks(b.JitTicks - a.JitTicks).TotalMilliseconds, 3),
        ["ms"] = Math.Round(GateMark.Ms(a.Ticks, b.Ticks), 3),
        // wrappers the window left registered in the DisposablesTracker (Variant disposers, Godot.Collections, GodotObjects):
        // created and not yet finalized or disposed by its end; GateFinalized adds what the closing collection frees
        ["wrappers"] = b.WrapGodot + b.WrapOther - a.WrapGodot - a.WrapOther,
    };

    /// <summary>
    /// After a window's closing GateCollect: the wrappers it finalized (the tracker's entries lost since the window's end
    /// mark), by kind: GodotObjects, and the rest (Variant disposers, StringName, NodePath, Godot.Collections).
    /// Undisposed wrappers are exactly these: each one costs a finalizer run.
    /// </summary>
    static void GateFinalized(JsonObject o, GateMark end)
    {
        var after = GateMark.Now();
        o["finalized_godot"] = end.WrapGodot - after.WrapGodot;
        o["finalized_other"] = end.WrapOther - after.WrapOther;
    }

    /// <summary>The tracker's registered wrappers by type, for the window's per-type growth (a diagnostic, not checked).</summary>
    static Dictionary<Type, int> GateCensus()
    {
        var r = new Dictionary<Type, int>();
        if (gateTrackers[0] is System.Collections.Concurrent.ConcurrentDictionary<WeakReference<GodotObject>, byte> g)
            foreach (var kv in g) if (kv.Key.TryGetTarget(out var t)) r[t.GetType()] = r.GetValueOrDefault(t.GetType()) + 1;
        if (gateTrackers[1] is System.Collections.Concurrent.ConcurrentDictionary<WeakReference<IDisposable>, byte> d)
            foreach (var kv in d) if (kv.Key.TryGetTarget(out var t)) r[t.GetType()] = r.GetValueOrDefault(t.GetType()) + 1;
        return r;
    }

    static JsonObject GateGrowth(Dictionary<Type, int> a, Dictionary<Type, int> b)
    {
        var o = new JsonObject();
        foreach (var (ty, n) in b.Select(kv => (kv.Key, kv.Value - a.GetValueOrDefault(kv.Key))).Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).Take(12))
            o[ty.FullName] = n;
        return o;
    }

    static readonly System.Collections.ICollection[] gateTrackers = new System.Collections.ICollection[2];
    internal static long GateWrapTotal() => GateWrap(0) + GateWrap(1);
    static long GateWrap(int slot) => gateTrackers[slot]?.Count ?? 0;

    void GateStep()
    {
        gateFrame++;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (gateIdleName != null && gateLastTick != 0) gateFrameMs.Add(GateMark.Ms(gateLastTick, now));
        gateLastTick = now;
        if (gateChurn) GateChurn();
        if (gateFrame == 0)
        {
            EffectInstance.Count = true;
            var m = GateMark.Now();
            gateOut["case"] = System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_CASE") ?? "session";
            gateOut["field"] = new JsonObject
            {
                ["map"] = "02000001_tw_tria",   // the offline field hud.tscn loads without --field
                ["ms"] = Math.Round((DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds, 1),
                ["total_bytes"] = m.Total,
                ["gc0"] = m.Gc0, ["gc1"] = m.Gc1, ["gc2"] = m.Gc2,
                ["objects"] = (long)m.Objects,
                ["nodes"] = (long)m.Nodes,
                ["jit_methods"] = m.Jit,
                ["jit_all_methods"] = m.JitAll,
                ["jit_ms"] = Math.Round(TimeSpan.FromTicks(m.JitTicks).TotalMilliseconds, 3),
            };
            // the inputs' fingerprint (run.sh's MS2_PERF_GATE_INPUTS): compare refuses a run whose inputs are not the baseline's
            gateOut["inputs"] = System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_INPUTS") is { Length: > 0 } inputs ? JsonNode.Parse(inputs) : null;
            // after the field's numbers: they stay the player's. Without it the tracker's growth lands in a window, so the
            // session fails rather than measure that (no result file: run.sh exits 2)
            if (!GatePregrowTrackers()) { GD.PushError("perf gate: the DisposablesTracker pre-grow failed"); GetTree().Quit(2); }
            return;
        }
        int t = gateFrame - GateSettle;
        if (t == -GateQuiet) GateCollect();
        if (t < 0) return;
        if (t == 0) { GateIdleStart("idle"); if (System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_ATTRIB") == "1") WrapperAttrib.Install(GetTree().Root); }
        if (t == GateIdle) GateIdleEnd();   // its collection is also the first open's (GateQuiet frames before it)
        t -= GateIdle + GateQuiet;
        if (t < 0) return;
        int i = t / GatePer, s = t % GatePer;
        if (i < GateDialogs.Length)
        {
            string d = GateDialogs[i];
            int k = s / 100, r = s % 100;   // k = 0: the first open; 1..GateRepeats: repeats; each 100 frames: open, close at 50
            // each open starts GateQuiet frames after a collection (the previous open's kept-objects one, or the idle's):
            // the engine objects that collection's finalizers queued are freed by then, outside the window
            if (r == 0) { gateOpen = GateMark.Now(); PerfToggle(d, true); }
            else if (r == GateOpenFrames)
            {
                var end = GateMark.Now();
                var o = GateDelta(gateOpen, end);
                // the engine objects the open left alive: counted after a collection, so the wrappers a collection in
                // the window happened to finalize (or not) do not move the count
                GateCollect();
                GateFinalized(o, end);
                o["objects"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectCount) - gateOpen.Objects);
                o["nodes"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - gateOpen.Nodes);
                o["dialog"] = d;
                o["kind"] = k == 0 ? "first" : "repeat";
                gateOpens.Add(o);
            }
            else if (r == 50) PerfToggle(d, false);
            else if (r == 100 - GateQuiet)
            {
                GateCollect();
                ((JsonObject)gateOpens[^1])["kept_objects"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectCount) - gateOpen.Objects);
            }
            return;
        }
        t -= GateDialogs.Length * GatePer;
        if (t == 0) { ToggleInventory(); ToggleMyInfo(); ToggleSkillBook(); }
        else if (t == 100 - GateQuiet) GateCollect();
        else if (t == 100)
        {
            GateIdleStart("idle_3_open");
            // the window's own attrib numbers: Install resets them (the idle window before it, and each dialog open, added theirs)
            if (System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_ATTRIB") == "1") WrapperAttrib.Install(GetTree().Root);
        }
        else if (t == 100 + GateIdle) { GateIdleEnd(); CloseAllDialogs(); skillBook?.Close(); }
        // the live field: the per-frame HUD parts a connected field runs (damage numbers, npc HP bars, chat lines), standing
        // (idle_live) and then walking a fixed loop (move). Each window starts GateQuiet frames after a collection.
        else if (t == 100 + GateIdle + 30) GateCollect();
        else if (t == 100 + GateIdle + 30 + GateQuiet) GateLiveStart("idle_live", false);
        else if (t == 100 + GateIdle + 30 + GateQuiet + GateIdle) { GateLiveEnd(); GateIdleEnd(); }
        else if (t == 100 + GateIdle + 30 + GateQuiet + GateIdle + GateQuiet) GateLiveStart("move", true);
        else if (t == 100 + GateIdle + 30 + GateQuiet + GateIdle + GateQuiet + GateIdle) { GateLiveEnd(); GateIdleEnd(); }
        else if (t == 100 + GateIdle + 30 + GateQuiet + GateIdle + GateQuiet + GateIdle + 30)
        {
            gateOut["opens"] = gateOpens;
            System.IO.File.WriteAllText(GatePath, gateOut.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"perf gate: {GatePath}");
            GetTree().Quit(0);
        }
    }

    /// <summary>
    /// GodotSharp's DisposablesTracker holds every wrapper in a ConcurrentDictionary, whose growth rebuilds every entry
    /// (some 3 MB at the field's ~60000 wrappers). The count crosses the growth point once a session, in whichever of
    /// the first few opens it reaches it. The gate grows both tables up front, past twice their count (they never
    /// shrink), so that one-time engine cost lands outside every measured window.
    /// </summary>
    static bool GatePregrowTrackers()
    {
        var t = typeof(GodotObject).Assembly.GetType("Godot.DisposablesTracker");
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        bool Grow<T>(string name, int slot) where T : class
        {
            if (t?.GetProperty(name, F)?.GetValue(null) is not System.Collections.Concurrent.ConcurrentDictionary<WeakReference<T>, byte> d)
            {
                GD.PushError($"perf gate: no DisposablesTracker.{name}");
                return false;
            }
            var keys = new WeakReference<T>[d.Count + 1024];
            for (int i = 0; i < keys.Length; i++) d.TryAdd(keys[i] = new WeakReference<T>(null), 0);
            foreach (var k in keys) d.TryRemove(k, out _);
            gateTrackers[slot] = d;
            return true;
        }
        return Grow<GodotObject>("GodotObjectInstances", 0) & Grow<IDisposable>("OtherInstances", 1);
    }

    void GateIdleStart(string name)
    {
        gateIdleName = name;
        gateCensus = GateCensus();   // before the mark: its allocation is not the window's
        gateIdle = GateMark.Now();
        gateFrameMs.Clear();
    }

    Dictionary<Type, int> gateCensus;
    bool gateChurn, gateMove;
    int gateChurnFrame;
    Player gatePlayer;
    World.NpcPresence gatePresence;
    List<World.FieldNpcs.Placed> gateNear;
    static readonly string[] GateChat = { "Welcome to the field.", "Anyone up for a dungeon run?", "Selling gemstones, whisper me.", "Party at the plaza in five minutes!" };

    /// <summary>
    /// A window with the live field's per-frame parts on: a chat line every 30 frames from a fixed list, a damage number and an
    /// HP bar change on one of the 4 nearest npcs every 20, a hit on the own PC every 45 (the name tags are always on). With
    /// <paramref name="move"/> the player also walks a fixed loop (a second a side, scripted input: with --fixed-fps the
    /// path is the same every run).
    /// </summary>
    void GateLiveStart(string name, bool move)
    {
        GateCollect();   // what the closed dialogs and the quiet frames left (their nodes free a frame late) is not the window's
        gatePlayer ??= FindPlayer(this);
        gatePresence ??= GetTree().Root.FindChild("NpcPresence", true, false) as World.NpcPresence;
        if (gatePresence != null && gatePlayer != null)
        {
            gateNear = gatePresence.Npcs.OrderBy(n => n.Root.GlobalPosition.DistanceTo(gatePlayer.GlobalPosition)).Take(4).ToList();
            foreach (var n in gateNear) gatePresence.Stance(n, true, 1000, 1000);
        }
        gateNear ??= new();
        gateMove = move;
        gateChurnFrame = 0;
        if (move && gatePlayer != null) gatePlayer.ScriptDriven = true;
        GateCollect();   // the setup's own wrappers (FindPlayer, FindChild, the sort, Stance) are not the window's
        GateIdleStart(name);
        gateChurn = true;
        if (System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_ATTRIB") == "1") WrapperAttrib.Install(GetTree().Root);
    }

    void GateLiveEnd()
    {
        gateChurn = false;
        if (gatePlayer != null) { gatePlayer.Input2 = Vector2.Zero; gatePlayer.ScriptDriven = false; }
    }

    void GateChurn()
    {
        int f = gateChurnFrame++;
        if (gateMove && gatePlayer != null)
            gatePlayer.Input2 = (f / 60 % 4) switch { 0 => new Vector2(0, 1), 1 => new Vector2(1, 0), 2 => new Vector2(0, -1), _ => new Vector2(-1, 0) };
        if (f % 30 == 0) ChatLog.AddSystemLine(GateChat[f / 30 % GateChat.Length], Ms2.Ui.Hud.TalkKind.SystemNotice);
        if (f % 20 == 5 && gateNear.Count > 0 && gatePresence != null)
        {
            var n = gateNear[f / 20 % gateNear.Count];
            gatePresence.Damaged(n, true);
            gatePresence.Stat(n, 1000 - f % 900, 1000);
            damageText.OnNpc(n, 100 + f, 0);
        }
        if (f % 45 == 10) damageText.OnPlayer(50 + f, 0);
    }

    /// <summary>
    /// A full collection with the finalizers run: every measured window starts from no garbage (so its collections are
    /// its own allocation's, not the ones before it), and the object counts are of live objects.
    /// </summary>
    static void GateCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    void GateIdleEnd()
    {
        var m = GateMark.Now();
        var o = GateDelta(gateIdle, m);
        if (System.Environment.GetEnvironmentVariable("MS2_PERF_GATE_ATTRIB") == "1")
        {
            o["attrib"] = WrapperAttrib.Result();
            o["attrib_us"] = WrapperAttrib.ResultUs();
            o["attrib_bytes"] = WrapperAttrib.ResultBytes();
            o["attrib_markers"] = WrapperAttrib.ResultMarkers();
        }
        o["wrapper_types"] = GateGrowth(gateCensus, GateCensus());   // after the mark; the collection below follows
        GateCollect();
        GateFinalized(o, m);
        o["objects"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectCount) - gateIdle.Objects);
        o["nodes"] = (long)(Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - gateIdle.Nodes);
        o["frames"] = GateIdle;
        long fxInst = m.FxInstances - gateIdle.FxInstances;
        o["fx_effect_frames"] = fxInst;
        o["fx_bytes"] = m.FxBytes - gateIdle.FxBytes;
        o["fx_us_per_effect"] = fxInst == 0 ? 0 : Math.Round(GateMark.Ms(gateIdle.FxTicks, m.FxTicks) * 1000 / fxInst, 3);
        gateFrameMs.Sort();
        o["frame_ms_median"] = gateFrameMs.Count == 0 ? 0 : Math.Round(gateFrameMs[gateFrameMs.Count / 2], 3);
        o["frame_ms_worst"] = gateFrameMs.Count == 0 ? 0 : Math.Round(gateFrameMs[^1], 3);
        gateOut[gateIdleName] = o;
        gateIdleName = null;
    }
}
