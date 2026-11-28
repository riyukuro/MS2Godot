using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The personal damage meter on its movie (needs MS2_DATA): the record added on load, the nine modes, the reset and close calls,
/// the 100 ms refresh and the close when the option stops holding (uiindividualdpsdialog.cpp). No test asserts a language-specific string.</summary>
public class IndividualDpsDialogTests
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
        public bool On = true;
        public bool OptionCleared;
        public long Self = 2;
        public readonly BattleRecordManager Records;
        public Env() => Records = new BattleRecordManager { Tick = () => Now, CharacterIdOf = id => id, NpcClassOf = _ => 6 };
    }

    static IndividualDpsController Dialog(Env e)
    {
        var c = new IndividualDpsController(e.Records)
        {
            SelfCharacterId = () => e.Self,
            MeterOn = () => e.On,
            ClearMeterOption = () => e.OptionCleared = true,
        };
        Open(c);
        return c;
    }

    [Test]
    public void TheOpenStartsTheGroupAddsTheOwnRecordAndWritesTheTitleAndTheTime()
    {
        var e = new Env();
        var c = Dialog(e);
        Assert.That((e.Records.IsGroupActive(IndividualDpsController.Group), e.Records.HasRecord(IndividualDpsController.Group, 2)), Is.EqualTo((true, true)));
        Assert.That((c.Mode, c.Title, c.TimeText), Is.Not.Null);
        Assert.That(c.Title, Is.Not.Empty);
        Assert.That(c.TimeText, Is.EqualTo(e.Records.ElapsedTimeText(IndividualDpsController.Group)));
    }

    [Test]
    public void WithoutALocalPlayerTheOpenDoesNothingMore()
    {
        var e = new Env { Self = 0 };
        var c = Dialog(e);
        Assert.That(e.Records.IsGroupActive(IndividualDpsController.Group), Is.False);
        Assert.That(c.Title, Is.Null);
    }

    [Test]
    public void TheMeterTextIsTheRecordManagersValueOfTheOwnRecordInTheMode()
    {
        var e = new Env();
        var c = Dialog(e);
        e.Records.RecordDamage(2, 99, new[] { new Hit(0, -3000) });
        c.UpdateMeterText();
        Assert.That(c.MeterText, Is.EqualTo(PartyDpsController.MeterText(e.Records, IndividualDpsController.Group, 2, 0)));
        Assert.That(c.MeterText, Is.Not.Empty);
        for (int mode = 1; mode < IndividualDpsController.ModeCount; mode++)
        {
            c.OnCallback("onTextToggle", Array.Empty<object>());
            c.UpdateMeterText();
            Assert.That((c.Mode, c.MeterText), Is.EqualTo((mode, PartyDpsController.MeterText(e.Records, IndividualDpsController.Group, 2, mode))), "mode " + mode);
        }
    }

    [Test]
    public void TheModeCyclesThroughNineAndBackToZeroAndRetitles()
    {
        var e = new Env();
        var c = Dialog(e);
        var titles = new HashSet<string> { c.Title };
        for (int i = 1; i <= 9; i++)
        {
            Assert.That(c.OnCallback("onTextToggle", Array.Empty<object>()), Is.True);
            Assert.That(c.Mode, Is.EqualTo(i % 9));
            titles.Add(c.Title);
        }
        Assert.That(titles.Count, Is.GreaterThan(1), "each mode names itself in the title");
    }

    [Test]
    public void OnResetRestartsTheGroup()
    {
        var e = new Env();
        var c = Dialog(e);
        e.Now = 5_000;
        Assert.That(c.OnCallback("onReset", Array.Empty<object>()), Is.True);
        Assert.That(e.Records.GroupStartTick(IndividualDpsController.Group), Is.EqualTo(5_000u));
    }

    [Test]
    public void OnCloseDialogClearsTheOptionStopsTheGroupAndCloses()
    {
        var e = new Env();
        var c = Dialog(e);
        Assert.That(c.OnCallbackBeforeDialog("OnCloseDialog", Array.Empty<object>()), Is.True);
        Assert.That((e.OptionCleared, e.Records.IsGroupActive(IndividualDpsController.Group), c.Closing), Is.EqualTo((true, false, true)));
        Assert.That(c.OnCallbackBeforeDialog("somethingElse", Array.Empty<object>()), Is.False);
    }

    [Test]
    public void TheOptionsCloseKeepsTheGroupRunningAndTheOptionAsItIs()
    {
        var e = new Env();
        var c = Dialog(e);
        c.CloseFromOptions();
        Assert.That((c.Closing, e.OptionCleared, e.Records.IsGroupActive(IndividualDpsController.Group)), Is.EqualTo((true, false, true)));
    }

    [Test]
    public void TheFrameUpdateRefreshesEveryHundredMillisecondsAndClosesWhenTheMeterIsOff()
    {
        var e = new Env();
        var c = Dialog(e);
        c.Update(10_000);                                   // arms the timer
        e.Records.RecordDamage(2, 99, new[] { new Hit(0, -777) });
        c.Update(10_050);
        Assert.That(c.MeterText, Is.Not.EqualTo(PartyDpsController.MeterText(e.Records, 0, 2, 0)), "under 100 ms: not refreshed");
        c.Update(10_100);
        Assert.That(c.MeterText, Is.EqualTo(PartyDpsController.MeterText(e.Records, 0, 2, 0)));
        e.On = false;
        c.Update(10_101);
        Assert.That(c.Closing, Is.True);
    }
}
