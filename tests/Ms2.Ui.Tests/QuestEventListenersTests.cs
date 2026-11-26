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
}
