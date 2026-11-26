using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The fame book of UserEnv modes 0xa-0xd (CSceneStateGame::OnUserEnv), the fame texts, the fame point board's dates and
/// tabs gate, and the fame contents notice's queue (UIFameContentsNotify), without game data.
/// </summary>
public class FameTests
{
    static UserEnvMessage Read(Action<ByteWriter> body)
    {
        var w = new ByteWriter();
        body(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        var m = UserEnvMessage.Read(r);
        Assert.That(r.Available, Is.Zero);
        return m;
    }

    static FameTable Table() => new(
        XDocument.Parse("<ms2><season name='SkyFotress' requireQuest='91000013'><fame alliance='DarkWind' officerNpcKind='100' symbol='DarkWind'/>" +
                        "<fame alliance='MapleUnion' officerNpcKind='101' symbol='MapleUnion'/></season>" +
                        "<season name='KritiasExped' requireQuest='50101050'><fame alliance='GreenHood_KritiasExped' officerNpcKind='107' symbol='GreenHood'/></season></ms2>"),
        XDocument.Parse("<ms2><fame alliance='MapleUnion' grade='1' requirePoint='0'/><fame alliance='MapleUnion' grade='2' requirePoint='10000'/>" +
                        "<fame alliance='MapleUnion' grade='3' requirePoint='30000'/><fame alliance='DarkWind' grade='1' requirePoint='0'/>" +
                        "<fame alliance='DarkWind' grade='2' requirePoint='5000'/><fame alliance='GreenHood_KritiasExped' grade='1' requirePoint='0'/></ms2>"),
        XDocument.Parse("<ms2><fame id='1001' alliance='DarkWind' grade='2'/><fame id='2001' alliance='MapleUnion' grade='2'/></ms2>"),
        _ => true);

    static string Common(string key) => key switch
    {
        "s_quest_alliance_darkwind" => "Dark Wind",
        "s_quest_alliance_mapleunion" => "Maple Alliance",
        "s_fame_point_board_max" => "MAX",
        "s_fame_contents_notify_opening" => "{0} mission started",
        "s_fame_contents_notify_ending" => "{0} mission complete",
        "s_fame_point_board_time1" => "{0} min ago",
        "s_fame_point_board_time2" => "{0}:{1}",
        "s_fame_point_board_time3" => "{0}-{1}-{2}",
        _ when key.StartsWith("s_fame_grade_", StringComparison.Ordinal) => "grade " + key[^1],
        _ => null,
    };

    [Test]
    public void UserEnvFameModesFillTheBook()
    {
        var book = new FameBook();
        var whole = Read(w => { w.WriteByte(0xa); w.WriteInt(2); w.WriteShort(1); w.WriteInt(700); w.WriteShort(2); w.WriteInt(12_000); });
        Assert.That(book.Apply(whole), Is.EqualTo(FameBook.Change.Points));
        Assert.That((book.GetFamePoints(1), book.GetFamePoints(2), book.GetFamePoints(3)), Is.EqualTo((700, 12_000, 0)));
        Assert.That(book.Apply(Read(w => { w.WriteByte(0xb); w.WriteShort(2); w.WriteInt(15_000); })), Is.EqualTo(FameBook.Change.Points));
        Assert.That((book.GetFamePoints(1), book.GetFamePoints(2)), Is.EqualTo((700, 15_000)), "0xb sets one alliance");
        book.Apply(Read(w => { w.WriteByte(0xa); w.WriteInt(1); w.WriteShort(3); w.WriteInt(5); }));
        Assert.That((book.GetFamePoints(1), book.GetFamePoints(3)), Is.EqualTo((0, 5)), "0xa replaces the whole map");

        var log = Read(w => { w.WriteByte(0xc); w.WriteInt(2); w.WriteInt(2001); w.WriteLong(1_700_000_500); w.WriteInt(1001); w.WriteLong(1_700_000_100); });
        Assert.That(book.Apply(log), Is.EqualTo(FameBook.Change.Log));
        Assert.That(book.Log.Select(e => (e.Key, e.Value)), Is.EqualTo(new[] { (1_700_000_100L, 1001), (1_700_000_500L, 2001) }), "time order");
        Assert.That(book.Apply(Read(w => { w.WriteByte(0xd); w.WriteInt(1002); w.WriteLong(1_700_000_300); })), Is.EqualTo(FameBook.Change.LogEntry));
        Assert.That(book.Log.Select(e => e.Value), Is.EqualTo(new[] { 1001, 1002, 2001 }));
        book.Apply(Read(w => { w.WriteByte(0xc); w.WriteInt(0); }));
        Assert.That(book.Log, Is.Empty, "0xc replaces the whole log");
        Assert.That(book.Apply(new UserEnvMessage.TitleAdded(1)), Is.EqualTo(FameBook.Change.None));
    }

    [Test]
    public void ProgressIsThePointsIntoTheGradeAndMaxAtTheLastGrade()
    {
        var book = new FameBook();
        var t = new FameTexts { Table = Table(), Book = book, CommonByKey = Common };
        Assert.That(t.Progress(2), Is.EqualTo((0, 10_000, "0 / 10,000")));
        book.Apply(new UserEnvMessage.FameByAlliance(false, new Dictionary<ushort, int> { [2] = 12_345 }));
        Assert.That(t.Progress(2), Is.EqualTo((2_345, 20_000, "2,345 / 20,000")));
        Assert.That(t.GradeName(2), Is.EqualTo("grade 2"));
        book.Apply(new UserEnvMessage.FameByAlliance(false, new Dictionary<ushort, int> { [2] = 30_000 }));
        Assert.That(t.Progress(2), Is.EqualTo((1, 1, "MAX")), "the max grade");
        book.Apply(new UserEnvMessage.FameByAlliance(false, new Dictionary<ushort, int> { [1] = 99_999 }));
        Assert.That(t.Progress(1), Is.EqualTo((1, 1, "MAX")));
        Assert.That((t.AllianceName(1), t.AllianceSymbol(1), t.AllianceSymbol(8)), Is.EqualTo(("Dark Wind", "darkwind", "greenhood")));
    }

    [Test]
    public void TheBoardOpensOnlyWithASeasonQuestRewarded()
    {
        var t = new FameTexts { Table = Table() };
        Assert.That(t.Table.AllianceIds, Is.EqualTo(new[] { 2, 1, 8 }), "fame.xml's order");
        Assert.That(t.AnyAllianceOpen(_ => false), Is.False);
        Assert.That(t.AnyAllianceOpen(q => q == 50101050), Is.True, "the expedition season's quest");
        Assert.That(t.AnyAllianceOpen(q => q == 12345), Is.False);
        Assert.That(new FameTexts().AnyAllianceOpen(_ => true), Is.False, "no fame.xml, no alliance");
    }

    [Test]
    public void TheBoardDateIsMinutesThenTimeThenDay()
    {
        long now = 1_800_000_000;
        Assert.That(FamePointBoardController.DateText(now, now - 59 * 60, Common), Is.EqualTo("59 min ago"));
        Assert.That(FamePointBoardController.DateText(now, now - 61 * 60 + 1, Common), Is.EqualTo("60 min ago"), "60 whole minutes is not over 60");
        var t2 = Ms2.Net.MsTime.LocalTm(now - 61 * 60);
        Assert.That(FamePointBoardController.DateText(now, now - 61 * 60, Common), Is.EqualTo($"{t2.Hour}:{t2.Minute}"));
        Assert.That(FamePointBoardController.DateText(now, now - 25 * 3600 + 1, Common), Is.EqualTo(Ms2.Net.MsTime.LocalTm(now - 25 * 3600 + 1) is var a ? $"{a.Hour}:{a.Minute}" : ""), "24 whole hours is not over 24");
        var t3 = Ms2.Net.MsTime.LocalTm(now - 25 * 3600);
        Assert.That(FamePointBoardController.DateText(now, now - 25 * 3600, Common), Is.EqualTo($"{t3.Year}-{t3.Month}-{t3.Day}"));
    }

    [Test]
    public void TheBoardTabsAreTheOpenAlliancesAndTheLogIsTheAlliancesOwn()
    {
        var book = new FameBook();
        book.Apply(new UserEnvMessage.FameLog(true, new Dictionary<long, int> { [100] = 1001, [200] = 2001, [300] = 9999 }));
        var c = new FamePointBoardController
        {
            Fame = new FameTexts { Table = Table(), Book = book, CommonByKey = Common },
            ServerTime = () => 400,
            TableString = (table, id) => table == FamePointBoardController.LogTable ? "log " + id : "",
            Words = s => s + "!",
        };
        var d = c.AllianceData(1);
        Assert.That((d["allianceName"], d["allianceGrade"], d["alliance"], d["famePoint"]), Is.EqualTo(("Dark Wind", "grade 1", "darkwind", "0 / 5,000")));
        var log = ((List<object>)d["log"]).Cast<Dictionary<string, object>>().ToList();
        Assert.That(log.Select(l => (string)l["msg"]), Is.EqualTo(new[] { "log 1001!" }), "only the alliance's entries, through the words");
        Assert.That(log[0]["date"], Is.EqualTo("5 min ago"));
        Assert.That(((List<object>)c.AllianceData(2)["log"]).Count, Is.EqualTo(1));
        Assert.That(((List<object>)c.AllianceData(8)["log"]), Is.Empty);
        Assert.That(c.OnCallback("onChangeTab", new object[] { 2.0 }), Is.True);
        Assert.That(c.Shown, Is.EqualTo(2));
        Assert.That(c.OnCallback("other", Array.Empty<object>()), Is.False);
    }

    sealed class Notify
    {
        public readonly FameContentsNotifyController C;
        public int Loads, Unloads;
        public Notify()
        {
            C = new FameContentsNotifyController
            {
                Fame = new FameTexts { Table = Table(), CommonByKey = Common },
                QuestAlliance = id => id is 10 or 11 ? 1 : id is 20 or 21 ? 2 : id == 30 ? 0 : null,
                QuestType = id => id switch { 10 or 20 => 9, 11 or 21 => 8, 30 => 9, 40 => 1, _ => null },
                LoadMovie = () => Loads++,
                UnloadMovie = () => Unloads++,
            };
        }
    }

    [Test]
    public void TheQueueHoldsEachBannerOnceAndLoadsTheMovie()
    {
        var n = new Notify();
        n.C.QueueOpening(10);
        Assert.That(n.Loads, Is.EqualTo(1));
        n.C.QueueOpening(10);
        n.C.QueueEnding(10);
        n.C.QueueOpening(20);
        n.C.QueueOpening(99);                       // no such quest
        Assert.That(n.C.Pending, Is.EqualTo(new[] { (1, true), (1, false), (2, true) }));
        Assert.That(n.Loads, Is.EqualTo(3), "with no movie each new banner asks for it (vfunc_2)");
        n.C.OnFadeInComplete();
        Assert.That(n.Loads, Is.EqualTo(4));
        new Notify().C.OnFadeInComplete();
        // without a movie the callback still drops the head
        Assert.That(n.C.OnCallback("onHideComplete", Array.Empty<object>()), Is.True);
        Assert.That(n.C.Pending.Count(), Is.EqualTo(2));
        n.C.OnCallback("onHideComplete", Array.Empty<object>());
        n.C.OnCallback("onHideComplete", Array.Empty<object>());
        Assert.That(n.C.Pending, Is.Empty);
        Assert.That(n.Unloads, Is.EqualTo(1), "the empty queue unloads the movie");
    }

    [Test]
    public void TheNoticeHasTheAllianceClassTheMessageAndTheProgress()
    {
        var n = new Notify();
        n.C.Fame.Book.Apply(new UserEnvMessage.FameByAlliance(true, new Dictionary<ushort, int> { [2] = 15_000 }));
        var o = n.C.Notice(2, false);
        Assert.That((o["alliance"], o["msg"], o["current"], o["max"], o["point"]), Is.EqualTo(("Ending_mapleunion", "Maple Alliance mission complete", 5_000, 20_000, "5,000 / 20,000")));
        Assert.That(n.C.Notice(1, true)["alliance"], Is.EqualTo("Opening_darkwind"));
        Assert.That(n.C.Notice(1, true)["msg"], Is.EqualTo("Dark Wind mission started"));
    }

    [Test]
    public void AllianceQuestStartsAndFieldOrAllianceQuestCompletesQueueBanners()
    {
        var n = new Notify();
        n.C.OnQuest(new QuestMessage.Start(10, 0, false, Array.Empty<long>()), false);      // alliance quest: opening
        n.C.OnQuest(new QuestMessage.Start(11, 0, false, Array.Empty<long>()), false);      // field quest: no banner on start
        n.C.OnQuest(new QuestMessage.Complete(11, 1, 0), false);                            // not known before: no Complete call
        n.C.OnQuest(new QuestMessage.Complete(21, 1, 0), true);                             // field quest: ending
        n.C.OnQuest(new QuestMessage.Complete(20, 1, 0), true);                             // alliance quest: ending (queued already)
        n.C.OnQuest(new QuestMessage.Start(40, 0, false, Array.Empty<long>()), false);      // other type
        n.C.OnQuest(new QuestMessage.Complete(40, 1, 0), true);
        n.C.OnQuest(new QuestMessage.Update(10, Array.Empty<long>()), true);
        Assert.That(n.C.Pending, Is.EqualTo(new[] { (1, true), (2, false) }));
        n.C.OnQuest(new QuestMessage.Start(30, 0, false, Array.Empty<long>()), false);      // alliance none: queued as the client does
        Assert.That(n.C.Pending.Last(), Is.EqualTo((0, true)));
        Assert.That(n.C.Notice(0, true)["alliance"], Is.EqualTo("Opening_none"));
    }
}
