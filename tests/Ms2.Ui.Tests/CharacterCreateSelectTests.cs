using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The job pick page (uicharactercreateselectdialog) run by its own ActionScript with UICharacterCreateSelectDialog's
/// C++ side (needs MS2_DATA).</summary>
public class CharacterCreateSelectTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        return packs;
    }

    static (UiHost, CharacterCreateSelectController) Load(int keepJob)
    {
        var c = new CharacterCreateSelectController { KeepJob = keepJob };
        c.Load(Packs(), "en");
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        Tick(h, 60);
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    static void Tick(UiHost h, int n) { for (int i = 0; i < n; i++) h.Player.Tick(); }

    [Test]
    public void KrDisplayRowIsTheHighestEnabledFeature()
    {
        var c = new CharacterCreateSelectController();
        c.Load(Packs(), "en");
        // the restored client's job page: twelve tabs, only the beginner tab disabled, a random start among six jobs
        Assert.That(c.Jobs.Select(j => j.JobCode), Is.EqualTo(new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 1 }));
        Assert.That(c.Jobs.Where(j => !j.Enabled).Select(j => j.JobCode), Is.EqualTo(new[] { 1 }));
        Assert.That(c.RandomJobs, Is.EqualTo(new[] { 10, 20, 30, 40, 50, 80 }));
        Assert.That(c.SelectJob, Is.EqualTo(10));
        Assert.That(c.Jobs.All(j => j.Movie.EndsWith(".usm") && j.Name.Length > 0 && j.Desc.Length > 0), Is.True);
    }

    [Test]
    public void EveryJobTabHasItsNameAndDescription()
    {
        var c = new CharacterCreateSelectController();
        c.Load(Packs(), "en");
        Assert.That(c.Jobs, Is.Not.Empty);
        Assert.That(c.Jobs.Where(j => j.Name.Length == 0).Select(j => j.JobCode), Is.Empty, "tabs without a job name");
    }

    [Test]
    public void TabChangeThenCreateSelectsTheJobAfterThePopdown()
    {
        var (h, c) = Load(30);
        Assert.That(c.ShownJob, Is.EqualTo(30));
        var content = h.Get(h.Entity, "m_content");
        Assert.That(Rt.ToString(h.Get(h.Get(h.Get(content, "m_jobInfo"), "m_name"), "text")), Is.EqualTo(c.Jobs[2].Name));
        // the knight tab: arrTab index 0, the first renderer of m_tab (more than six tabs: even indices on the top bar)
        var b = ((AsDisplayObject)h.Invoke(h.Get(content, "m_tab"), "getButtonAt", 0)).GetBounds(null);
        h.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
        Tick(h, 5);
        Assert.That(c.ShownJob, Is.EqualTo(10));
        int picked = 0;
        c.SelectCreateJob += j => picked = j;
        h.Invoke(content, "onClickCreate", (object)null);
        Tick(h, 120);
        Assert.That(picked, Is.EqualTo(10), "calls: " + string.Join(", ", h.Calls));
    }

    [Test]
    public void PrevClosesWithoutAJob()
    {
        var (h, c) = Load(30);
        bool closed = false; int picked = 0;
        c.Closed += () => closed = true;
        c.SelectCreateJob += j => picked = j;
        h.Invoke(h.Entity, "onClickPrev", (object)null);
        Tick(h, 120);
        Assert.That(closed, Is.True);
        Assert.That(picked, Is.EqualTo(0));
    }
}
