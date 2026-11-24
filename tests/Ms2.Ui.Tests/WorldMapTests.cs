using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.WorldMap;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The world map: WorldMapManager's tile order (synthetic tiles) and uiworldmapdialog run by its own ActionScript
/// with UIWorldMapDialog's C++ side (needs MS2_DATA).</summary>
[NonParallelizable]   // sets UiHost.Tooltips, which every UiHost movie reads
public class WorldMapTests
{
    static WorldMapTable.Tile T(int type, int x, int y, int z, int size = 1) => new() { TileType = type, X = x, Y = y, Z = z, Size = size };

    [Test]
    public void TilesSortByTypeThenHeightThenFrontToBack()
    {
        var a = T(0, 0, 0, 1);      // higher
        var b = T(0, 5, 5, 0);      // further along the turned y: first
        var c = T(0, 0, 0, 0);
        var d = T(1, 9, 9, 0);      // the sky fortress after every map tile
        var list = new List<WorldMapTable.Tile> { d, a, c, b };
        WorldMapTable.Sort(list, WorldMapTable.TileLess);
        Assert.That(list, Is.EqualTo(new[] { b, c, a, d }));
    }

    static PackSet packs;
    static MovieSource src;
    static WorldMapTable table;
    static FieldMeta meta;
    static Ms2.Formats.World.NpcTable npcs;
    static Ms2.Quest.DungeonRooms rooms;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            meta = FieldMeta.Load(packs);
            npcs = new Ms2.Formats.World.NpcTable(packs);
            rooms = Ms2.Quest.DungeonRooms.Load(packs, e => FeatureElements.IsAvailable(packs, e));
            var names = new GameTables(packs, "en");
            table = WorldMapTable.Load(packs, names.MapName, id => meta.ParentFields.TryGetValue(id, out int p) ? p : null);
        }
        return packs;
    }

    static UiModel NewModel() => new(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };

    const uint Tria = 2000001;

    [Test]
    public void TriaHasATileAndHenesysIsFound()
    {
        Packs();
        var tile = table.FindTile(Tria);
        Assert.That(tile, Is.Not.Null);
        Assert.That(table.WorldTiles(tile.World).Count(), Is.GreaterThan(50));
        var henesys = table.FindSearch("Henesys");
        Assert.That(henesys, Is.Not.Null);
        Assert.That(table.FindTile(henesys.MappedId)?.World, Is.EqualTo(tile.World), "Henesys is on Tria's island");
    }

    static (UiHost, WorldMapController, Runtime) Load()
    {
        Packs();
        var c = new WorldMapController { Table = table, Meta = meta, FieldId = (int)Tria, PlayerName = "Tester", PlayerLevel = 1, JobGroupId = 50,
                                         VisitedFields = new[] { Tria }, Npcs = npcs, Rooms = rooms };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 200_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, NewModel());
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        for (int i = 0; i < 400 && c.QueuedTiles > 0; i++) { c.Process(); p.Tick(); }
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, rt);
    }

    [Test]
    public void OpensOnTriasIslandAndDrawsEveryTile()
    {
        var (h, c, rt) = Load();
        Assert.That(c.SelectedWorld, Is.EqualTo(table.FindTile(Tria).World));
        Assert.That(c.QueuedTiles, Is.Zero, "FlushQueuedTiles emptied the queue");
        Assert.That(rt.Errors, Is.Empty);
        var info = h.Child("m_searchInfo");
        Assert.That(info, Is.Not.Null);
    }

    [Test]
    public void SearchAndClickSelectHenesys()
    {
        var (h, c, rt) = Load();
        c.SetPendingSearch("Henesys");
        uint henesys = table.FindSearch("Henesys").MappedId;
        Assert.That(c.OnCallback("onClickSearchInfo", new object[] { henesys }), Is.True);
        Assert.That(c.SelectedField, Is.EqualTo(henesys));
        Assert.That(c.OnCallback("OnRadioBtn", new object[] { 3 }), Is.True);
        Assert.That(c.Mode, Is.EqualTo(3));
        Assert.That(rt.Errors, Is.Empty);
        c.Close();
        for (int i = 0; i < 60 && !c.IsClosed; i++) h.Player.Tick();
        Assert.That(c.IsClosed, Is.True, "the close tween ended");
    }

    static List<string> Texts(Dictionary<string, object> o, string key) =>
        ((IEnumerable<object>)o[key]).Cast<Dictionary<string, object>>().Select(d => (string)d["text"]).ToList();

    [Test]
    public void TriasPanelListsItsDungeonAndMonsters()
    {
        var (_, c, _) = Load();
        var o = c.FieldDetails(Tria);
        foreach (var k in new[] { "dungeon", "bossMonster", "eliteMonster", "monster", "npc", "indoor" })
            TestContext.WriteLine(k + ": " + string.Join(" | ", Texts(o, k)));
        Assert.That(Texts(o, "dungeon"), Has.Count.EqualTo(1), "the Palace Undercroft room behind Tria's dungeon portal");
        Assert.That(Texts(o, "bossMonster"), Has.Count.EqualTo(1));
        Assert.That(Texts(o, "eliteMonster"), Has.Count.EqualTo(1));
        Assert.That(Texts(o, "monster"), Has.Count.EqualTo(8));
        Assert.That(Texts(o, "indoor"), Is.Not.Empty);
    }

    [Test]
    public void HenesysSearchFindsItsNotablePerson()
    {
        var (_, c, _) = Load();
        var o = c.NameSearch("Henesys");
        Assert.That(Texts(o, "map"), Has.Count.EqualTo(2));
        Assert.That(Texts(o, "indoor"), Has.Count.EqualTo(3));
        Assert.That(Texts(o, "npc"), Has.Count.EqualTo(1), "the doctor in Henesys Hospital");
        Assert.That(o.ContainsKey("title"), Is.False);
        Assert.That(c.NameSearch("zzzz-none").ContainsKey("title"), Is.True, "s_err_worldmap_search_none");
    }

    [Test]
    public void FieldMissionRoundsKeepTheirOrderAndRewards()
    {
        var t = FieldMissionTable.Parse(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <round mission=""10"" type=""statPoint"" value=""3"" subJobLevel=""1"" />
            <round mission=""5"" type=""item"" value=""20000028,1,10"" />
            <round mission=""15"" type=""SkillPoint"" value=""1"" /></ms2>"));
        Assert.That(t.GetNextRound(0), Is.EqualTo(5), "round 0 gives the first round");
        Assert.That(t.GetNextRound(5), Is.EqualTo(10));
        Assert.That(t.GetNextRound(15), Is.Zero);
        Assert.That(t.GetPrevRound(5), Is.Zero);
        Assert.That(t.GetPrevRound(15), Is.EqualTo(10));
        Assert.That(t.GetMaxRound(), Is.EqualTo(15));
        var item = t.GetReward(5);
        Assert.That((item.Type, item.ItemId, item.ItemRank, item.ItemCount), Is.EqualTo((1, 20000028, 1, 10)));
        Assert.That((t.GetReward(10).Type, t.GetReward(10).Value, t.GetReward(10).SubJobLevel), Is.EqualTo((2, 3, 2)));
        Assert.That((t.GetReward(15).Type, t.GetReward(15).SubJobLevel), Is.EqualTo((3, 1)));
        Assert.That(t.GetReward(7), Is.Null);
    }

    [Test]
    public void ExplorationPanelShowsTheFirstRoundUnclaimed()
    {
        var (_, c, _) = Load();
        var missions = new FieldMissions("KR", _ => true);
        missions.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2><environment>
            <quest><basic questID=""9001"" questType=""4"" /><progressMap progressMap=""2000001"" /><condition type=""run"" value=""1"" /></quest>
            <quest><basic questID=""9002"" questType=""4"" /><progressMap progressMap=""2000001"" /><condition type=""run"" value=""1"" /></quest>
            </environment></ms2>"));
        missions.BuildIndex();
        c.Missions = missions;
        c.MissionRewards = FieldMissionTable.Load(packs);
        Assert.That(c.MissionRewards.GetNextRound(0), Is.GreaterThan(0), "table/kr/fieldmission.xml has rounds");
        var o = c.FieldMissionData();
        foreach (var (k, v) in o) TestContext.WriteLine($"{k}: {v}");
        int first = c.MissionRewards.GetNextRound(0);
        Assert.That(o["rewardCount"], Is.EqualTo(first));
        Assert.That(o["rate"], Is.EqualTo("0%"));
        Assert.That(o["count"], Is.EqualTo("0/2"));
        Assert.That(o["enable"], Is.EqualTo(0), "nothing done: the claim is off");
        Assert.That(o["prev"], Is.EqualTo(false));
        Assert.That(o["desc"], Does.Contain(first.ToString()));
        if (c.MissionRewards.GetReward(first).Type == 1)
            Assert.That(o["binding"], Is.EqualTo(c.TypeName), "the reward item is bound as the dialog's slot");
        c.CompletedMissions = first;
        Assert.That(c.FieldMissionData()["enable"], Is.EqualTo(1), "the round is reached");
    }

    [Test]
    public void TriasPanelNamesItsFishingSpot()
    {
        var (_, c, _) = Load();
        var spot = (string)c.FieldDetails(Tria)["fishingSpot"];
        TestContext.WriteLine(spot);
        Assert.That(spot, Is.Not.Empty, "CFishingManager::GetSpotDescription of Tria's spot");
    }

    // ---- the tooltip movie over the map (GFxManager::FindMovie(5), run once for the tests) -----------------------------

    static (TooltipController, UiHost) TipLayer()
    {
        Packs();
        var tc = new TooltipController();
        var (rt, p) = src.Open(tc.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var th = new UiHost(p, tc, NewModel());
        Assert.That(th.Load(), Is.True, "tooltip movie: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        UiHost.Tooltips = tc;
        return (tc, th);
    }

    /// <summary>
    /// The dialog closed while one of its tooltips shows: the movie leaves the stage (UISwfEntity::Unload, uiswfentity.cpp:241,
    /// unloads the Loader that holds it) and the removedFromStage that runs down the tree makes every element registered
    /// with the movie's TooltipManager destroy its tooltip (TooltipManager.appendMouseEvent's REMOVED_FROM_STAGE listener
    /// and UIComponent.handleRemoveFromStage → unregisterMouseEvent → destroyTooltipByOwner), so the run's tooltip movie
    /// holds none of the map's tooltips once the dialog is closed (as the game's OnEndClose: Host.Unload + QueueFree).
    /// </summary>
    [Test]
    public void ClosingTheMapDestroysItsTooltips()
    {
        var (tc, th) = TipLayer();
        try
        {
            var (h, c, rt) = Load();
            Assert.That(rt.Errors, Is.Empty);
            // hover a tile as the HUD's shot does (HudScene.Hover): its hitarea, the first hit point of its art +4
            AsDisplayObject Breadth(string name)
            {
                var queue = new Queue<AsDisplayObject>();
                foreach (var k in (h.Player.Stage as AsContainer).Children) queue.Enqueue(k);
                while (queue.Count > 0)
                {
                    var d = queue.Dequeue();
                    if (d.InstanceName == name) return d;
                    if (d is AsContainer cc) foreach (var k in cc.Children) queue.Enqueue(k);
                }
                return null;
            }
            var hit = Breadth("m_hitarea");
            Assert.That(hit, Is.Not.Null, "a tile's m_hitarea to hover");
            var b = hit.GetBounds(null);
            (double, double)? pt = null;
            for (double y = b.Y; y < b.Y + b.H && pt == null; y += 2)
                for (double x = b.X; x < b.X + b.W && pt == null; x += 2)
                    for (var t = (AsDisplayObject)h.Player.MouseTarget(x, y); t != null; t = t.Parent)
                        if (t == hit) { pt = (x + 4, y + 4); break; }
            Assert.That(pt, Is.Not.Null, "a point on the hitarea's art");
            h.Player.MouseMove(pt.Value.Item1, pt.Value.Item2);
            for (int i = 0; i < 3; i++) { h.Player.Tick(); th.Player.Tick(); }
            Assert.That(tc.Open, Is.Not.Empty, "the hovered tile's tooltip is shown");
            // the HUD's OnEndClose wiring (HudScene.OpenWorldMap): the dialog unloaded once the close tween ended
            c.Unloaded += () => h.Unload();
            c.Close();
            for (int i = 0; i < 60 && !c.IsClosed; i++) { h.Player.Tick(); th.Player.Tick(); }
            Assert.That(c.IsClosed, Is.True, "the close tween ended");
            Assert.That(tc.Open, Is.Empty, "the movie leaving the stage destroyed its tooltips");
        }
        finally { UiHost.Tooltips = null; }
    }

    static string TaxiState(Dictionary<string, object> o) =>
        o.TryGetValue("taxi", out var a) && a is string sa && sa.Length > 0 ? sa : o.TryGetValue("goodtaxi", out var b) ? b as string : null;

    // FUN_141b5ee80 (uiworldmapdialog.cpp:2096-2101): a found station is "enabled", or with m_194 set (SetTaxiMode, :1502)
    // "default" on the current field and "active" elsewhere; the close (vfunc_67, :308) resets m_194.
    [Test]
    public void TaxiModeShowsTheFoundStationsAsDefaultHereAndActiveElsewhere()
    {
        Packs();
        var other = meta.TaxiStations.Where(kv => !kv.Value && kv.Key != (int)Tria && table.FindTile((uint)kv.Key) != null)
                        .Select(kv => (uint)kv.Key).FirstOrDefault();
        if (other == 0 || !meta.TaxiStations.TryGetValue((int)Tria, out bool home) || home) Assert.Ignore("no two plain taxi stations with tiles");
        var (h, c, rt) = Load();
        c.FoundTaxis = new[] { Tria, other };
        var here = table.FindTile(Tria);
        var there = table.FindTile(other);
        var o = new Dictionary<string, object>();
        c.TaxiMembers(here, o);
        Assert.That(TaxiState(o), Is.EqualTo("enabled"));
        c.SetTaxiMode(1);
        Assert.That(c.TaxiMode, Is.EqualTo(1));
        o.Clear(); c.TaxiMembers(here, o);
        Assert.That(TaxiState(o), Is.EqualTo("default"));
        o.Clear(); c.TaxiMembers(there, o);
        Assert.That(TaxiState(o), Is.EqualTo("active"));
        c.Close();
        Assert.That(c.TaxiMode, Is.Zero, "closing the map resets the taxi mode");
    }

    // ---- the taxi: OnTaxi (uiworldmapdialog.cpp:516), OnMessage (:755), TakeTaxi (@0x141b5a550), the fare line (:2108) ----

    sealed class TaxiRig
    {
        public WorldMapController C;
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<int> Started = new(), RodeOff = new();
        public uint Near, Far, Island;      // Near: two portals from Tria; Far: no route
        public long Mesos = 100000;
    }

    static TaxiRig Rig(out Runtime rt)
    {
        var (h, c, r) = Load();
        rt = r;
        var ids = table.Fields.Values.Where(f => f.MapType != 3 && f.Id != (int)Tria).Select(f => (uint)f.Id).OrderBy(x => x).Take(3).ToArray();
        var m = new FieldMeta();
        m.PortalTargets[(int)Tria] = new List<int> { (int)ids[0] };
        m.PortalTargets[(int)ids[0]] = new List<int> { (int)Tria, (int)ids[1] };
        c.Meta = m;
        c.PlayerLevel = 24;
        var rig = new TaxiRig { C = c, Near = ids[1], Far = ids[2] };
        c.ShowBox = rig.Boxes.Add;
        c.HasPlayer = () => true;
        c.CanEnterState = _ => true;
        c.StationNear = () => true;
        c.Mesos = () => rig.Mesos;
        c.StartTaxiCall = rig.Started.Add;
        c.RideOffForTaxi = rig.RodeOff.Add;
        MessageBoxData.CommonByKey = new Ms2.Formats.World.GameStrings(packs, "en").Common();
        c.SetTaxiMode(1);
        return rig;
    }

    static object[] TaxiArgs(uint field) => new object[] { "name", field };

    [Test]
    public void OnTaxiAsksTheFareAndYesStartsTheCall()
    {
        var r = Rig(out _);
        Assert.That(r.C.OnCallback("OnTaxi", TaxiArgs(r.Near)), Is.True);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Boxes[0].Type, Is.EqualTo(1), "a yes / no box");
        Assert.That(r.Boxes[0].Extra, Is.EqualTo("onTaxi"));
        Assert.That(r.Boxes[0].Msg, Does.Contain(TaxiManager.CalcTaxiCharge(2, 24).ToString()), "two portal hops at level 24");
        Assert.That(r.Started, Is.Empty, "nothing starts before the answer");
        Assert.That(r.C.OnMessageBox(null, 0, "onTaxi"), Is.True);
        Assert.That(r.Started, Is.EqualTo(new[] { (int)r.Near }));
        Assert.That(r.C.TaxiMode, Is.Zero, "TakeTaxi closed the dialog");
    }

    [Test]
    public void OnTaxiNoticesTheSameFieldTheNoRouteAndAPlayerThatCannotCall()
    {
        var r = Rig(out _);
        r.C.OnCallback("OnTaxi", TaxiArgs(Tria));
        Assert.That(r.Boxes.Select(b => (b.Type, b.Msg)), Is.EqualTo(new[] { (0, MessageBoxData.Common(0x8a)) }));
        r.Boxes.Clear();
        r.C.OnCallback("OnTaxi", TaxiArgs(r.Far));
        Assert.That(r.Boxes.Select(b => (b.Type, b.Msg)), Is.EqualTo(new[] { (0, r.C.Model.Tables.Common("s_err_taxi_continent") ?? "") }));
        r.Boxes.Clear();
        r.C.CanEnterState = _ => false;
        r.C.OnCallback("OnTaxi", TaxiArgs(r.Near));
        Assert.That(r.Boxes.Select(b => (b.Type, b.Msg)), Is.EqualTo(new[] { (0, MessageBoxData.Common(0x93)) }));
        r.C.HasPlayer = () => false;
        r.Boxes.Clear();
        r.C.OnCallback("OnTaxi", TaxiArgs(r.Near));
        Assert.That(r.Boxes, Is.Empty);
    }

    [Test]
    public void TakeTaxiRefusesInTheClientsOrderAndNeverStartsTheCall()
    {
        var r = Rig(out _);
        var c = r.C;
        c.OnCallback("OnTaxi", TaxiArgs(r.Near));
        r.Boxes.Clear();
        void Answer(int notice)
        {
            Assert.That(c.OnMessageBox(null, 0, "onTaxi"), Is.True);
            Assert.That(r.Started, Is.Empty);
            Assert.That(r.RodeOff, Is.Empty);
            Assert.That(c.TaxiMode, Is.EqualTo(1), "a refusal leaves the dialog open");
            if (notice < 0) Assert.That(r.Boxes, Is.Empty);
            else Assert.That(r.Boxes.Select(b => b.Msg), Is.EqualTo(new[] { MessageBoxData.Common(notice) }));
            r.Boxes.Clear();
        }
        c.PlayerDead = () => true; Answer(-1); c.PlayerDead = () => false;
        c.PlayerStateId = () => 0x25; Answer(-1);
        c.PlayerStateId = () => 0x2a; Answer(-1); c.PlayerStateId = () => 0;
        c.StationNear = () => false; Answer(0x89); c.StationNear = () => true;
        r.Mesos = TaxiManager.CalcTaxiCharge(2, 24) - 1; Answer(0x34);
        var t3 = table.Fields.Values.FirstOrDefault(f => f.MapType == 3 && f.Id != (int)Tria);
        if (t3 != null)
        {
            c.Meta.PortalTargets[(int)Tria] = new List<int> { t3.Id };
            c.OnCallback("OnTaxi", TaxiArgs((uint)t3.Id)); r.Boxes.Clear();
            Answer(0x51);                                          // CFieldData::FUN_140374410: m_nMapType == 3
            c.OnCallback("OnTaxi", TaxiArgs(r.Near)); r.Boxes.Clear();    // its fare is cached: 2 hops
        }
        r.Mesos = TaxiManager.CalcTaxiCharge(2, 24);
        c.OnMessageBox(null, 0, "onTaxi");
        Assert.That(r.Started, Is.EqualTo(new[] { (int)r.Near }), "exactly the fare in mesos is enough");
    }

    [Test]
    public void ARiderRidesOffInsteadOfCallingAndANoAnswerDoesNothing()
    {
        var r = Rig(out _);
        r.C.OnCallback("OnTaxi", TaxiArgs(r.Near));
        Assert.That(r.C.OnMessageBox(null, 1, "onTaxi"), Is.False);
        Assert.That(r.Started, Is.Empty);
        r.C.PlayerRiding = () => true;
        Assert.That(r.C.OnMessageBox(null, 0, "onTaxi"), Is.True);
        Assert.That(r.RodeOff, Is.EqualTo(new[] { (int)r.Near }));
        Assert.That(r.Started, Is.Empty);
    }

    [Test]
    public void AnyYesAnswerIsHandledWhateverTheTagButANoIsNot()
    {
        var r = Rig(out _);
        Assert.That(r.C.OnMessageBox(null, 0, "someOtherTag"), Is.True, "OnMessage :774 returns 1 for any result 0");
        Assert.That(r.C.OnMessageBox(null, 1, "someOtherTag"), Is.False);
        Assert.That(r.Started, Is.Empty, "an unknown tag runs nothing");
    }

    [Test]
    public void TheTooltipShowsTheFareOfAFoundStationAndNoDescriptionThen()
    {
        var r = Rig(out _);
        var other = meta.TaxiStations.Where(kv => !kv.Value && kv.Key != (int)Tria && table.FindTile((uint)kv.Key) != null)
                        .Select(kv => (uint)kv.Key).FirstOrDefault();
        if (other == 0 || !meta.TaxiStations.TryGetValue((int)Tria, out bool home) || home) Assert.Ignore("no two plain taxi stations with tiles");
        var c = r.C;
        c.Meta.TaxiStations[(int)Tria] = false;
        c.Meta.TaxiStations[(int)other] = false;
        c.Meta.PortalTargets[(int)Tria] = new List<int> { (int)other };
        c.FoundTaxis = new[] { Tria, other };
        var o = new Dictionary<string, object>();
        c.TaxiMembers(table.FindTile(other), o);
        Assert.That(o["taxicost"], Is.Not.EqualTo(""), "the fare line");
        Assert.That(o["taxicost"].ToString(), Does.Contain(TaxiManager.CalcTaxiCharge(1, 24).ToString()));
        Assert.That(o["taxidesc"], Is.EqualTo(""), "no description beside a fare");
        o.Clear(); c.TaxiMembers(table.FindTile(Tria), o);
        Assert.That(o["taxicost"], Is.EqualTo(""), "the current field has no fare");
        Assert.That(o["taxidesc"], Is.Not.EqualTo(""));
    }

    // ---- the air taxi: OnCashTaxi (:548), CheckCashTaxi (@0x141b60e90), FUN_141b614c0, the confirms and TakeCashTaxi (@0x141b5aa70) ----

    sealed class CashRig
    {
        public WorldMapController C;
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<int> Called = new();
        public readonly Dictionary<int, int> Tickets = new();
        public uint Target;      // Tria's continent, a destination, no admin level, no pk mode
        public string Name;
        public long Mesos = 10_000_000;
    }

    static CashRig CashTaxiRig(bool visited)
    {
        var (h, c, rt) = Load();
        var tria = table.FindField(Tria);
        uint target = table.Fields.Values.Where(f => f.Id != (int)Tria && f.Continent == tria.Continent && f.CashTaxiDestinationAllowed
                                                     && f.EnterAdminLevel == 0 && f.PkMode == 0)
                           .Select(f => (uint)f.Id).OrderBy(x => x).First();
        var rig = new CashRig { C = c, Target = target, Name = c.Model.Tables.MapName((int)target) ?? "" };
        c.PlayerLevel = 24;
        c.ShowBox = rig.Boxes.Add;
        c.HasPlayer = () => true;
        c.CanEnterState = _ => true;
        c.Mesos = () => rig.Mesos;
        c.TicketCount = tag => rig.Tickets.TryGetValue(tag, out int n) ? n : 0;
        c.StartCashCall = rig.Called.Add;
        MessageBoxData.CommonByKey = new Ms2.Formats.World.GameStrings(packs, "en").Common();
        c.SetTaxiMode(1);        // Close() resets it: the sign that TakeCashTaxi closed the dialog
        c.VisitedFields = visited ? new[] { Tria, target } : new[] { Tria };
        c.OnIconPacket();        // FUN_141b58f00 copies DAT_143ab3058 into m_1a0
        return rig;
    }

    static string Fill(string text, params object[] args)
    {
        for (int i = 0; i < args.Length; i++) text = text.Replace("{" + i + "}", Convert.ToString(args[i], System.Globalization.CultureInfo.InvariantCulture));
        return text;
    }

    [Test]
    public void OnCashTaxiRefusesInTheClientsOrder()
    {
        var r = CashTaxiRig(true);
        var c = r.C;
        void Ask(uint f) => Assert.That(c.OnCallback("OnCashTaxi", new object[] { f }), Is.True);
        void Expect(string msg, string why)
        {
            Assert.That(r.Boxes.Select(b => b.Msg), Is.EqualTo(new[] { msg }), why);
            Assert.That(r.Called, Is.Empty);
            r.Boxes.Clear();
        }
        c.PlayerDead = () => true; Ask(r.Target); Expect(MessageBoxData.Common(0x91), "dead"); c.PlayerDead = () => false;
        c.CanEnterState = s => s != 0x2a; Ask(r.Target); Expect(MessageBoxData.Common(0x93), "FUN_141527160(0x2a)"); c.CanEnterState = _ => true;
        c.PlayerInCubeMode = () => true; Ask(r.Target); Expect(MessageBoxData.Common(0x92), "cube mode"); c.PlayerInCubeMode = () => false;
        c.PlayerStateId = () => 0x25; Ask(r.Target); Expect(MessageBoxData.Common(0x91), "StateTaxiCall");
        c.PlayerStateId = () => 0x2a; Ask(r.Target); Expect(MessageBoxData.Common(0x91), "StateCashCall"); c.PlayerStateId = () => 1;
        Ask(Tria); Expect(MessageBoxData.Common(0x8a), "the current field");
        var cases = new List<string>();
        if (table.Fields.Values.FirstOrDefault(f => !f.CashTaxiDepartureAllowed) is { } noDep)
        {
            c.FieldId = noDep.Id; Ask(r.Target); Expect(MessageBoxData.Common(0x8f), "cashTaxiNotDeparture"); c.FieldId = (int)Tria;
            cases.Add("departure");
        }
        if (table.Fields.Values.FirstOrDefault(f => !f.CashTaxiDestinationAllowed && f.Id != (int)Tria) is { } noDest)
        {
            Ask((uint)noDest.Id); Expect(Fill(MessageBoxData.Common(0x90), c.Model.Tables.MapName(noDest.Id) ?? ""), "cashTaxiNotDestination");
            cases.Add("destination");
        }
        if (table.Fields.Values.FirstOrDefault(f => f.EnterAdminLevel > 0 && f.CashTaxiDestinationAllowed && f.Id != (int)Tria) is { } admin)
        {
            Ask((uint)admin.Id); Expect(MessageBoxData.Common(0x99d), "enterAdminLevel");
            cases.Add("admin");
        }
        var tria = table.FindField(Tria);
        if (table.Fields.Values.FirstOrDefault(f => f.Continent != tria.Continent && f.CashTaxiDestinationAllowed && f.EnterAdminLevel == 0) is { } far)
        {
            Ask((uint)far.Id); Expect(MessageBoxData.Common(0x8e), "another continent group");
            c.WarpOpenContinents = new[] { tria.Continent, far.Continent };
            Ask((uint)far.Id);
            Assert.That(r.Boxes, Has.Count.EqualTo(1));
            Assert.That(r.Boxes[0].Msg, Is.Not.EqualTo(MessageBoxData.Common(0x8e)), "WarpOpenContinent joins the two groups");
            r.Boxes.Clear();
            cases.Add("continent");
        }
        c.HasPlayer = () => false; Ask(r.Target);
        Assert.That(r.Boxes, Is.Empty, "no player: the callback returns first");
        TestContext.WriteLine("field cases run: " + string.Join(", ", cases));
        Assert.That(cases, Does.Contain("continent"), "the data has fields on other continents");
    }

    [Test]
    public void AVisitedFieldWithoutTicketsAsksTheMesoFareAndYesCallsWhenTheMesosCoverIt()
    {
        var r = CashTaxiRig(true);
        var c = r.C;
        c.AirTaxiDiscountRate = () => 0.1f;
        c.OnCallback("OnCashTaxi", new object[] { r.Target });
        long fare = (long)((double)TaxiManager.GetAirTaxiFare(24) * (double)(1.0f - 0.1f) + 0.000001);
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        var box = r.Boxes[0];
        Assert.That((box.Type, box.HasCallback), Is.EqualTo((1, true)));
        Assert.That(box.Msg, Is.EqualTo(Fill(c.Model.Tables.Common("s_cash_taxi_transfer_meso") ?? "", r.Name, fare)));
        Assert.That(box.ArgLong(0, -1), Is.EqualTo(fare), "the fare kept in the box");
        r.Boxes.Clear();
        box.Callback(1, box);
        Assert.That((r.Boxes.Count, r.Called.Count), Is.EqualTo((0, 0)), "no does nothing");
        r.Mesos = fare - 1;
        box.Callback(0, box);
        Assert.That(r.Boxes.Select(b => b.Msg), Is.EqualTo(new[] { MessageBoxData.Common(0x34) }));
        Assert.That(r.Called, Is.Empty);
        r.Mesos = fare;
        box.Callback(0, box);
        Assert.That(r.Called, Is.EqualTo(new[] { (int)r.Target }));
        Assert.That(c.TaxiMode, Is.Zero, "TakeCashTaxi closed the dialog");
    }

    [Test]
    public void TicketsTheFreeEventAndVipAskWithOnCashTaxiAndYesCalls()
    {
        var r = CashTaxiRig(true);
        var c = r.C;
        r.Tickets[CashCall.AirTaxiTag] = 1;
        c.OnCallback("OnCashTaxi", new object[] { r.Target });
        Assert.That(r.Boxes.Select(b => (b.Type, b.Extra, b.Msg)),
            Is.EqualTo(new[] { (1, "onCashTaxi", Fill(MessageBoxData.Common(0x8c), r.Name)) }));
        Assert.That(r.Called, Is.Empty, "nothing before the answer");
        Assert.That(c.OnMessageBox(null, 1, "onCashTaxi"), Is.False);
        Assert.That(r.Called, Is.Empty);
        Assert.That(c.OnMessageBox(null, 0, "onCashTaxi"), Is.True);
        Assert.That(r.Called, Is.EqualTo(new[] { (int)r.Target }));

        r = CashTaxiRig(true);
        r.Tickets[CashCall.AdvancedAirTaxiTag] = 1;
        r.C.FreeAirTaxi = () => true;
        r.C.OnCallback("OnCashTaxi", new object[] { r.Target });
        Assert.That(r.Boxes.Select(b => (b.Extra, b.Msg)), Is.EqualTo(new[] { ("onCashTaxi", Fill(MessageBoxData.Common(0x8d), r.Name)) }),
            "the free event comes before the tickets");

        r = CashTaxiRig(false);
        r.C.PlayerVip = () => true;
        r.C.FeatureEnabled = id => id == CashCall.VipAirTaxiFeature;
        r.C.OnCallback("OnCashTaxi", new object[] { r.Target });
        Assert.That(r.Boxes.Select(b => (b.Extra, b.Msg)),
            Is.EqualTo(new[] { ("onCashTaxi", Fill(r.C.Model.Tables.Common("s_taxi_transfer_vip_user") ?? "", r.Name)) }), "VIP goes before the visit test");
    }

    [Test]
    public void AFieldNotVisitedNeedsThreeAdvancedTicketsOrTheMeratBox()
    {
        var r = CashTaxiRig(false);
        var c = r.C;
        string Key(string k) => c.Model.Tables.Common(k) ?? "";
        void Ask() { r.Boxes.Clear(); c.OnCallback("OnCashTaxi", new object[] { r.Target }); }
        r.Tickets[CashCall.AirTaxiTag] = 5;
        r.Tickets[CashCall.AdvancedAirTaxiTag] = 2;
        Ask();
        Assert.That(r.Boxes.Select(b => (b.Type, b.Msg)), Is.EqualTo(new[] { (0, Key("s_err_cash_taxi_need_advanced_ticket")) }),
            "air_taxi tickets do not count for a field not visited");
        r.Tickets[CashCall.AdvancedAirTaxiTag] = 3;
        Ask();
        Assert.That(r.Boxes.Select(b => (b.Type, b.Extra, b.Msg)),
            Is.EqualTo(new[] { (1, "onCashTaxi", Fill(Key("s_cash_taxi_transfer_advanced"), r.Name)) }));
        r.Tickets[CashCall.AdvancedAirTaxiTag] = 0;
        c.FeatureEnabled = id => id == CashCall.MeratAirTaxiFeature;
        c.AirTaxiDiscountRate = () => 0.5f;
        Ask();
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        var box = r.Boxes[0];
        Assert.That((box.Type, box.HasCallback), Is.EqualTo((1, true)));
        Assert.That(box.Msg, Is.EqualTo(Fill(Key("s_err_cash_taxi_need_advanced_ticket_merat"), 195L)), "MeratAirTaxiPrice 390 less half");
        r.Boxes.Clear();
        box.Callback(0, box);
        Assert.That((r.Boxes.Count, r.Called.Count), Is.EqualTo((0, 0)), "no platform merat check: nothing");
        long asked = 0;
        c.MeratPlatformCheck = p => { asked = p; return false; };
        box.Callback(0, box);
        Assert.That(asked, Is.EqualTo(390), "the platform checks the undiscounted price");
        Assert.That(r.Boxes.Select(b => b.Msg), Is.EqualTo(new[] { MessageBoxData.Common(0x35) }));
        c.MeratPlatformCheck = _ => true;
        box.Callback(0, box);
        Assert.That(r.Called, Is.EqualTo(new[] { (int)r.Target }));
    }

    [Test]
    public void APvpFieldOnAShadowContinentAsksBeforeTheCall()
    {
        var r = CashTaxiRig(true);     // first: its Load skips the test without data
        var tria = table.FindField(Tria);
        var pk = table.Fields.Values.FirstOrDefault(f => f.PkMode != 0 && f.Id != (int)Tria && f.CashTaxiDestinationAllowed && f.EnterAdminLevel == 0);
        Assert.That(pk, Is.Not.Null, "the data has a pk field");
        Assert.That(tria.PkMode, Is.Zero);
        var c = r.C;
        c.VisitedFields = new[] { Tria, (uint)pk.Id };
        c.OnIconPacket();
        c.WarpOpenContinents = new[] { tria.Continent, pk.Continent };
        r.Tickets[CashCall.AirTaxiTag] = 1;
        c.IsShadowContinent = code => code == pk.Continent;
        c.OnCallback("OnCashTaxi", new object[] { (uint)pk.Id });
        r.Boxes.Clear();
        c.OnMessageBox(null, 0, "onCashTaxi");
        Assert.That(r.Called, Is.Empty, "the pk box comes first");
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        var box = r.Boxes[0];
        Assert.That((box.Type, box.Msg, box.HasCallback), Is.EqualTo((1, MessageBoxData.Common(0x971), true)));
        Assert.That(c.TaxiMode, Is.EqualTo(1), "the dialog stays open");
        box.Callback(0, box);
        Assert.That(r.Called, Is.EqualTo(new[] { pk.Id }));
        Assert.That(c.TaxiMode, Is.Zero);
        // not a shadow continent: no box, the call at once
        r = CashTaxiRig(true);
        r.C.VisitedFields = new[] { Tria, (uint)pk.Id };
        r.C.OnIconPacket();
        r.C.WarpOpenContinents = new[] { tria.Continent, pk.Continent };
        r.Tickets[CashCall.AirTaxiTag] = 1;
        r.C.OnCallback("OnCashTaxi", new object[] { (uint)pk.Id });
        r.C.OnMessageBox(null, 0, "onCashTaxi");
        Assert.That(r.Called, Is.EqualTo(new[] { pk.Id }));
    }

    [Test]
    public void TheSearchInfosCashTaxiIconChecksTheAirTaxi()
    {
        var r = CashTaxiRig(true);
        r.C.OnCallback("onClickSearchInfoIcon", new object[] { "dungeon", r.Target });
        Assert.That(r.Boxes, Is.Empty, "the dungeon room dialog is not here");
        r.C.OnCallback("onClickSearchInfoIcon", new object[] { "cashTaxi", r.Target });
        Assert.That(r.Boxes, Has.Count.EqualTo(1));
        Assert.That(r.Boxes[0].Msg, Is.EqualTo(Fill(r.C.Model.Tables.Common("s_cash_taxi_transfer_meso") ?? "", r.Name, TaxiManager.GetAirTaxiFare(24))),
            "no ticket, no discount: the meso box of CheckCashTaxi");
    }
}
