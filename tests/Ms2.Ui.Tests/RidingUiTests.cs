using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using Ms2Godot.Movement;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The riding row's HUD fields (CRideeData, riding/%08d.xml &lt;basic&gt;) and the status event 0xc listeners
/// (MyPC::vfunc_120 NotifyStatusChanged(0xc, 1), mypc.cpp:3317; uistatuseventlistener.cpp:335-338). Needs MS2_DATA.
/// Rows: 99910090 (battle, hidePCInfo 1, useRidingUI, showRideeNameTag, tag/bar offsets -50), 99910320 (battle,
/// hidePCInfo 2, useRidingUI, nameTagHide), 50600171 (default, kfm2 for gender 0, useRidingNavi).
/// </summary>
public class RidingUiTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static KeyTable keys;

    static PackSet Packs()
    {
        if (packs == null)
        {
            packs = new PackSet(RcGap.Data("MS2_DATA"));
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
            keys = new KeyTable(packs, "en");
        }
        return packs;
    }

    static RideeRow Row(int id) => RideData.Load(Packs(), id).UiRow;

    static UiModel Model()
    {
        Packs();
        var m = new UiModel(tables) { Keys = keys };
        UiFixture.Apply(m);
        m.ServerTime = () => new DateTime(2026, 1, 1, 9, 5, 0);
        return m;
    }

    static UiHost Load(UiController c, UiModel m)
    {
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return h;
    }

    static void Ride(UiModel m, UiHost h, RideeRow row)
    {
        m.Player.Riding = row != null;
        m.Player.Ridee = row;
        m.Notify(StatusEvent.Riding);
        for (int i = 0; i < 10; i++) h.Player.Tick();
    }

    static bool Visible(UiHost h) => Rt.ToBoolean(h.Get(h.Entity, "visible"));

    static IEnumerable<AsTextField> Texts(AsDisplayObject o)
    {
        if (o is AsTextField t) yield return t;
        if (o is AsContainer c) foreach (var k in c.Children) foreach (var x in Texts(k)) yield return x;
    }

    // ---- the row ----

    [Test]
    public void TheRowGivesTheHudFields()
    {
        var a = Row(99910090);
        Assert.That((a.HidePCInfo, a.UseRidingUI, a.ShowRideeNameTag, a.NameTagHide), Is.EqualTo((1, true, true, false)));
        var b = Row(99910320);
        Assert.That((b.HidePCInfo, b.UseRidingUI, b.NameTagHide), Is.EqualTo((2, true, true)));
        var c = Row(50600171);
        Assert.That((c.HidePCInfo, c.UseRidingUI, c.UseRidingNavi), Is.EqualTo((0, false, true)));
        var d = RideData.Load(Packs(), 99910090);
        Assert.That((d.NameTagOffsetY, d.HpBarOffsetY), Is.EqualTo((-50f, -50f)));
    }

    /// <summary>CRideeData::GetKfmName(gender) (unit_140781d80_ridingtable.cpp): kfm2 for gender 0, else kfm.</summary>
    [Test]
    public void TheKfmFollowsTheRidersGender()
    {
        var d = RideData.Load(Packs(), 50600171);
        Assert.That(d.KfmFor(0), Is.EqualTo("50600171_UGC_Zhangyixing_M"));
        Assert.That(d.KfmFor(1), Is.EqualTo("50600171_UGC_Zhangyixing"));
        var e = RideData.Load(Packs(), 50600147);
        Assert.That(e.KfmFor(0), Is.EqualTo(e.KfmFor(1)), "no kfm2: kfm for both");
    }

    /// <summary>LoadRidingEffectDummy (ridingtable.cpp:784-829): the common rows, with riding/effectdummy/%08d.xml parsed
    /// over them; 99910090 seats its rider on Eff_UI, which only the dummies name.</summary>
    [Test]
    public void TheRowCarriesTheCommonDummiesAndItsOwn()
    {
        var plain = RideData.Load(Packs(), 50600171).EffectDummies;
        Assert.That(plain.Select(x => x.Name), Is.EqualTo(new[] { "Eff_Head", "Eff_Body", "Eff_Foot", "Eff_UI", "Eff_Damage", "Eff_Head_World", "Eff_Body_World", "Eff_Foot_World" }));
        var d = RideData.Load(Packs(), 99910090);
        Assert.That(d.RideBone, Is.EqualTo("Eff_UI"));
        var ui = Ms2.Formats.World.EffectDummyTable.Find(d.EffectDummies, "Eff_UI");
        Assert.That((ui.ParentNode, ui.Translate.Z, ui.ApplyWorldTransform, ui.ApplyLocalScale), Is.EqualTo(("Scene Root", 250f, true, false)),
            "the row's own Eff_UI, 250 up (the common one is 180)");
    }

    [Test]
    public void TheRulesFollowTheClient()
    {
        var hide1 = Row(99910090); var hide2 = Row(99910320); var plain = Row(50600171);
        Assert.That((RidingUi.PcInfoShown(null), RidingUi.PcInfoShown(plain), RidingUi.PcInfoShown(hide1), RidingUi.PcInfoShown(hide2)),
            Is.EqualTo((true, true, false, false)));
        Assert.That((RidingUi.MainInfoShown(hide1), RidingUi.MainInfoShown(hide2)), Is.EqualTo((true, false)));
        Assert.That((RidingUi.SlotMode(true, hide1), RidingUi.SlotMode(false, hide1), RidingUi.SlotMode(true, plain)), Is.EqualTo((1, 0, 0)));
        Assert.That(RidingUi.KeyActionAllowed(hide1, 0x61a81), Is.False);
        Assert.That(RidingUi.KeyActionAllowed(hide1, 0x61a83), Is.True, "only 400001/400002/400005");
        Assert.That(RidingUi.KeyActionAllowed(plain, 0x61a81), Is.True);
        Assert.That((RidingUi.RidingBeam(plain), RidingUi.RidingBeam(hide1), RidingUi.RidingBeam(null)), Is.EqualTo((true, false, false)));
        Assert.That(hide2.ClosesOnRide, Is.EqualTo(RideeRow.ClosedOnRide));
        Assert.That(hide1.ClosesOnRide, Is.Empty);
    }

    /// <summary>CStringTable 0x31 is stringRideeName (table/string.xml), the name the riding slots show.</summary>
    [Test]
    public void TheRideeNameComesFromTable49()
    {
        Packs();
        Assert.That(tables.StringTableText(0x31, 99910090), Is.Not.Empty);
    }

    // ---- the listeners ----

    [Test]
    public void TheExpBarHidesWhileTheMountHidesThePcInfo()
    {
        var m = Model();
        var h = Load(new ExpBarController(), m);
        Assert.That(Visible(h), Is.True);
        Ride(m, h, Row(50600171));
        Assert.That(Visible(h), Is.True, "hidePCInfo 0");
        Ride(m, h, Row(99910090));
        Assert.That(Visible(h), Is.False, "hidePCInfo 1");
        Ride(m, h, null);
        Assert.That(Visible(h), Is.True, "ride off");
    }

    [Test]
    public void TheGameMenuHidesWhileTheMountHidesThePcInfo()
    {
        var m = Model();
        var h = Load(new GameMenuController(), m);
        Assert.That(Visible(h), Is.True);
        Ride(m, h, Row(99910090));
        Assert.That(Visible(h), Is.False);
        Ride(m, h, null);
        Assert.That(Visible(h), Is.True);
    }

    [Test]
    public void TheMainPlayerInfoHidesOnlyForHidePcInfoTwo()
    {
        var m = Model();
        var h = Load(new MainPlayerInfoController(), m);
        Assert.That(Visible(h), Is.True);
        Ride(m, h, Row(99910090));
        Assert.That(Visible(h), Is.True, "hidePCInfo 1 keeps the main player info");
        Ride(m, h, null);
        Ride(m, h, Row(99910320));
        Assert.That(Visible(h), Is.False, "hidePCInfo 2 hides it (SetHideReason(4, false))");
        Ride(m, h, null);
        Assert.That(Visible(h), Is.True);
    }

    [Test]
    public void AUseRidingUiMountShowsTheRidingSlotsWithTheRideeName()
    {
        var m = Model();
        var h = Load(new MainPlayerInfoController(), m);
        var name = tables.StringTableText(0x31, 99910090);
        Assert.That(Texts(h.Player.Root).Any(t => t.Text == name), Is.False);
        Ride(m, h, Row(99910090));
        Assert.That(Texts(h.Player.Root).Any(t => t.InstanceName == "m_tag" && t.Text == name), Is.True, "rideeName " + name + " in the tag");
    }
}
