using System.Xml.Linq;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The quest log's give-up button on the quest log movie: onGiveUpQuest, GiveUpQuest @0x141aa1b50 and OnGiveUpConfirm
/// @0x141aa1fa0 (uiquestdialog.cpp:1792-1832): only a taken quest with data, a confirm box for a time-limited one not yet
/// expired, the packet at once otherwise.
/// </summary>
public class QuestDialogGiveUpTests
{
    sealed class State : IQuestState
    {
        public readonly SortedDictionary<int, QuestRecord> ById = new();
        public IReadOnlyList<QuestRecord> Records => ById.Values.ToList();
        public QuestRecord Get(int questId) => ById.TryGetValue(questId, out var q) ? q : null;
        public bool CanStart(int questId) => false;
        public int PlayerLevel { get; set; } = 10;
        public long CharacterId => 7;
        public bool NpcFieldOpen(int npcId, int fieldId) => false;
        public void Add(int id, int state, bool expired = false) => ById[id] = new QuestRecord(id, state, 5, 0, 0, false, expired);
    }

    sealed class Folder : ICharacterFolder
    {
        public string Read(string file) => null;
        public void Write(string file, string text) { }
    }

    // 100 untimed, 101 an hour long (usePeriod 60), 102 with a reset weekday
    static QuestTable Table()
    {
        var t = new QuestTable("KR", _ => true);
        string Q(int id, string extra = "") =>
            $@"<quest id=""{id}""><basic chapterID=""10"" questType=""0"" standardLevel=""1"" {extra} /><require quest="""" /><start npc=""0"" /></quest>";
        t.AddQuests(XDocument.Parse("<ms2>" + Q(100) + Q(101, @"usePeriod=""60""") + Q(102, @"usePeriod=""mon""") + "</ms2>"));
        t.BuildIndexes();
        t.AddChapters(XDocument.Parse(@"<ms2><chapter id=""10"" name=""Ch10"" /></ms2>"));
        return t;
    }

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static UiHost Open(QuestDialogController c)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables)) { Folder = new Folder(), ClientStageW = 1920, ClientStageH = 1080 };
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return h;
    }

    [Test]
    public void GiveUpSendsOnlyForATakenQuestAndAsksFirstForATimedOne()
    {
        var c = new QuestDialogController();
        var h = Open(c);
        var state = new State();
        c.Log = new QuestLog(Table(), state);
        var sent = new List<int>();
        var boxes = new List<MessageBoxData>();
        c.SendGiveUp += sent.Add;
        c.ShowMessageBox = boxes.Add;
        void GiveUp(int id) => h.Player.External(h.Id, new object[] { "onGiveUpQuest", id });

        GiveUp(100);
        Assert.That((sent.Count, boxes.Count), Is.EqualTo((0, 0)), "no record: nothing");
        state.Add(100, 2);
        GiveUp(100);
        Assert.That((sent.Count, boxes.Count), Is.EqualTo((0, 0)), "a rewarded quest: nothing");
        state.Add(999, 1);
        GiveUp(999);
        Assert.That((sent.Count, boxes.Count), Is.EqualTo((0, 0)), "a quest the data lacks: nothing");

        state.Add(100, 1);
        GiveUp(100);
        Assert.That(sent, Is.EqualTo(new[] { 100 }), "an untimed taken quest goes at once");
        Assert.That(boxes, Is.Empty);

        foreach (int timed in new[] { 101, 102 })
        {
            sent.Clear(); boxes.Clear();
            state.Add(timed, 1);
            GiveUp(timed);
            Assert.That(sent, Is.Empty, $"{timed}: a time-limited quest asks first");
            Assert.That(boxes, Has.Count.EqualTo(1));
            Assert.That(boxes[0].Type, Is.EqualTo(1));
            Assert.That(boxes[0].Callback(1, boxes[0]), Is.True);
            Assert.That(sent, Is.Empty, $"{timed}: button 1 sends nothing");
            Assert.That(boxes[0].Callback(0, boxes[0]), Is.True);
            Assert.That(sent, Is.EqualTo(new[] { timed }), $"{timed}: button 0 sends the give-up");

            sent.Clear(); boxes.Clear();
            state.Add(timed, 1, expired: true);
            GiveUp(timed);
            Assert.That(sent, Is.EqualTo(new[] { timed }), $"{timed}: an expired one goes at once");
            Assert.That(boxes, Is.Empty);
        }
        Assert.That(h.RT.Errors, Is.Empty);
    }
}
