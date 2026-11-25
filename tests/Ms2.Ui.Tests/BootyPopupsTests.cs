using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// <see cref="BootyPopups"/> and <see cref="BootyPopup"/>: UIBootyPopup (uibootypopup.cpp) made with a popup cube
/// (functioncubeobject.cpp:574-582) and CubeMapInformationService::FUN_1410c8a90 (cubemapinformationservice_3.cpp:3491-3520)
/// showing its text for the nearest usable popup cube.
/// </summary>
public class BootyPopupsTests
{
    static ObjectData Obj(string attrs, string inner = "") =>
        ObjectData.Parse(XDocument.Parse($"<ms2><FunctionCube {attrs}>{inner}</FunctionCube></ms2>"), 1);

    const string Effect = "<firstAdditionalEffect firstAdditionalEffectID=\"50000011\" firstAdditionalEffectLevel=\"1\" firstAdditionalPopup=\"1\"/>";
    static readonly ObjectData Buff = Obj("ControlType=\"Switch\" StateChangeAction=\"ActionKey\"", Effect);
    static readonly ObjectData BuffByAttack = Obj("ControlType=\"Switch\" StateChangeAction=\"Attack\"", Effect);
    static readonly ObjectData Plain = Obj("ControlType=\"Switch\" StateChangeAction=\"ActionKey\"");

    sealed class Rig
    {
        public readonly FunctionCubes Cubes = new();
        public readonly BootyPopups Popups;
        public readonly List<string> Calls = new();
        public Rig()
        {
            Popups = new BootyPopups(Cubes) { Distance = 200, Duration = 3000, TextOf = id => ($"item {id}", $"buff {id}") };
            Popups.Opened = (_, p) =>
            {
                p.SetText = (n, d) => Calls.Add($"{p.ItemId} setText {n}|{d}");
                p.SetShow = on => Calls.Add($"{p.ItemId} setShow {on}");
            };
        }
        public FunctionCubes.Cube Place(int key, ObjectData o, float x, int item) => Cubes.Place(key, 5, o, item, 0, new Vector3(x, 0, 0));
    }

    [Test]
    public void APopupCubeMakesItsPopup150UpAndShowsIt()
    {
        var r = new Rig();
        var c = r.Place(1, Buff, 300, 70);
        r.Place(2, Plain, 600, 71);
        Assert.That(r.Popups.Count, Is.EqualTo(1), "only the object with firstAdditionalPopup");
        var p = r.Popups.Of(c.Id);
        Assert.That((p.Point, p.ItemId, p.Shown, p.HidePending, p.TextShown), Is.EqualTo((new Vector3(300, 0, 150), 70, true, true, false)));
        r.Cubes.Remove(c.Id);
        Assert.That((r.Popups.Count, p.Closed, p.Shown), Is.EqualTo((0, true, false)), "the cube's removal closes it");
    }

    [Test]
    public void TheNearestPopupCubeShowsItsTextUntilTheDurationThenWaitsForThePcToLeave()
    {
        var r = new Rig();
        r.Place(1, Buff, 0, 70);
        r.Popups.Tick(new Vector3(100, 0, 0), 1000);
        Assert.That(r.Calls, Is.EqualTo(new[] { "70 setText item 70|buff 70", "70 setShow True" }));
        r.Popups.Tick(new Vector3(100, 0, 0), 4000);
        Assert.That(r.Calls.Count, Is.EqualTo(2), "3000 ms is not past the duration");
        r.Popups.Tick(new Vector3(100, 0, 0), 4001);
        Assert.That(r.Calls[2], Is.EqualTo("70 setShow False"));
        r.Popups.Tick(new Vector3(100, 0, 0), 9000);
        Assert.That(r.Calls.Count, Is.EqualTo(3), "the same cube stays hidden");
        r.Popups.Tick(new Vector3(1000, 0, 0), 9100);
        Assert.That(r.Calls.Count, Is.EqualTo(3), "leaving it hides nothing more");
        r.Popups.Tick(new Vector3(100, 0, 0), 9200);
        Assert.That(r.Calls.Skip(3), Is.EqualTo(new[] { "70 setText item 70|buff 70", "70 setShow True" }), "coming back shows it again");
    }

