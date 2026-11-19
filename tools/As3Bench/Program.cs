using System.Diagnostics;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using Ms2.Ui.Shop;
using Ms2.Ui.WorldMap;

// As3Bench: headless warm opens of UI dialogs (the game's MovieDialog path without the Godot view): src.Open, UiHost.Load,
// first ticks. Times each phase; run under a sampling profiler for the breakdown.
//   As3Bench [--gen DLL] [--n N] [--only a,b] [--loop SECONDS]   (MS2_DATA)
string data = Environment.GetEnvironmentVariable("MS2_DATA");
int n = 15; string only = null; double loop = 0; bool micro = false, count = false;
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--gen": Console.Error.WriteLine($"compiled bodies: {GeneratedCode.LoadAssembly(args[++i])} blocks"); break;
        case "--n": n = int.Parse(args[++i]); break;
        case "--only": only = args[++i]; break;
        case "--loop": loop = double.Parse(args[++i]); break;
        case "--interp": Interp.UseOracle = true; break;
        case "--micro": micro = true; break;
        case "--count": count = true; break;
    }
var packs = new PackSet(data);
var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
var tables = new GameTables(packs, "en");
UiModel Model() { var m = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f), HasLocalPlayer = true }; m.Player.Level = 1; return m; }

ItemBook Book()
{
    var b = new ItemBook { TabOf = id => id / 10000000, BaseSlots = _ => 48 };
    b.Apply(new ItemMessage.LoadTab(0, new[] { new EquipItem { Id = 1, Uid = 1, InventorySlot = 0, Amount = 1 } }));
    b.Apply(new ItemMessage.LoadTab(3, new[] { new EquipItem { Id = 30000001, Uid = 3, InventorySlot = 0, Amount = 7 } }));
    return b;
}
FieldMeta meta = null; WorldMapTable wmt = null; Ms2.Formats.World.NpcTable npcs = null; DungeonRooms rooms = null;
var dialogs = new List<Dlg>
{
    new Dlg("inventory", () => new InventoryController { View = new InventoryView(Book()) }, null, null),
    new Dlg("myinfo", () => { var me = UiFixture.Character(); return new MyInfoController { Record = () => me.ToPcInfo(0), StatPoints = me.StatPoints }; }, null, null),
    new Dlg("skill", () =>
    {
        var t = tables.Skills; var jobs = new Ms2.Ui.Skills.JobSystem(t);
        jobs.Read(1, 50, t.Jobs.GetJobData(50).Skills.Select(r => new BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)));
        return new SkillBookTreeController { JobChange01 = true, Jobs = jobs, SkillTooltip = (d, id, lv) => new AsTooltip { ["tooltip"] = "export_common_tooltip_kindsSkill", ["skillID"] = id, ["level"] = lv } };
    }, null, null),
    new Dlg("worldmap", () =>
    {
        if (meta == null)
        {
            meta = FieldMeta.Load(packs); npcs = new Ms2.Formats.World.NpcTable(packs);
            rooms = DungeonRooms.Load(packs, e => FeatureElements.IsAvailable(packs, e));
            wmt = WorldMapTable.Load(packs, tables.MapName, id => meta.ParentFields.TryGetValue(id, out int p) ? p : null);
        }
        return new WorldMapController { Table = wmt, Meta = meta, FieldId = 2000001, PlayerName = "Tester", PlayerLevel = 1, JobGroupId = 50, VisitedFields = new[] { 2000001u }, Npcs = npcs, Rooms = rooms };
    }, null, c => { var w = (WorldMapController)c; for (int i = 0; i < 400 && w.QueuedTiles > 0; i++) w.Process(); }),
    new Dlg("shop", () => new ShopController { Categories = new ShopCategoryTable(packs), Npcs = new Ms2.Formats.World.NpcTable(packs) },
        c => ((ShopController)c).ReadShopInfo(new ShopInfo { NpcId = 11000004, ShopId = 106, ProductCount = 3, TabShopId = 5, ResetEnable = true, ResetPrice = 1000, Icon = "" }),
        c => ((ShopController)c).ReadProducts(new[] {
            new ShopProduct { Serial = 1, ItemId = 11300001, Rarity = 1, Price = 1830, OriginalPrice = 1830, Count = 1, Categories = "CP" },
            new ShopProduct { Serial = 16, ItemId = 11400003, Rarity = 1, Price = 2360, OriginalPrice = 2360, Count = 1, Categories = "CL" } })),
    new Dlg("mainplayerinfo", () => new MainPlayerInfoController(), null, null),
    new Dlg("upperhud", () => new UpperHudController(), null, null),
};
if (micro)
{
    var (rt, p) = src.Open(new UpperHudController().Movie, r => r.Budget = 200_000_000);
    var o = (Ms2.Avm2.Flash.AsDisplayObject)rt.GetClass("flash.display.Sprite").Construct();
    var bx = o.Traits.FindPublic("x"); var nx = Name.Public("x"); var none = Array.Empty<object>(); var one = new object[] { Rt.Num(5) };
    void T(string what, Action a) { for (int i = 0; i < 200_000; i++) a(); var sw = Stopwatch.StartNew(); const int N = 3_000_000; for (int i = 0; i < N; i++) a(); Console.WriteLine($"{what,-40} {sw.Elapsed.TotalMilliseconds * 1e6 / N,7:0.0} ns"); }
    double sink = 0;
    T("C# o.x get", () => sink += o.x);
    T("native getter Invoke (reflection)", () => sink += Rt.ToNumber(bx.Getter.Invoke(o, none)));
    T("rt.GetProperty(o, x)", () => sink += Rt.ToNumber(rt.GetProperty(o, nx)));
    T("native setter Invoke (reflection)", () => bx.Setter.Invoke(o, one));
    T("rt.SetProperty(o, x, 5)", () => rt.SetProperty(o, nx, one[0]));
    T("C# o.x = 5", () => o.x = 5);
    T("TraitTable.Find(x)", () => sink += o.Traits.Find(nx).Slot);
    T("Rt.Num(d) box", () => sink += Rt.Num(sink) is double ? 1 : 0);
    var mi = typeof(Ms2.Avm2.Flash.AsDisplayObject).GetProperty("x").GetGetMethod();
    T("MethodInfo.Invoke bare", () => sink += (double)mi.Invoke(o, null));
    var sw2 = Stopwatch.StartNew(); for (int i = 0; i < 20; i++) src.Open(new UpperHudController().Movie, r => r.Budget = 200_000_000); Console.WriteLine($"MovieSource.Open upperhud {sw2.Elapsed.TotalMilliseconds / 20:0.00} ms");
    Console.WriteLine(sink > 0 ? "" : " ");
    return 0;
}
if (only != null) { var set = only.Split(','); dialogs = dialogs.Where(d => set.Contains(d.Name)).ToList(); }

