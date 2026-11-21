using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>CQuestNavigationMap's trace table and CQuestNavigator (questnavigation.cpp, questnavigator.cpp).</summary>
public class QuestNavigatorTests
{
    // fields 1 - 2 - 3 in a line, 4 alone; npc 11 in 1 and 3, npc 12 in 2, interact 21 in 2, liftable 31 in 3
    const string Meta = @"<ms2>
  <fieldPortal field=""1""><portal portalType=""0"" targetField=""2""/></fieldPortal>
  <fieldPortal field=""2""><portal portalType=""0"" targetField=""1""/><portal portalType=""0"" targetField=""3""/></fieldPortal>
  <fieldPortal field=""3""><portal portalType=""0"" targetField=""2""/></fieldPortal>
  <fieldPortal field=""4""/>
  <npc id=""11""><field id=""1""/><field id=""3""/></npc>
  <npc id=""12""><field id=""2""/></npc>
  <npc id=""90""><field id=""3""/></npc>
  <interactObject id=""21""><field id=""2""/><field id=""4""/></interactObject>
  <liftable id=""31""><field id=""3""/></liftable>
</ms2>";

    sealed class State : IQuestState
    {
        public readonly List<QuestRecord> List = new();
        public IReadOnlyList<QuestRecord> Records => List;
        public QuestRecord Get(int id) => List.Find(r => r.Id == id);
        public bool CanStart(int questId) => false;
        public int PlayerLevel => 1;
        public long CharacterId => 0;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
    }

    static FieldMeta FieldMetaOf() => FieldMeta.Parse(Meta);

    static QuestTable Table(params string[] quests)
    {
        var t = new QuestTable("", null);
        t.AddQuests(XDocument.Parse("<ms2>" + string.Concat(quests) + "</ms2>"));
        t.BuildIndexes();
        return t;
    }

    // 100: talk to npc 12 (field 2), then use interact 21; 101: navi rows naming liftable 31 and npc 11 in field 3 only;
    // 102: npc condition with target fields; 103: no navi flag
    static readonly string[] Quests =
    {
        @"<quest id=""100""><basic useNavi=""1""/><complete npc=""90""/>
            <condition type=""npc"" code=""12"" value=""1""/><condition type=""interact_object"" code=""21"" value=""1""/></quest>",
        @"<quest id=""101""><basic useNavi=""true""/>
            <condition type=""item_move"" code=""5"" value=""1""><navi type=""liftable"" code=""31""/><navi type=""NPC"" code=""11"" map=""3""/><navi type=""other"" code=""77""/></condition></quest>",
        @"<quest id=""102""><basic useNavi=""1""/><condition type=""npc"" code=""11"" target=""3"" value=""1""/></quest>",
        @"<quest id=""103""><condition type=""npc"" code=""12"" value=""1""/></quest>",
    };

    static QuestRecord Active(int id, bool complete = false, params long[] progress) =>
        new(id, 1, 0, 0, 1, complete, false, progress);

    [Test]
    public void FieldMetaReadsInteractAndLiftableFields()
    {
        var m = FieldMetaOf();
        Assert.That(m.InteractFields[21], Is.EqualTo(new[] { 2, 4 }));
        Assert.That(m.LiftableFields[31], Is.EqualTo(new[] { 3 }));
        Assert.That(m.NpcFields[11], Is.EqualTo(new[] { 1, 3 }));
    }

    [Test]
    public void QuestTableReadsUseNaviAndNaviRows()
    {
        var t = Table(Quests);
        Assert.That(t.Quest(100).UseNavi, Is.True);
        Assert.That(t.Quest(103).UseNavi, Is.False);
        Assert.That(t.Quest(100).Navigation, Is.Empty);
        var rows = t.Quest(101).Navigation[0];
        Assert.That(rows.Select(r => r.Kind), Is.EqualTo(new[] { 2, 0, 3 }));
        Assert.That(rows[1].Ids, Is.EqualTo(new uint[] { 11 }));
        Assert.That(rows[1].Maps, Is.EqualTo(new uint[] { 3 }));
        Assert.That(rows[0].Maps, Is.Empty);
    }

    [Test]
    public void TraceTableClassifiesConditionsAndNaviRows()
    {
        var meta = FieldMetaOf();
        var map = new QuestNavigationMap(Table(Quests), meta, QuestMapGraph.From(meta));
        Assert.That(map.TargetOf(100, 0).ByKind[0][2], Is.EqualTo(new[] { 12 }));
        Assert.That(map.TargetOf(100, 1).ByKind[1].Keys, Is.EqualTo(new[] { 2, 4 }));
        // navi rows win over the condition type; a kind past 2 is dropped; a row's own fields win over the meta
        var t101 = map.TargetOf(101, 0);
        Assert.That(t101.ByKind[2][3], Is.EqualTo(new[] { 31 }));
        Assert.That(t101.ByKind[0].Keys, Is.EqualTo(new[] { 3 }));
        // a type 0xc condition with target ids: those are the fields
        Assert.That(map.TargetOf(102, 0).ByKind[0].Keys, Is.EqualTo(new[] { 3 }));
        Assert.That(QuestNavigationMap.ClassifyCondition(0x1a), Is.EqualTo(0));
        Assert.That(QuestNavigationMap.ClassifyCondition(0x1e), Is.EqualTo(1));
        Assert.That(QuestNavigationMap.ClassifyCondition(0x9c), Is.EqualTo(2));
        Assert.That(QuestNavigationMap.ClassifyCondition(0x1c), Is.EqualTo(3));
    }

