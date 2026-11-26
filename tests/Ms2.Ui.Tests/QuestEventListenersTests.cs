using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIQuestEventDispatcher's routing to the quest log's and the minimap's slots (uiquesteventlistener.cpp:223).</summary>
public class QuestEventListenersTests
{
    sealed class NoQuests : IQuestState
    {
        public IReadOnlyList<QuestRecord> Records => Array.Empty<QuestRecord>();
        public QuestRecord Get(int questId) => null;
        public bool CanStart(int questId) => false;
        public int PlayerLevel { get; set; } = 10;
        public long CharacterId => 7;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
    }

    sealed class Rewarded : IQuestState
    {
        public readonly Dictionary<int, QuestRecord> ById = new();
        public IReadOnlyList<QuestRecord> Records => ById.Values.ToList();
        public QuestRecord Get(int questId) => ById.TryGetValue(questId, out var q) ? q : null;
        public bool CanStart(int questId) => false;
        public int PlayerLevel { get; set; } = 10;
        public long CharacterId => 7;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
    }

    // 1 a general quest of chapter 10, 2 .. 6 the types the dispatcher sends elsewhere, 7 type 1 (general), 9 no data
    static QuestData Data(int id) => id switch
    {
        1 => new QuestData { Type = 0, Chapter = 10 },
        2 => new QuestData { Type = 3 }, 3 => new QuestData { Type = 4 }, 4 => new QuestData { Type = 5 },
        5 => new QuestData { Type = 7 }, 6 => new QuestData { Type = 10 },
        7 => new QuestData { Type = 1, Chapter = 20 },
        _ => null,
    };

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static MinimapController OpenMinimap()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) };
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(c.IsClosed, Is.False);
        return c;
    }

    static MinimapController mm;
    static ulong now = 1000;

    /// <summary>The minimap's refresh kinds 1 - 4 (one per 100 ms tick): the kind 4 pass clears m_210.</summary>
    static void Cycle()
    {
        for (int i = 0; i < 4; i++) { mm.Process(now); now += 100; }
        Assert.That(mm.EntitiesInvalid, Is.False);
    }

    static bool Invalidates(QuestEventListeners l, Action<QuestEventListeners> fire)
    {
        fire(l);
        bool v = mm.EntitiesInvalid;
        Cycle();
        return v;
    }

    [Test]
    public void EachEventReachesTheSlotsTheDispatcherSendsItTo()
    {
        mm = OpenMinimap();
        Cycle();
        var l = new QuestEventListeners { Data = Data, Minimap = () => mm };
        int[] general = { 1, 7 }, other = { 2, 3, 4, 5, 6 };

        // slot 1, 2, 3 (start, reward, progress): the general types with data only
        foreach (int id in general)
        {
            Assert.That(Invalidates(l, x => x.Started(id)), Is.True, $"start {id}");
            Assert.That(Invalidates(l, x => x.Rewarded(id)), Is.True, $"reward {id}");
            Assert.That(Invalidates(l, x => x.Progress(id)), Is.True, $"progress {id}");
        }
        foreach (int id in other.Append(9))
        {
            Assert.That(Invalidates(l, x => x.Started(id)), Is.False, $"start {id}");
            Assert.That(Invalidates(l, x => x.Rewarded(id)), Is.False, $"reward {id}");
            Assert.That(Invalidates(l, x => x.Progress(id)), Is.False, $"progress {id}");
        }
        // slot 5 (give-up, expiry): every quest with data; slot 7 (result): every quest
        foreach (int id in general.Concat(other))
            Assert.That(Invalidates(l, x => x.Removed(id)), Is.True, $"removed {id}");
        Assert.That(Invalidates(l, x => x.Removed(9)), Is.False);
        Assert.That(Invalidates(l, x => x.Result(9, 0)), Is.True);
        Assert.That(Invalidates(l, x => x.Tracking(1)), Is.False, "the minimap has no slot 6");

        // the pins of a log not made yet stay with the listener, then go to the log once it is there
        Assert.That(l.Pinned, Is.EqualTo(new[] { 1, 7 }));
        var c = new QuestDialogController { Log = new QuestLog(new QuestTable("KR", _ => true), new NoQuests()) };
        int rewards = 0;
        c.HasLocalPlayer = () => { rewards++; return false; };
        l.QuestLog = () => c;
        l.Started(1);
        l.Started(3);
        Assert.That(c.Log.Pinned, Is.EqualTo(new[] { 1 }));
        Assert.That(l.Pinned, Is.EqualTo(new[] { 1, 7 }));
        l.Rewarded(7);
        l.Rewarded(4);
        l.Rewarded(9);
        Assert.That(rewards, Is.EqualTo(1), "slot 2: the general type with data only");
    }

    /// <summary>
    /// A reward closes its chapter only on a log with its movie. CloseChapter @0x141aa2520 (uiquestdialog.cpp:1910) reads
    /// the sub-tab through GetButtonBar("m_subTab"), null while m_30 is null (uidailymissiondialog.cpp:687): before the
    /// first open and after every close (OnEndClose, uidialog.cpp:894 -> Unload, uiswfentity.cpp:241). GetSubTab then
    /// gives 3, GetFlags null, and nothing is set or kept for later; a reopen shows the old flag, and a chapter seen the
    /// first time starts on IsSubTabOne (:1935).
    /// </summary>
    [Test]
    public void ARewardWithoutTheMovieLeavesTheChapterFlags()
    {
        var table = new QuestTable("KR", _ => true);
        table.AddQuests(System.Xml.Linq.XDocument.Parse(@"<ms2>
            <quest id=""1""><basic chapterID=""10"" questType=""0"" standardLevel=""10"" /></quest>
            <quest id=""2""><basic chapterID=""20"" questType=""0"" standardLevel=""10"" /></quest></ms2>"));
        table.BuildIndexes();
        var state = new Rewarded();
        state.ById[1] = new QuestRecord(1, 2, 5, 9, 0, true, false);
        state.ById[2] = new QuestRecord(2, 2, 5, 9, 0, true, false);
        var l = new QuestEventListeners { Data = Data };

        // not made yet: the reward goes nowhere; the log made later starts chapter 10 open on the in-progress sub-tab
        l.Rewarded(1);
        var c = new QuestDialogController { Log = new QuestLog(table, state) { SubTab = 1 } };
        l.QuestLog = () => c;
        Assert.That(c.Log.IsChapterOpen(10), Is.True, "a chapter seen the first time: IsSubTabOne");

        // made, its flag seen open, then closed (no movie): the reward sets nothing, the reopen shows the old flag
        l.Rewarded(1);
        Assert.That(c.Log.SubTab, Is.EqualTo(3), "GetSubTab without m_subTab");
        c.Log.SubTab = 1;
        Assert.That(c.Log.IsChapterOpen(10), Is.True, "the flag kept from before the close");

        // the same reward on the sub-tab the movie gives closes it (the open log's path)
        c.Log.CloseChapter(10);
        Assert.That(c.Log.IsChapterOpen(10), Is.False);

        // a chapter rewarded while closed and never seen: the default, on whichever sub-tab the reopen reads
        c.Log.SubTab = 3;
        l.Rewarded(7);
        Assert.That(Data(7).Chapter, Is.EqualTo(20));
        c.Log.SubTab = 1;
        Assert.That(c.Log.IsChapterOpen(20), Is.True);
        c.Log.SubTab = 0;
        Assert.That(c.Log.IsChapterOpen(20), Is.False);
    }
}
