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
}
