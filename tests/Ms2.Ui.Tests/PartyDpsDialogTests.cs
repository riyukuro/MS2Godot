using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The party damage meter on its movie (needs MS2_DATA): the ranking from the battle record manager's party group,
/// the modes, the reset, close and notice calls and the member removal (uipartydialog.cpp:1191-1414).</summary>
public class PartyDpsDialogTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static void Open(UiController c)
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= TestData.Packs(d);
        src ??= TestData.Movies(packs);
        tables ??= new GameTables(packs, "en");
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors) + " missing: " + string.Join("; ", rt.Missing.Keys));
        for (int i = 0; i < 5; i++) p.Tick();
    }

    sealed class Env
    {
        public uint Now = 1_000;
        public readonly PartyBook Book = new();
        public readonly BattleRecordManager Records;
        public readonly List<string> Chat = new();
        public Env(int members = 3)
        {
            Records = new BattleRecordManager { Tick = () => Now, CharacterIdOf = id => id, NpcClassOf = _ => 6 };
            Book.BattleRecords = Records;
            var list = Enumerable.Range(1, members).Select(i => new PartyInfoMember(false, User(i))).ToArray();
            Book.Apply(new PartyInfo(false, 7, 1, list, false, 0), 1, true);
        }
    }

    static PartyUser User(long id) => new() { CharacterId = id, Name = "M" + id, Level = (short)(10 + id), Job = 10 * (int)id };

    static PartyDpsController Dialog(Env e)
    {
        var c = new PartyDpsController(e.Book, e.Records)
        {
            SelfCharacterId = () => 2,
            ServerNow = () => new DateTime(2026, 11, 21, 13, 4, 5),
            PartyChat = e.Chat.Add,
        };
        Open(c);
        return c;
    }

    static object Get(UiHost h, object o, params string[] path)
    {
        foreach (var k in path) o = h.Get(o, k);
        return o;
    }

    [Test]
    public void TheOpenStartsTheGroupAndRanksTheMembersByTheMeter()
    {
        var e = new Env();
        var c = Dialog(e);
        var h = c.Host;
        Assert.That((e.Records.IsGroupActive(1), e.Records.GroupStartTick(1)), Is.EqualTo((true, 1000u)), "StartGroupIfInactive");
        e.Records.RecordDamage(3, 99, new[] { new Hit(0, -3000) });
        e.Records.RecordDamage(2, 99, new[] { new Hit(1, -1234) });
        e.Now = 2_000;
        c.Update(e.Now);                                    // arms the timer
        c.Update(e.Now + 100);
        Assert.That(c.Ranking.Select(m => m.CharacterId), Is.EqualTo(new long[] { 3, 2, 1 }));
        var top = Get(h, h.Entity, "m_TopRank", "data");
        Assert.That((Rt.ToString(h.Get(top, "name")), Rt.ToString(h.Get(top, "meterText")), Rt.ToNumber(h.Get(top, "meterValue"))),
            Is.EqualTo(("M3", "3,000", 3000.0)), "rank 1 goes to SetTopRank, the text grouped");
        Assert.That(Rt.ToInt32(Get(h, h.Entity, "m_members", "numChildren")), Is.EqualTo(2), "ranks 2 and 3 are rows");
        Assert.That(Rt.ToString(Get(h, h.Entity, "m_MyRank", "m_nameText", "text")), Is.EqualTo("M2"), "SetMyRank: the own row");
        Assert.That(Rt.ToString(Get(h, h.Entity, "m_MyRank", "m_rankText", "text")), Is.EqualTo("2"));
        Assert.That(Rt.ToNumber(h.Get(h.Entity, "m_bottomMeterValue")), Is.Zero, "the last member's value");
        Assert.That(Rt.ToString(Get(h, h.Entity, "m_timer", "m_timerText", "text")), Is.EqualTo("00:00:01"));
    }

    [Test]
    public void TheToggleCyclesTheSevenModesAndModeSixRanksAscending()
    {
        var e = new Env();
        var c = Dialog(e);
        var h = c.Host;
        string title0 = Rt.ToString(Get(h, h.Entity, "m_titleBar", "m_titleText", "text"));
        Assert.That(title0, Is.Not.Empty);
        e.Records.RecordDamage(99, 1, new[] { new Hit(0, -50) });
        e.Records.RecordDamage(99, 3, new[] { new Hit(0, -10) });
        for (int i = 0; i < 6; i++) c.OnCallback("onTextToggle", Array.Empty<object>());
        Assert.That((c.Mode, Rt.ToBoolean(h.Get(h.Entity, "m_ascendingMeterValue"))), Is.EqualTo((6, true)));
        Assert.That(Rt.ToString(Get(h, h.Entity, "m_titleBar", "m_titleText", "text")), Is.Not.EqualTo(title0));
        c.RebuildRanking();
        Assert.That(c.Ranking.Select(m => m.CharacterId), Is.EqualTo(new long[] { 2, 3, 1 }), "damage taken: the least first");
        c.OnCallback("onTextToggle", Array.Empty<object>());
        Assert.That((c.Mode, Rt.ToBoolean(h.Get(h.Entity, "m_ascendingMeterValue"))), Is.EqualTo((0, false)), "7 wraps to 0");
    }

    [Test]
    public void TheRatesShowTwoDecimalsWithAPercentSign()
    {
        var e = new Env();
        e.Records.StartGroup(1);
        e.Records.RecordDamage(1, 99, new[] { new Hit(0, -1), new Hit(1, -1), new Hit(2, 0) });
        Assert.That(PartyDpsController.MeterText(e.Records, 1, 1, 4), Is.EqualTo("66.67%"));
        Assert.That(PartyDpsController.MeterText(e.Records, 1, 1, 5), Is.EqualTo("50.00%"));
        Assert.That(PartyDpsController.MeterText(e.Records, 1, 9, 0), Is.Empty, "no record");
    }

    [Test]
    public void ResetCloseNoticeAndTheMemberRemoval()
    {
        var e = new Env(4);
        var c = Dialog(e);
        var h = c.Host;
        e.Records.RecordDamage(1, 99, new[] { new Hit(0, -70) });
        c.RebuildRanking();
        e.Now = 1_000 + 65_000;
        c.OnCallback("onNotice", Array.Empty<object>());
        string notice = e.Chat.Single();
        Assert.That(notice, Does.Contain("M1").And.Contain("M4").And.Contain("70").And.Contain("01:05"), notice);

        c.OnCallback("onReset", Array.Empty<object>());
        Assert.That((e.Records.GroupStartTick(1), e.Records.GetRecord(1, 1).Amount(BattleRecord.Dealt)), Is.EqualTo((66_000u, 0L)));

        Assert.That(Rt.ToInt32(Get(h, h.Entity, "m_members", "numChildren")), Is.EqualTo(3));
        e.Book.Apply(new PartyLeave(4, false), 1, true);
        c.RemoveMemberIfUnderLimit();
        Assert.That(Rt.ToInt32(Get(h, h.Entity, "m_members", "numChildren")), Is.EqualTo(2), "under ten: the last row goes");

        Assert.That(c.OnCallbackBeforeDialog("OnCloseDialog", Array.Empty<object>()), Is.True);
        Assert.That((c.Closing, e.Records.IsGroupActive(1)), Is.EqualTo((true, false)), "the close stops the group");
    }

    [Test]
    public void TheHudCloseKeepsTheGroupAndAReopenShowsTheTallies()
    {
        var e = new Env();
        var c = Dialog(e);
        e.Records.RecordDamage(1, 99, new[] { new Hit(0, -300) });
        e.Now = 31_000;
        c.CloseKeepingTheGroup();                           // Ctrl+Q or Escape: vfunc_67, no StopGroup
        Assert.That((c.Closing, e.Records.IsGroupActive(1)), Is.EqualTo((true, true)));
        var again = Dialog(e);                              // OnLoadComplete: StartGroupIfInactive keeps the run
        again.RebuildRanking();
        Assert.That((e.Records.GroupStartTick(1), e.Records.GetRecord(1, 1).Amount(BattleRecord.Dealt)), Is.EqualTo((1_000u, 300L)));
        Assert.That(Rt.ToString(Get(again.Host, again.Host.Entity, "m_timer", "m_timerText", "text")), Is.EqualTo("00:00:30"));
    }

    [Test]
    public void TheRollOverShowsTheTitleBarAndTheTimer()
    {
        var e = new Env();
        var c = Dialog(e);
        var h = c.Host;
        Assert.That(Rt.ToBoolean(Get(h, h.Entity, "m_timer", "visible")), Is.False, "configUI hides the timer");
        double x = Rt.ToNumber(h.Get(h.Entity, "x")) + Rt.ToNumber(Get(h, h.Entity, "m_TopRank", "x")) + 20;
        double y = Rt.ToNumber(h.Get(h.Entity, "y")) + Rt.ToNumber(Get(h, h.Entity, "m_TopRank", "y")) + 10;
        h.Player.MouseMove(x, y);                           // the pointer over the meter: ROLL_OVER, onMouseRollOver
        for (int i = 0; i < 30; i++) h.Player.Tick();
        Assert.That((Rt.ToString(h.Get(h.Entity, "currentLabel")), Rt.ToBoolean(Get(h, h.Entity, "m_timer", "visible"))), Is.EqualTo(("show", true)));
        Assert.That(Rt.ToNumber(Get(h, h.Entity, "m_titleBar", "alpha")), Is.EqualTo(1.0), "the title bar faded in");
        Assert.That(Rt.ToString(Get(h, h.Entity, "m_titleBar", "m_titleText", "text")), Is.Not.Empty);
    }

    [Test]
    public void OutOfAPartyTheMeterCloses()
    {
        var e = new Env(2);
        var c = Dialog(e);
        e.Book.Apply(new PartyLeave(2, false), 1, true);
        c.Update(5_000);
        Assert.That(c.Closing, Is.True);
    }
}