    [Test]
    public void AnotherCubeHidesTheLastAndShowsItsOwn()
    {
        var r = new Rig();
        r.Place(1, Buff, 0, 70);
        r.Place(2, Buff, 1000, 71);
        r.Popups.Tick(new Vector3(50, 0, 0), 1000);
        r.Popups.Tick(new Vector3(950, 0, 0), 1100);
        Assert.That(r.Calls, Is.EqualTo(new[] { "70 setText item 70|buff 70", "70 setShow True", "70 setShow False", "71 setText item 71|buff 71", "71 setShow True" }));
    }

    [Test]
    public void ANearerUsableCubeWithoutThePopupHidesIt()
    {
        var r = new Rig();
        r.Place(1, Buff, 0, 70);
        r.Place(2, Plain, 150, 71);
        r.Place(3, BuffByAttack, 120, 72);
        r.Popups.Tick(new Vector3(10, 0, 0), 1000);
        r.Popups.Tick(new Vector3(140, 0, 0), 1100);
        Assert.That(r.Calls, Is.EqualTo(new[] { "70 setText item 70|buff 70", "70 setShow True", "70 setShow False" }),
            "the nearest usable cube is the plain one; an attack cube is not usable by the key");
        r.Popups.Tick(null, 1200);
        Assert.That(r.Calls.Count, Is.EqualTo(3), "no local player, no tick");
    }

    [Test]
    public void TheFramePlacesTheShownPopupCentredAbove()
    {
        var r = new Rig();
        var p = r.Popups.Of(r.Place(1, Buff, 0, 70).Id);
        Vector2? Project(Vector3 at) => new Vector2(at.X + 400, 300);
        p.Frame(true, Project, 0.5f, 101, 40);
        Assert.That((p.Visible, p.HidePending), Is.EqualTo((false, false)), "the first frame hides it, the text is not up");
        r.Popups.Tick(Vector3.Zero, 1000);
        p.Frame(true, Project, 0.5f, 101, 40);
        Assert.That(p.Visible, Is.True);
        Assert.That(p.Position, Is.EqualTo(new Vector2(200 - 50, 150 - 40 - 15)));
        p.Frame(true, _ => null, 0.5f, 101, 40);
        Assert.That(p.Visible, Is.False, "off the screen");
        p.Frame(true, Project, 0.5f, 101, 40);
        p.Frame(false, _ => null, 0.5f, 101, 40);
        Assert.That(p.Visible, Is.True, "no local player leaves it");
        r.Popups.Tick(Vector3.Zero, 5000);
        p.Frame(true, Project, 0.5f, 101, 40);
        Assert.That(p.Visible, Is.True, "a shown popup with its text down keeps its visibility (the movie's setShow hides it)");
    }

