using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

public class QuestLogTests
{
    sealed class FakeState : IQuestState
    {
        public readonly SortedDictionary<int, QuestRecord> ById = new();
        public readonly HashSet<int> Startable = new();
        public IReadOnlyList<QuestRecord> Records => ById.Values.ToList();
        public QuestRecord Get(int questId) => ById.TryGetValue(questId, out var q) ? q : null;
        public bool CanStart(int questId) => Startable.Contains(questId);
        public int PlayerLevel { get; set; } = 10;
        public long CharacterId => 7;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
        public void Add(int id, int state, long start = 0, long complete = 0, bool done = false) =>
            ById[id] = new QuestRecord(id, state, start, complete, 0, done, false);
    }

    // synthetic data: chapter 10 epic 100 -> 101, chapter 20 a tutorial, chapters 30 and 40 world quests in two
    // continents, 301 a daily
    static QuestTable Table()
    {
        var t = new QuestTable("KR", _ => true);
        string Q(int id, int chapter, int type, int level, string extra = "", string require = "", int npc = 0) =>
            $@"<quest><basic questID=""{id}"" chapterID=""{chapter}"" questType=""{type}"" standardLevel=""{level}"" {extra} />
               <require quest=""{require}"" /><start npc=""{npc}"" /></quest>";
        t.AddQuests(XDocument.Parse("<ms2><environment>" +
            Q(100, 10, 0, 10) + Q(101, 10, 0, 12, require: "100") + Q(200, 20, 0, 1) +
            Q(300, 30, 1, 20, npc: 1) + Q(301, 30, 1, 20, @"repeatable=""2""", npc: 1) + Q(400, 40, 1, 25, npc: 2) +
            "</environment></ms2>"));
        t.BuildIndexes();
        t.AddChapters(XDocument.Parse(@"<ms2><chapter id=""10"" name=""Ch10"" /><chapter id=""20"" name=""Tut"" isTutorial=""1"" />
            <chapter id=""30"" name=""Ch30"" isTutorial=""OK"" /><chapter id=""40"" name=""Ch40"" /></ms2>"));
        t.AddDescriptions(XDocument.Parse(@"<ms2><quest questID=""100"" name=""A"" /><quest questID=""101"" name=""B"" />
            <quest questID=""200"" name=""T"" /><quest questID=""300"" name=""W"" /><quest questID=""301"" name=""D"" />
            <quest questID=""400"" name=""X"" /></ms2>"));
        var common = new Dictionary<string, string>
        {
            ["s_quest_list_quest_name"] = "{0} (Lv. {1})", ["s_quest_list_level"] = "Level {0} - {1}",
            ["s_quest_list_level_equal"] = "Lv. {0}", ["s_quest_repeat_daily"] = "[Daily]",
        };
        t.Common = k => common.TryGetValue(k, out var v) ? v : "";
        var avail = (Func<XElement, bool>)t.Available;
        t.Words.AddTable(ReservedWords.ContinentName, XDocument.Parse(@"<ms2><key id=""1"" name=""East"" /><key id=""2"" name=""West"" /></ms2>"), avail);
        t.Words.AddTable(ReservedWords.IslandName, XDocument.Parse(@"<ms2><key id=""1"" name=""Isle"" /></ms2>"), avail);
        t.Words.AddTable(ReservedWords.RegionName, XDocument.Parse(@"<ms2><key id=""5"" name=""Forest"" /></ms2>"), avail);
        return t;
    }

    static QuestLog Log(FakeState s, int tab, int subTab)
    {
        var log = new QuestLog(Table(), s) { Tab = tab, SubTab = subTab };
        log.Meta = FieldMeta.Parse(@"<ms2><npc id=""1""><field id=""1000"" /></npc><npc id=""2""><field id=""2000"" /></npc></ms2>");
        log.FieldCodes = new Dictionary<int, (int, int)> { [1000] = (1, 5), [2000] = (2, 0) };
        return log;
    }

    static IEnumerable<string> Labels(QuestLogNode root) => root.Nodes.Select(n => n.Label);

    [Test]
    public void OfflineListsAreEmpty()
    {
        var s = new NoQuestState(10);
        for (int tab = 0; tab < 4; tab++)
            for (int sub = 0; sub < 3; sub++)
            {
                var root = new QuestLog(Table(), s) { Tab = tab, SubTab = sub }.Build();
                Assert.That(root.Type, Is.EqualTo(1));
                Assert.That(root.Nodes, Is.Empty);
                Assert.That(new QuestLogTree(root).Length, Is.EqualTo(0));
            }
    }

    [Test]
    public void StartableListTakesTheChapterFromTheStartableQuestOn()
    {
        var s = new FakeState();
        s.Startable.Add(100);
        var root = Log(s, 0, 0).Build();
        var ch = root.Nodes.Single();
        Assert.That((ch.Chapter, ch.Label, ch.Level, ch.Type, ch.Renderer), Is.EqualTo((10, "Ch10", "Level 10 - 12", 2, "qd_renderer_chapter")));
        Assert.That(ch.Nodes.Select(n => (n.Id, n.Label, n.Icon)), Is.EqualTo(new[] { (100u, "A", ""), (101u, "B", "") }));
    }

    [Test]
    public void StartableListProperLevelFilter()
    {
        var s = new FakeState { PlayerLevel = 16 };
        s.Startable.Add(100);
        Assert.That(Log(s, 0, 0).Build().Nodes, Is.Empty);                        // 6 levels below
        var log = Log(s, 0, 0);
        log.ProperLevel = false;
        Assert.That(log.Build().Nodes, Has.Count.EqualTo(1));
        log = Log(s, 0, 0);
        log.Focus = 101;                                                       // the focused chapter is exempt
        Assert.That(log.Build().Nodes.Single().Type, Is.EqualTo(1));          // and open
    }

    [Test]
    public void StartableListFollowsTheQuestInProgress()
    {
        var s = new FakeState();
        s.Add(100, 1);
        var ch = Log(s, 0, 0).Build().Nodes.Single();
        Assert.That(ch.Nodes.Select(n => n.Id), Is.EqualTo(new[] { 101u }));
    }

    [Test]
    public void StartableListSortsByLevelAndKeepsTheTab()
    {
        var s = new FakeState { PlayerLevel = 20 };
        s.Startable.UnionWith(new[] { 400, 300, 100, 200 });
        Assert.That(Labels(Log(s, 2, 0).Build()), Is.EqualTo(new[] { "Ch30", "Ch40" }));   // world tab; level 20 then 25
        // the daily of chapter 30 sits in the daily tab; its own node lists from it on (a startable daily)
        s.Startable.Add(301);
        var daily = Log(s, 1, 0).Build().Nodes.Single();
        Assert.That(daily.Nodes.Select(n => n.Label), Is.EqualTo(new[] { "[Daily] D" }));
        Assert.That(Log(s, 0, 0).Build().Nodes, Is.Empty);                     // 100 is 10 levels below; 200 is a tutorial
    }

    [Test]
    public void EpicTabInProgress()
    {
        var s = new FakeState();
        s.Add(100, 2);
        s.Add(101, 1, start: 5);
        var log = Log(s, 0, 1);
        var root = log.Build();
        var ch = root.Nodes.Single();
        Assert.That(ch.Type, Is.EqualTo(1));                                  // in progress: open the first time
        Assert.That(ch.Nodes.Select(n => (n.Label, n.Icon, n.Progress)), Is.EqualTo(new[]
        {
            ("A (Lv. 10)", "complete", false), ("B (Lv. 12)", "progress", true),
        }));
        log.ProgressOnly = true;
        Assert.That(log.Build().Nodes.Single().Nodes.Select(n => n.Id), Is.EqualTo(new[] { 101u }));
        Assert.That(log.SelectChapter(10), Is.True);                           // was open: closes
        Assert.That(log.Build().Nodes.Single().Type, Is.EqualTo(2));
        Assert.That(Log(s, 0, 2).Build().Nodes, Is.Empty);                     // the completed sub-tab: not all rewarded
    }

    [Test]
    public void WorldTabGroupsByContinentAndRegion()
    {
        var s = new FakeState();
        s.Add(300, 1);
        s.Add(400, 1);
        var root = Log(s, 2, 1).Build();
        // "East" and "West" in the CAtlMap's string-hash order, region 5 with a name, region 0 without
        var expected = QuestLog.AtlStringMapOrder(new[] { "East", "West" });
        var rows = root.Nodes.Select(n => n.Renderer + ":" + (n.Label ?? n.Field)).ToList();
        var east = new[] { "qd_renderer_continent:East", "qd_renderer_region:Forest", "qd_renderer_chapter:Ch30" };
        var west = new[] { "qd_renderer_continent:West", "qd_renderer_chapter:Ch40" };
        Assert.That(rows, Is.EqualTo(expected[0] == "East" ? east.Concat(west) : west.Concat(east)));
        Assert.That(root.Nodes.Single(n => n.Field == "Forest").Code, Is.EqualTo("Isle"));
    }

    [Test]
    public void CStringHash()
    {
        Assert.That(QuestLog.Hash("ab"), Is.EqualTo((uint)('a' * 33 + 'b')));
        Assert.That(QuestLog.AtlStringMapOrder(new[] { "a", "b", "a" }), Is.EqualTo(new[] { "a", "b" }));   // bins 97 % 17, 98 % 17
    }

    static QuestLogNode N(string label, int type = 0, params QuestLogNode[] nodes) =>
        new() { Label = label, Type = type, Nodes = nodes.Length == 0 && type == 0 ? null : nodes.ToList() };

    [Test]
    public void TreeRowsWalkOpenNodes()
    {
        var root = N("root", 1, N("c1", 1, N("a"), N("b")), N("c2", 2, N("x")), N("c3", 1, N("y")));
        var tree = new QuestLogTree(root);
        Assert.That(tree.Length, Is.EqualTo(6));
        Assert.That(tree.Range(0, 7).Select(n => n?.Label), Is.EqualTo(new[] { "c1", "a", "b", "c2", "c3", "y", null, null }));
        Assert.That(tree.ItemAt(3).Label, Is.EqualTo("c2"));
        Assert.That(tree.ItemAt(9).Label, Is.EqualTo("y"));                     // past the end: the last node
        Assert.That(root.Nodes[0].Nodes[0].DepthIcons, Is.EqualTo(new[] { 1 }));
        Assert.That(root.Nodes[2].Nodes[0].DepthIcons, Is.EqualTo(new[] { 0 }));
        Assert.That(root.Nodes[1].Nodes[0].Type, Is.EqualTo(QuestLogTree.TypeLeaf));
    }

    [Test]
    public void TreeOpenNodeWithNoChildrenEndsItsBranch()
    {
        // as the AS walks it: after the empty open c1, the walk goes to root's next sibling (none), so c2 is not counted
        var root = N("root", 1, N("c1", 1), N("c2"));
        root.Nodes[0].Nodes = new List<QuestLogNode>();
        var tree = new QuestLogTree(root);
        Assert.That(tree.Length, Is.EqualTo(1));
        Assert.That(tree.ItemAt(1).Label, Is.EqualTo("c2"));
    }
}
