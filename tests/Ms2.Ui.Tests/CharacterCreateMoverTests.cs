using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The create page's category accordion (uicharactercreatedialog, CharacterCreatePage) run by its own
/// ActionScript (needs MS2_DATA).</summary>
public class CharacterCreateMoverTests
{
    /// <summary>A C++ side that answers nothing: the page is driven by the test.</summary>
    class Quiet : UiController
    {
        public override string Movie => "uicharactercreatedialog";
        protected override string EntityMethod => "onChangeGenderComplete";
        protected override bool IsDialog => false;
        public override bool OnCallback(string name, object[] args) => true;
    }

    /// <summary>
    /// Opening the first category plays "_down" on the movers below it. At the end (frame 45) the page adds the mover's
    /// dummy offset to the mover and goes back to "_default" (onMoverEndAni). The dummy's placement there has an identity
    /// matrix, so the rewind puts the dummy back at 0 and the category stays where it was moved to, not twice as far.
    /// </summary>
    [Test]
    public void MoverDummyReturnsToItsPlacementOnRewind()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var c = new Quiet();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(new GameTables(packs, "en")));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        h.Invoke(h.Entity, "initData", 0, h.Obj(("name", "x"), ("iconPath", "")));
        var rows = new List<object>();
        for (int i = 0; i < 7; i++)
            rows.Add(h.Obj(("tab", 0), ("category", i), ("itemname", "n" + i), ("itemcolor", 4294901760.0), ("itemid", i.ToString())));
        h.Invoke(h.Entity, "buildCurrentItems", rows);
        for (int i = 0; i < 30; i++) p.Tick();

        var list = h.Get(h.Get(h.Get(h.Entity, "m_content"), "m_page"), "m_categoryList");
        var mover = (AsMovieClip)h.Get(h.Get(list, "1"), "mover");
        var dummy = h.Get(mover, "dummy");
        Assert.That(mover.Frame, Is.EqualTo(0), "back at _default");
        Assert.That(Rt.ToNumber(h.Get(dummy, "y")), Is.EqualTo(0), "the dummy at its placement");
        Assert.That(Rt.ToNumber(h.Get(mover, "y")), Is.EqualTo(340).Within(1), "the mover moved by the tween's offset once");
    }
}
