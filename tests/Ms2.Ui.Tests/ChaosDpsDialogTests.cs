using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The chaos damage meter on its movie (needs MS2_DATA): the party meter's body over the record manager's chaos group, its
/// option flag cleared by the close button and the frame update's three close conditions (uichaosdpsdialog.cpp). No test asserts a
/// language-specific string.</summary>
public class ChaosDpsDialogTests
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
        public bool On = true, Feature = true, OptionCleared;
        public readonly PartyBook Book = new();
        public readonly BattleRecordManager Records;
        public readonly List<string> Chat = new();
        public Env(int members = 3)
        {
            Records = new BattleRecordManager { Tick = () => Now, CharacterIdOf = id => id, NpcClassOf = _ => 6, ChaosRoomMeter = () => true };
            Book.BattleRecords = Records;
            var list = Enumerable.Range(1, members).Select(i => new PartyInfoMember(false, new PartyUser { CharacterId = i, Name = "M" + i, Level = (short)(10 + i), Job = 10 * i })).ToArray();
            Book.Apply(new PartyInfo(false, 7, 1, list, false, 0), 1, true);
        }
    }

    static ChaosDpsController Dialog(Env e)
    {
        var c = new ChaosDpsController(e.Book, e.Records)
        {
            SelfCharacterId = () => 2,
            ServerNow = () => new DateTime(2026, 11, 21, 13, 4, 5),
            PartyChat = e.Chat.Add,
            MeterOn = () => e.On,
            FeatureEnabled = () => e.Feature,
            ClearMeterOption = () => e.OptionCleared = true,
        };
        Open(c);
        return c;
    }

    [Test]
    public void TheOpenStartsTheChaosGroupAndRanksByTheChaosGroupsMeter()
    {
        var e = new Env();
        var c = Dialog(e);
        Assert.That((e.Records.IsGroupActive(ChaosDpsController.Group), e.Records.IsGroupActive(PartyDpsController.Group)), Is.EqualTo((true, false)));
        e.Records.StartGroup(PartyDpsController.Group);
        e.Records.RecordDamage(3, 99, new[] { new Hit(0, -3000) });
        e.Records.RecordDamage(2, 99, new[] { new Hit(0, -1234) });
        e.Records.StopGroup(PartyDpsController.Group);
        c.RebuildRanking();
        Assert.That(c.Ranking.Select(m => m.CharacterId), Is.EqualTo(new long[] { 3, 2, 1 }));
        Assert.That(c.TypeName, Is.Not.EqualTo(new PartyDpsController(e.Book, e.Records).TypeName));
    }

    [Test]
    public void TheModeCyclesSevenAndTheNoticeGoesToThePartyChat()
    {
        var e = new Env();
        var c = Dialog(e);
        for (int i = 1; i <= 7; i++) { c.OnCallback("onTextToggle", Array.Empty<object>()); Assert.That(c.Mode, Is.EqualTo(i % 7)); }
        e.Records.RecordDamage(1, 99, new[] { new Hit(0, -70) });
        c.RebuildRanking();
        c.OnCallback("onNotice", Array.Empty<object>());
        Assert.That(e.Chat.Single(), Does.Contain("M1").And.Contain("70"));
    }

    [Test]
    public void OnCloseDialogClearsTheOptionStopsTheChaosGroupAndCloses()
    {
        var e = new Env();
        var c = Dialog(e);
        e.Records.StartGroup(PartyDpsController.Group);
        Assert.That(c.OnCallbackBeforeDialog("OnCloseDialog", Array.Empty<object>()), Is.True);
        Assert.That((e.OptionCleared, e.Records.IsGroupActive(ChaosDpsController.Group), e.Records.IsGroupActive(PartyDpsController.Group), c.Closing),
            Is.EqualTo((true, false, true, true)));
    }

    [Test]
    public void EscapeClosesWithoutClearingTheOptionOrStoppingTheGroup()
    {
        var e = new Env();
        var c = Dialog(e);
        c.EscapeClose();
        Assert.That((c.Closing, e.OptionCleared, e.Records.IsGroupActive(ChaosDpsController.Group)), Is.EqualTo((true, false, true)));
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void TheFrameUpdateClosesWhenTheMeterIsOffTheFeatureIsOffOrOutOfAParty(bool on, bool feature, bool inParty)
    {
        var e = new Env { On = on, Feature = feature };
        var c = Dialog(e);
        if (!inParty) e.Book.Apply(new PartyLeave(1, true), 1, true);
        c.Update(10_000);
        Assert.That(c.Closing, Is.True);
    }

    [Test]
    public void TheFrameUpdateKeepsAnOpenMeterOpenAndRefreshesEveryHundredMilliseconds()
    {
        var e = new Env();
        var c = Dialog(e);
        c.Update(10_000);
        e.Records.RecordDamage(3, 99, new[] { new Hit(0, -500) });
        c.Update(10_050);
        Assert.That(c.Ranking.Select(m => m.CharacterId).First(), Is.Not.EqualTo(3), "under 100 ms: the ranking is not rebuilt");
        c.Update(10_100);
        Assert.That((c.Closing, c.Ranking.First().CharacterId), Is.EqualTo((false, 3L)));
    }
}
