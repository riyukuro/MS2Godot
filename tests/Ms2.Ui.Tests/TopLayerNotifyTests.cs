using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UITopLayerNotify (uitoplayernotify.cpp) with its movie: loaded by a message, shows it, unloads at onHideMessage.</summary>
public class TopLayerNotifyTests
{
    [Test]
    public void AMessageLoadsTheMovieShowsAndHides()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = TestData.Packs(d);
        var src = TestData.Movies(packs);
        var m = new UiModel(new GameTables(packs, "en"));
        var c = new TopLayerNotifyController();
        Player p = null;
        UiHost h = null;
        int loads = 0, unloads = 0;
        c.LoadMovie = () =>
        {
            loads++;
            var (rt, player) = src.Open(c.Movie, r => { r.Budget = 20_000_000; });
            p = player;
            p.StageW = 1920; p.StageH = 1080;
            h = new UiHost(p, c, m);
            Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        };
        c.UnloadMovie = () => { unloads++; h.Unload(); };

        // without the movie: kept, loaded, and shown by OnLoadComplete
        c.ShowMessage("Server maintenance in 10 minutes", 3000);
        Assert.That(loads, Is.EqualTo(1));
        Assert.That(h.Entity, Is.Not.Null);
        p.Tick();
        var texts = TooltipTests.TextFields(p.Stage).Select(t => t.Text).ToList();
        Assert.That(texts, Has.Some.Contains("Server maintenance in 10 minutes"));

        // vfunc_43: top centre, a tenth of the height down
        c.Layout(1920, 1080);

        // the movie's own timer ends the message: onHideMessage unloads it and forgets the message
        for (int f = 0; f < 2000 && unloads == 0; f++) p.Tick();
        Assert.That(h.Calls, Does.Contain("onHideMessage"));
        Assert.That(unloads, Is.EqualTo(1));
        Assert.That(h.Entity, Is.Null);
        Assert.That(c.Pending, Is.EqualTo(("", 0)));

        // the next message loads it again
        c.ShowMessage("again", 1000);
        Assert.That(loads, Is.EqualTo(2));
    }
}
