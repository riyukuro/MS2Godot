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
            packs = TestData.Packs(RcGap.Data("MS2_DATA"));
            src = TestData.Movies(packs);
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
    // ---- UIAvatarRidee: the mount portrait of the main player info ----

    static UiHost LoadCapturing(UiController c, UiModel m, List<string> urls)
    {
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        p.ImageSize = url => { urls.Add(url); return null; };
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return h;
    }

    /// <summary>
    /// UIMainPlayerInfo::vfunc_oa0_20 (uimainplayerinfo.cpp:795-835): a useRidingUI row builds the UIAvatarRidee, the slot mode
    /// goes to 1 and LoadRenderTarget has the movie's RideeInfo clip load "img://&lt;id&gt;:faceView/100/100"; the ride off
    /// deletes it and loads nothing; a mount without useRidingUI has none.
    /// </summary>
    [Test]
    public void AUseRidingUiMountLoadsTheRideePortrait()
    {
        var m = Model();
        var urls = new List<string>();
        var c = new MainPlayerInfoController();
        var h = LoadCapturing(c, m, urls);
        string face = $"img://{h.Id}:faceView/";
        Assert.That(urls.Where(u => u.StartsWith(face)), Is.Empty, "no portrait before the ride");
        var changes = new List<RideeRow>();
        c.AvatarRideeChanged += changes.Add;

        Ride(m, h, Row(50600171));
        Assert.That(c.AvatarRidee, Is.Null, "a mount without useRidingUI has no UIAvatarRidee");
        Assert.That(urls.Where(u => u.StartsWith(face)), Is.Empty);
        Ride(m, h, null);

        var row = Row(99910090);
        Ride(m, h, row);
        TestContext.WriteLine("urls: " + string.Join(", ", urls));
        Assert.That(c.AvatarRidee, Is.SameAs(row));
        Assert.That(urls.Where(u => u.StartsWith(face)), Is.EqualTo(new[] { face + "100/100" }), "the RideeInfo clip's render target");
        Assert.That(changes.Last(), Is.SameAs(row));

        Ride(m, h, null);
        Assert.That(c.AvatarRidee, Is.Null, "the ride off deletes m_ridee");
        Assert.That(changes.Last(), Is.Null);
        Assert.That(urls.Count(u => u.StartsWith(face)), Is.EqualTo(1), "slot mode 0 loads no render target");
    }

    /// <summary>UIModelView::vfunc_15 (uimodelview.cpp:320-327) and UIAvatarRidee::vfunc_4 (GetPreviewKind(0) = 1).</summary>
    [Test]
    public void ThePortraitsFrustumAndAngleFollowTheClient()
    {
        Assert.That(RideeAvatar.Frustum(100, 100), Is.EqualTo((0.5f, 0.5f)));
        Assert.That(RideeAvatar.Frustum(200, 100), Is.EqualTo((0.5f, 0.25f)));
        Assert.That(RideeAvatar.PreviewAngle(RideeAvatar.PreviewKind), Is.EqualTo(270f));
        Assert.That((RideeAvatar.PreviewAngle(6), RideeAvatar.PreviewAngle(9), RideeAvatar.PreviewAngle(11)), Is.EqualTo((135f, 270f, 300f)));
        Assert.That((RideeAvatar.PreviewAngle(1, 7), RideeAvatar.PreviewAngle(1, 10)), Is.EqualTo((90f, 334f)));
    }

    /// <summary>
    /// Every riding row of a data set with useRidingUI (the rows whose ride builds a UIAvatarRidee): its &lt;faceCamera&gt;
    /// gives three lookAtPosition and three cameraPosition values (RideeData::FaceCamera, FUN_140783b00,
    /// unit_140781d80_ridingtable.cpp:506-527) at two different points, the eye on the +x side of the target (where the
    /// preview angle's clockwise 270 degrees turns the model's front), and its kfm for gender 1 is in the model packs
    /// (AvatarRidee's "urn:gamebryo-animation:" + GetKfmName(1)).
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRidingUiRowHasAFaceCameraAndAModel(string env)
    {
        var ps = TestData.Packs(RcGap.Data(env));
        string[] archives = { "Resource/Model/Npc", "Resource/Model/Map", "Resource/Model/Item", "Resource/Model/Effect" };
        var ids = ps.List("Xml").Where(e => e.StartsWith("riding/") && e.Count(ch => ch == '/') == 1 && e.EndsWith(".xml"))
            .Select(e => int.TryParse(e[7..^4], out var id) ? id : -1).Where(id => id > 0).OrderBy(id => id).ToList();
        var ui = new List<int>();
        var bad = new List<string>();
        int faced = 0, all = 0;
        foreach (var id in ids)
        {
            var doc = Ms2.Formats.Text.PackXml.Parse(ps.Read("Xml", $"riding/{id:D8}.xml"));
            if (doc.Root?.Element("riding")?.Element("basic") == null) continue;
            all++;
            var d = RideData.Load(ps, id);
            bool hasFace = doc.Root.Element("riding").Element("faceCamera") != null;
            if (hasFace) faced++;
            if (!d.UseRidingUI) continue;
            ui.Add(id);
            var r = d.UiRow;
            if (!hasFace) bad.Add($"{id}: no faceCamera");
            else if (r.FaceLookAt == r.FaceCamera) bad.Add($"{id}: eye {r.FaceCamera} at its target");
            else if (r.FaceCamera.X <= r.FaceLookAt.X) bad.Add($"{id}: eye {r.FaceCamera} not on the +x side of {r.FaceLookAt}");
            var kfm = d.KfmFor(1);
            if (string.IsNullOrEmpty(kfm)) bad.Add($"{id}: no kfm");
            else if (!archives.Any(a => ps.FindByFileName(a, kfm.ToLowerInvariant() + ".kfm") != null)) bad.Add($"{id}: kfm {kfm} not in the model packs");
            TestContext.WriteLine($"  {id}: kfm {kfm} lookAt {r.FaceLookAt} camera {r.FaceCamera}");
        }
        TestContext.WriteLine($"{env}: {all} riding rows, {faced} with a faceCamera, {ui.Count} with useRidingUI");
        foreach (var b in bad) TestContext.WriteLine("  BAD " + b);
        Assert.That(ui, Is.Not.Empty);
        Assert.That(bad, Is.Empty);
    }
}