var phases = new[] { "open", "load", "tick1", "ticks" };
(double[] T, int Errors, long Calls, long Alloc, double Pause) OneOpen(Dlg d)
{
    var c = d.Make();
    long a0 = GC.GetAllocatedBytesForCurrentThread(); var p0 = GC.GetTotalPauseDuration();
    var t = new double[4];
    long calls0 = Interp.CompiledCalls + Interp.InterpretedCalls;
    var sw = Stopwatch.StartNew();
    var (rt, p) = src.Open(c.Movie, r => r.Budget = 200_000_000, c.Libraries);
    p.StageW = 1280; p.StageH = 720;
    t[0] = sw.Elapsed.TotalMilliseconds; sw.Restart();
    var h = new UiHost(p, c, Model());
    d.Pre?.Invoke(c);
    h.Load();
    d.Post?.Invoke(c);
    t[1] = sw.Elapsed.TotalMilliseconds; sw.Restart();
    p.Tick();
    t[2] = sw.Elapsed.TotalMilliseconds; sw.Restart();
    for (int i = 0; i < 9; i++) p.Tick();
    t[3] = sw.Elapsed.TotalMilliseconds;
    return (t, rt.Errors.Count, Interp.CompiledCalls + Interp.InterpretedCalls - calls0, GC.GetAllocatedBytesForCurrentThread() - a0, (GC.GetTotalPauseDuration() - p0).TotalMilliseconds);
}