    [Test]
    public void TraceQuestPicksTheNearestFieldOfTheUnfinishedConditions()
    {
        var meta = FieldMetaOf();
        var map = new QuestNavigationMap(Table(Quests), meta, QuestMapGraph.From(meta));
        var c = map.TraceQuest(Active(100, false, 0, 0), 1);
        Assert.That(c.MapId, Is.EqualTo(2));
        Assert.That(c.NpcIds, Is.EquivalentTo(new[] { 12 }));
        Assert.That(c.InteractIds, Is.EquivalentTo(new[] { 21 }));      // field 4 is unreachable
        // the npc condition done: only the interact object is left
        c = map.TraceQuest(Active(100, false, 1, 0), 1);
        Assert.That(c.NpcIds, Is.Empty);
        Assert.That(c.InteractIds, Is.EquivalentTo(new[] { 21 }));
        // a complete quest: its complete npc in the nearest field holding it
        c = map.TraceQuest(Active(100, true, 1, 1), 1);
        Assert.That((c.MapId, c.NpcIds.Single()), Is.EqualTo((3, 90)));
        Assert.That(map.TraceQuest(new QuestRecord(100, 2, 0, 0, 1, false, false), 1), Is.Null);

        var ctx = new TraceContext();
        map.AddFieldTargets(Active(101, false, 0), 3, ctx);
        map.AddFieldTargets(Active(100, false, 0, 0), 3, ctx);
        Assert.That(ctx.NpcIds, Is.EquivalentTo(new[] { 11 }));
        Assert.That(ctx.LiftableIds, Is.EquivalentTo(new[] { 31 }));
    }

    static (QuestNavigator nav, State state, List<int> saved) Navigator(int field)
    {
        var meta = FieldMetaOf();
        var table = Table(Quests);
        var state = new State();
        var saved = new List<int>();
        var nav = new QuestNavigator(table, state)
        {
            Map = new QuestNavigationMap(table, meta, QuestMapGraph.From(meta)),
            FieldId = () => field,
            PlayerPosition = () => (0, 0, 0),
            CanShowBeam = () => true,
            SaveTracked = saved.Add,
        };
        return (nav, state, saved);
    }

    [Test]
    public void SameFieldTakesTheClosestKind()
    {
        var (nav, state, _) = Navigator(2);
        state.List.Add(Active(100, false, 0, 0));
        nav.NearestNpc = ids => ids.Contains(12) ? (300, 0, 0, 300f) : null;
        nav.NearestInteract = ids => ids.Contains(21) ? (0, 200, 0, 200f) : null;
        nav.OnQuestAccepted(state.List[0]);
        nav.OnCompleteMyPc();
        nav.Frame();
        Assert.That(nav.Result.QuestId, Is.EqualTo(100));
        Assert.That(nav.Result.Kind, Is.EqualTo(TraceKind.InteractObject));
        Assert.That((nav.Result.X, nav.Result.Y), Is.EqualTo((0.0, 200.0)));
        Assert.That(nav.Result.DistanceText, Is.EqualTo(string.Format(TraceStrings.Default.DistanceM, 2)));
        Assert.That(nav.Result.Context.MapId, Is.EqualTo(2));
        Assert.That(nav.BeamShown, Is.True);
        Assert.That(nav.Context.NpcIds, Is.EquivalentTo(new[] { 12 }));
        Assert.That(nav.Context.InteractIds, Is.EquivalentTo(new[] { 21 }));
    }

    [Test]
    public void AnotherFieldGoesThroughThePortalOnThePath()
    {
        var (nav, state, saved) = Navigator(1);
        state.List.Add(Active(102, false, 0));
        nav.Portals = () => new[] { new TracePortal(9, 1, 1, 1), new TracePortal(2, 500, 0, 0, Shown: false), new TracePortal(2, 0, 700, 0) };
        nav.OnQuestAccepted(state.List[0]);
        Assert.That(saved, Is.EqualTo(new[] { 102 }));
        nav.OnCompleteMyPc();
        nav.Frame();
        Assert.That(nav.Result.Kind, Is.EqualTo(TraceKind.Portal));
        Assert.That(nav.Result.Y, Is.EqualTo(700.0));
        // FindPortalByDestMap: the first portal to the next field decides, even when hidden
        nav.Portals = () => new[] { new TracePortal(2, 500, 0, 0, Shown: false) };
        nav.Frame();
        Assert.That(nav.Result.Kind, Is.EqualTo(TraceKind.None));
        Assert.That(nav.BeamShown, Is.False);
    }

    [Test]
    public void QuestEventsSelectAndDrop()
    {
        var (nav, state, saved) = Navigator(1);
        state.List.Add(Active(103));
        state.List.Add(Active(100, false, 0, 0));
        nav.OnQuestAccepted(state.List[0]);            // no navi: not selected
        Assert.That(nav.Result.QuestId, Is.EqualTo(0));
        nav.OnQuestAccepted(state.List[1]);
        Assert.That(nav.Result.QuestId, Is.EqualTo(100));
        nav.OnQuestAccepted(Active(102));              // one is selected already
        Assert.That(nav.Result.QuestId, Is.EqualTo(100));
        nav.OnQuestTracking(state.List[1] with { Tracking = 0 });
        Assert.That(nav.Result.QuestId, Is.EqualTo(0));
        Assert.That(saved, Is.EqualTo(new[] { 100, 0 }));
        nav.LoadTracked = () => 100;
        nav.OnCreateMyPc();
        Assert.That(nav.Result.QuestId, Is.EqualTo(100));
        nav.OnQuestDropped(100);
        Assert.That(nav.Result.QuestId, Is.EqualTo(0));
        nav.Map = null;
        nav.OnQuestAccepted(state.List[1]);
        Assert.That(nav.TraceQuestTarget(), Is.EqualTo(2));
        Assert.That(nav.Result.DistanceText, Is.EqualTo(TraceStrings.Default.Initializing));
    }
}
