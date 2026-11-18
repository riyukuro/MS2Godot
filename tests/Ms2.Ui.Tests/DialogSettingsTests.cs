using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIDialog's settings file (uidialog.cpp vfunc_4 / 5 / 53 / 54 / 43 / 18) on the quest log movie.</summary>
public class DialogSettingsTests
{
    sealed class MemoryFolder : ICharacterFolder
    {
        public readonly Dictionary<string, string> Files = new();
        public string Read(string file) => Files.TryGetValue(file, out var t) ? t : null;
        public void Write(string file, string text) => Files[file] = text;
    }

    [TestCase(437.0, "437")]
    [TestCase(0.2276041, "0.227604")]
    [TestCase(1234567.0, "1.23457e+06")]
    [TestCase(0.0001, "0.0001")]
    [TestCase(0.00001, "1e-05")]
    [TestCase(-12.5, "-12.5")]
    [TestCase(0.0, "0")]
    public void FloatsAreWrittenAsPrintfG(double v, string expected) => Assert.That(DialogSettings.FormatG(v), Is.EqualTo(expected));

    [Test]
    public void ChildAttributesReuseTheFirstChild()
    {
        var s = new DialogSettings("QuestDialog");
        s.Set("transform", "x", 10f);
        s.Set("transform", "y", 20.5f);
        s.Set("tab", "properLevelWorld", 1);
        var back = DialogSettings.Parse("QuestDialog", s.ToString());
        float x = 0, y = 0, missing = -1; int w = 0;
        back.Get("transform", "x", ref x);
        back.Get("transform", "y", ref y);
        back.Get("transform", "rateX", ref missing);
        back.Get("tab", "properLevelWorld", ref w);
        Assert.That((x, y, missing, w), Is.EqualTo((10f, 20.5f, -1f, 1)));
        Assert.That(DialogSettings.Parse("Other", s.ToString()), Is.Null);
        Assert.That(DialogSettings.Parse("QuestDialog", "not xml"), Is.Null);
    }

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static UiHost Open(MemoryFolder folder)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var c = new QuestDialogController();
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables)) { Folder = folder, ClientStageW = 1920, ClientStageH = 1080 };
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();      // startOpen's tween (160 ms) ends where the dialog was placed
        return h;
    }

    static (double X, double Y) Pos(UiHost h) => (Rt.ToNumber(h.Get(h.Entity, "x")), Rt.ToNumber(h.Get(h.Entity, "y")));
    static void Call(UiHost h, string name) => h.Player.External(h.Id, new object[] { name });

    [Test]
    public void ADraggedQuestLogIsSavedAndOpensWhereItWasLeft()
    {
        var folder = new MemoryFolder();
        var h = Open(folder);
        var aligned = Pos(h);
        Assert.That(folder.Files, Is.Empty, "nothing is written before a drag or a close");

        Call(h, "OnStartDrag");
        h.Set(h.Entity, "x", 100.0);
        h.Set(h.Entity, "y", 50.0);
        Call(h, "OnStopDrag");
        var s = DialogSettings.Parse("QuestDialog", folder.Read("QuestDialog.xml"));
        Assert.That(s, Is.Not.Null);
        int edit = 0; float x = 0, y = 0, rx = -1, ry = -1, world = -1;
        s.Get("transform", "editAlign", ref edit);
        s.Get("transform", "x", ref x); s.Get("transform", "y", ref y);
        s.Get("transform", "rateX", ref rx); s.Get("transform", "rateY", ref ry);
        int w = -1; s.Get("tab", "properLevelWorld", ref w); world = w;
        Assert.That((edit, x, y), Is.EqualTo((1, 100f, 50f)));
        Assert.That(rx, Is.EqualTo(100f / 1920f).Within(1e-5));
        Assert.That(ry, Is.EqualTo(50f / 1080f).Within(1e-5));
        Assert.That(world, Is.EqualTo(1), "ResetFilters: proper level on for the world tab");

        // opened again: placed from its rates, on the stage, so the layout keeps it there
        var again = Open(folder);
        Assert.That(Pos(again).X, Is.EqualTo(100).Within(0.05));
        Assert.That(Pos(again).Y, Is.EqualTo(50).Within(0.05));
        Assert.That(Pos(again), Is.Not.EqualTo(aligned));

        // a rate above 1 (m_60) re-aligns
        folder.Files["QuestDialog.xml"] = folder.Read("QuestDialog.xml").Replace($"rateX=\"{DialogSettings.FormatG(rx)}\"", "rateX=\"1.5\"");
        var far = Open(folder);
        Assert.That(Pos(far).X, Is.EqualTo(aligned.X).Within(0.05));
    }

    [Test]
    public void AResizeKeepsAPlacedDialogsRatioToTheStage()
    {
        var folder = new MemoryFolder();
        var first = Open(folder);
        Call(first, "OnStartDrag");
        first.Set(first.Entity, "x", 100.0);
        first.Set(first.Entity, "y", 50.0);
        Call(first, "OnStopDrag");

        var h = Open(folder);
        h.Player.StageW = 960; h.Player.StageH = 540;
        h.Controller.Resize(1920, 1080, 960, 540);
        Assert.That(Pos(h).X, Is.EqualTo(50).Within(0.05));
        Assert.That(Pos(h).Y, Is.EqualTo(25).Within(0.05));
    }

    [Test]
    public void CloseSavesTheTabsAndUnloadsAfterTheCloseTween()
    {
        var folder = new MemoryFolder();
        var h = Open(folder);
        var c = (QuestDialogController)h.Controller;
        c.SetTab(2);
        bool unloaded = false;
        c.Unloaded += () => unloaded = true;
        Assert.That(c.IsClosed, Is.False);

        Call(h, "OnCloseDialog");
        Assert.That(c.IsClosed, Is.False, "closing: the close tween runs (m_c0 1)");
        Assert.That(folder.Read("QuestDialog.xml"), Is.Not.Null, "vfunc_4 on close");
        for (int i = 0; i < 60 && !unloaded; i++) h.Player.Tick();
        Assert.That(unloaded, Is.True, "startClose's tween ends with OnEndClose");
        Assert.That(c.IsClosed, Is.True);
        Assert.That(c.OpenTab, Is.EqualTo(2), "UIQuestDialog::vfunc_67 keeps the tab (m_180)");
    }
}