#if COUNTERS
if (count)
{
    foreach (var d in dialogs)
    {
        OneOpen(d); ProfCounters.Reset(); ProfCounters.On = true; var r = OneOpen(d); ProfCounters.On = false;
        Console.WriteLine($"== {d.Name}: AS calls {r.Calls}  get {ProfCounters.Get} set {ProfCounters.Set} call {ProfCounters.Call} findprop {ProfCounters.FindProp} getlex {ProfCounters.GetLex} locals {ProfCounters.Locals} reflect-natives {ProfCounters.Native} lambda-natives {ProfCounters.Lambda}");
        void Top(string what, Dictionary<string, long> m, int k) => Console.WriteLine($"  {what}: " + string.Join(", ", m.OrderByDescending(x => x.Value).Take(k).Select(x => $"{x.Key} {x.Value}")));
        Top("natives", ProfCounters.Natives, 25); Top("AS methods", ProfCounters.Methods, 15); Top("names", ProfCounters.Names, 25);
    }
    return 0;
}
#endif
foreach (var d in dialogs)
{
    var cold = OneOpen(d);
    var runs = new List<double[]>();
    long calls = 0; int errs = 0; var allocs = new List<long>(); var pauses = new List<double>();
    var until = Stopwatch.StartNew();
    for (int i = 0; i < n || until.Elapsed.TotalSeconds < loop; i++) { var r = OneOpen(d); runs.Add(r.T); calls = r.Calls; errs = r.Errors; allocs.Add(r.Alloc); pauses.Add(r.Pause); }
    // median per phase of the last half (tiered JIT settled)
    var tail = runs.Skip(runs.Count / 2).ToList();
    double Median(int k) { var v = tail.Select(x => x[k]).OrderBy(x => x).ToList(); return v[v.Count / 2]; }
    double total = tail.Select(x => x.Sum()).OrderBy(x => x).ElementAt(tail.Count / 2);
    if (Environment.GetEnvironmentVariable("BENCH_ALL") == "1") Console.WriteLine(d.Name + " runs: " + string.Join(" ", runs.Select(x => x.Sum().ToString("0"))) + " | gc0 " + GC.CollectionCount(0) + " gc1 " + GC.CollectionCount(1) + " gc2 " + GC.CollectionCount(2) + " server " + System.Runtime.GCSettings.IsServerGC + " heap " + (GC.GetGCMemoryInfo().HeapSizeBytes / 1e6).ToString("0") + " MB");
    double Min(int k) => runs.Skip(runs.Count / 3).Min(x => x[k]);
    if (Environment.GetEnvironmentVariable("BENCH_MIN") == "1") { var tt = runs.Skip(runs.Count / 3).Select(x => x.Sum()).OrderBy(x => x).ToList(); Console.WriteLine($"{d.Name,-15} min {tt[0],6:0.0} med {tt[tt.Count / 2],6:0.0}  open {Min(0):0.0} load {Min(1):0.0} tick1 {Min(2):0.0} ticks {Min(3):0.0}  alloc {allocs.Skip(allocs.Count/2).Average()/1e6:0.0} MB"); continue; }
    Console.WriteLine($"{d.Name,-15} cold {cold.T.Sum(),7:0.0}  warm {total,6:0.0} ms  [{string.Join(" ", phases.Select((ph, k) => $"{ph} {Median(k):0.0}"))}]  AS calls {calls}  errors {errs}  runs {runs.Count}  alloc {allocs.Skip(allocs.Count/2).Average()/1e6:0.0} MB  gc pause {pauses.Skip(pauses.Count/2).Average():0.0} ms");
}
return 0;

record Dlg(string Name, Func<UiController> Make, Action<UiController> Pre, Action<UiController> Post);
