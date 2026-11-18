using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;
using System.Text.Json.Nodes;

namespace Ms2.Ui.Tests;

/// <summary>The login scene's movie (uiscenelogin) run by its own ActionScript with UISceneLogin's C++ side (needs MS2_DATA).</summary>
public class SceneLoginTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static (UiHost, SceneLoginController, Player, Runtime) Load(bool background)
    {
        var model = new UiModel(new GameTables(Packs(), "en"));
        var c = new SceneLoginController();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        c.Enter();
        if (background) c.BackgroundLoaded();
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        c.SceneShown();
        for (int i = 0; i < 5; i++) { c.Tick(); p.Tick(); }
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, p, rt);
    }

    [Test]
    public void TheTitleShowsOnceTheBackgroundHasLoadedAndTheLoginDialogOpensAtFullProgress()
    {
        var (h, c, _, rt) = Load(true);
        Assert.That(c.Progress, Is.EqualTo(0xffff));
        Assert.That(c.Shown, Is.True);
        Assert.That(h.Get(h.Entity, "m_title"), Is.Not.Null, "the logo (logo_ani) is made by show()");
        int opened = 0;
        c.OpenLoginDialogs += () => opened++;
        c.Tick();
        Assert.That(opened, Is.EqualTo(0), "the dialogs open once");
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>
    /// The age rating marks: export_use_grade (component_v1) places the class "grade" of the locale's CommonImage, whose
    /// timeline places two sub-images of its atlas directly; each is a Bitmap of the sub-image's size.
    /// </summary>
    [Test]
    public void TheGradeMarksAreBitmapsOfTheCommonImageAtlas()
    {
        var (h, _, _, _) = Load(true);
        var bitmaps = new List<AsBitmap>();
        void Walk(AsDisplayObject d)
        {
            if (d is AsBitmap b) bitmaps.Add(b);
            if (d is AsContainer dc) for (int i = 0; i < dc.numChildren; i++) Walk(dc.getChildAt(i));
        }
        Walk((AsDisplayObject)h.Get(h.Entity, "m_grade"));
        Assert.That(bitmaps, Has.Count.EqualTo(2));
        foreach (var b in bitmaps)
        {
            // the image resource the Bitmap stands for: a DefineSubImage of the CommonImage_I atlas
            var scope = b.bitmapData.ImageScope;
            Assert.That(scope, Is.Not.Null);
            var sub = (scope.Layout["subImages"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => J.Iv(o["id"]) == b.bitmapData.ImageId);
            Assert.That(sub, Is.Not.Null, "the id is a sub-image of the scope");
            var file = (string)(scope.Layout["images"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => J.Iv(o["id"]) == J.Iv(sub["image"]))?["file"];
            Assert.That(file, Does.StartWith("CommonImage_I").IgnoreCase);
            Assert.That((b.bitmapData.width, b.bitmapData.height), Is.EqualTo((99, 117)));
        }
    }

    [Test]
    public void WithoutTheBackgroundTheProgressStopsShort()
    {
        var (h, c, _, _) = Load(false);
        Assert.That(c.Progress, Is.EqualTo(0x7fff));
        Assert.That(c.Shown, Is.False);
        Assert.That(h.Get(h.Entity, "m_title"), Is.Null);
    }
}