    /// <summary>
    /// Every install item of MS2_DATA and MS2_GMS2_DATA whose object has the popup flag: placed, it has its popup; its text is
    /// the item's name and its effect's tooltip description, with the duration line in whole minutes when the effect's level
    /// has data. Prints the counts.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryPopupItemHasItsPopupAndText(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var t = new GameTables(packs, "en");
        var bad = new List<string>();
        int items = 0, withDuration = 0, withTip = 0, usable = 0;
        var popupObjects = new SortedSet<int>();
        string durationFormat = t.Common(BootyPopup.BuffDurationText);
        Assert.That(durationFormat, Is.Not.Empty, "s_bootypopup_buff_duration");
        var ids = new SortedSet<int>();
        var names = packs.List("Xml").ToList();
        var idAttr = new Regex("<item id=\"(\\d+)\"", RegexOptions.Compiled);
        foreach (var n in names.Where(n => n.StartsWith("itemdata/", StringComparison.Ordinal)))
            foreach (Match m in idAttr.Matches(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", n))))
                ids.Add(int.Parse(m.Groups[1].Value));
        foreach (var n in names.Where(n => n.StartsWith("item/", StringComparison.Ordinal)))
            if (Regex.Match(n, @"/(\d+)\.xml$") is { Success: true } m) ids.Add(int.Parse(m.Groups[1].Value));
        foreach (int id in ids)
        {
            var d = t.Items.Get(id);
            if (d == null || d.InstallObjCode == 0) continue;
            var o = t.ObjectDataOf(d.InstallObjCode);
            if (o == null || !o.Cube.Enabled || o.Cube.FirstEffect.Popup == 0) continue;
            popupObjects.Add(d.InstallObjCode);
            var cubes = new FunctionCubes();
            var popups = new BootyPopups(cubes);
            var c = cubes.Place(1, d.Subtype, o, d.Id, 0, Vector3.Zero);
            if (c == null) { bad.Add($"{d.Id}: not placed"); continue; }
            if (popups.Of(c.Id) is not { ItemId: var pid } || pid != d.Id) { bad.Add($"{d.Id}: no popup"); continue; }
            items++;
            if (c.Usable) usable++;
            var (name, desc) = BootyPopup.Text(t, d.Id);
            if (name != t.ItemName(d.Id) || name == "") bad.Add($"{d.Id}: name '{name}'");
            uint eid = (uint)o.Cube.FirstEffect.EffectId;
            ushort lv = (ushort)o.Cube.FirstEffect.Level;
            string tip = t.AdditionalDescription(eid, lv, "tooltipDescription");
            if (tip != "") withTip++;
            if (!desc.StartsWith(tip, StringComparison.Ordinal)) bad.Add($"{d.Id}: description");
            if (t.Additional.GetLevel(eid, lv) is { } level)
            {
                withDuration++;
                string line = "\n" + ClientFormat.Format(durationFormat, (int)(level.DurationTick / 1000 / 60));
                if (desc != tip + line) bad.Add($"{d.Id}: duration line '{desc.Substring(tip.Length)}'");
            }
            else if (desc != tip) bad.Add($"{d.Id}: a line without the level");
        }
        TestContext.Progress.WriteLine($"{variable}: {popupObjects.Count} popup objects, {items} items, {withDuration} with the duration line, " +
            $"{withTip} with a tooltip, {usable} usable by the action key");
        Assert.That(bad, Is.Empty);
        Assert.That(items, Is.GreaterThan(0));
        Assert.That(withDuration, Is.GreaterThan(0));
        Assert.That(withTip, Is.EqualTo(items), "every popup effect has its tooltip text");
        Assert.That(usable, Is.EqualTo(items), "every popup cube is a usable cube, so the nearest cube search finds it");
    }

    /// <summary>
    /// The movie uibootypopup of MS2_DATA and MS2_GMS2_DATA with <see cref="BootyPopupController"/>: its root is the entity, the
    /// popup's text goes to setText (the title and description fields) and setShow, and the root has a size to centre by.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheMovieTakesTheTextAndShow(string variable)
    {
        var packs = TestData.Packs(RcGap.Data(variable));
        var src = TestData.Movies(packs);
        var popup = new BootyPopup { ItemId = 70 };
        var c = new BootyPopupController(popup);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true });
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(h.Entity, Is.Not.Null, "the root with setText");
        popup.SetTextShown(true, _ => ("Name 70", "Buff text"));
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That(rt.Errors, Is.Empty);
        var inner = h.Child("m_popup");
        Assert.That(inner, Is.Not.Null, "BootyPopup's m_popup (BootyEntity) holds the fields");
        var title = h.Child("m_title", inner);
        var desc = h.Child("m_desc", inner);
        Assert.That(title, Is.Not.Null);
        Assert.That(Ms2.Avm2.Rt.ToString(h.Get(title, "text")), Does.Contain("Name 70"));
        Assert.That(Ms2.Avm2.Rt.ToString(h.Get(desc, "text")), Does.Contain("Buff text"));
        var (w, hgt) = c.Size();
        Assert.That(w, Is.GreaterThan(0));
        Assert.That(hgt, Is.GreaterThan(0));
        TestContext.Progress.WriteLine($"{variable}: popup {w}x{hgt}");
        c.Place(100, 50);
        Assert.That(Ms2.Avm2.Rt.ToNumber(h.Get(h.Entity, "x")), Is.EqualTo(100));
        popup.SetTextShown(false, null);
        Assert.That(rt.Errors, Is.Empty);
    }
}
