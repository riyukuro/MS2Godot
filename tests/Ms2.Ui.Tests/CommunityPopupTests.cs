using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// UICommunityPopup (0x4f5c, uicommunitypopup.cpp) run on its movie across the game data roots: a line loads the movie and
/// is laid out at the lane's end, scrolls and wraps, the next queued line replaces it after 120 server seconds, the movie
/// unloads when none is left, and a click opens the address and ends the line; and the PopupText user command reaches it.
/// </summary>
public class CommunityPopupTests
{
    static IEnumerable<(string Label, string Dir, string Lang)> DataRoots()
    {
        var d1 = Environment.GetEnvironmentVariable("MS2_DATA");
        if (!string.IsNullOrEmpty(d1) && Directory.Exists(d1))
            yield return ("MS2_DATA", d1, RegionOf(d1));
        var d2 = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (!string.IsNullOrEmpty(d2) && Directory.Exists(d2) && !string.Equals(d1, d2, StringComparison.Ordinal))
            yield return ("MS2_GMS2_DATA", d2, "en");
    }

    static string RegionOf(string dir)
    {
        using var p = new PackSet(dir);
        return p.Exists("Xml", "string/kr/stringcommon.xml") ? "kr" : "en";
    }

    static int Count(string s, string part)
    {
        int n = 0;
        for (int i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    [Test]
    public void LinesScrollQueueRotateAfterTheirTimeAndCloseOnEveryDataRoot()
    {
        var roots = DataRoots().ToList();
        if (roots.Count == 0) Assert.Ignore("MS2_DATA is not set");
        foreach (var (label, dir, lang) in roots)
        {
            using var packs = new PackSet(dir);
            var src = TestData.Movies(packs);
            var model = new UiModel(new GameTables(packs, lang));
            long now = 5000;
            var opened = new List<string>();
            var c = new CommunityPopupController { ServerTime = () => now, OpenUrl = opened.Add };
            Player p = null;
            UiHost h = null;
            Runtime rt = null;
            int loads = 0, unloads = 0;
            c.LoadMovie = () =>
            {
                loads++;
                (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
                p.StageW = 1280; p.StageH = 720;
                h = new UiHost(p, c, model);
                Assert.That(h.Load(), Is.True, $"{label} load: " + string.Join("; ", rt.Errors));
            };
            c.UnloadMovie = () => { unloads++; h.Unload(); };
            AsTextField Field() => TooltipTests.TextFields(p.Stage).Single(t => t.InstanceName == "textField");

            // the first line loads the movie and is laid out at x 275, repeated to fill the lane
            c.Show("Alpha line", "https://example.com/a");
            Assert.That((loads, c.IsOpen, c.Current), Is.EqualTo((1, true, ("Alpha line" + CommunityPopupController.Padding, "https://example.com/a"))), label);
            p.Tick();
            Assert.That(rt.Errors, Is.Empty, label);
            Assert.That(c.TextWidth, Is.GreaterThan(0), label);
            int repeat = (c.TextWidth + CommunityPopupController.Lane) / c.TextWidth + 1;
            Assert.That(repeat, Is.GreaterThanOrEqualTo(2), label);
            Assert.That(Count(Field().Text, "Alpha line"), Is.EqualTo(repeat), label);
            Assert.That(Field().x, Is.EqualTo(CommunityPopupController.Lane), label);
            Assert.That(Field().width, Is.GreaterThan(c.TextWidth * repeat), label);

            // a second line queues
            c.Show("Beta line", "https://example.com/b");
            Assert.That((c.Queued, c.Current.Text), Is.EqualTo((1, "Alpha line" + CommunityPopupController.Padding)), label);

            // 50 pixels a second to the left; once one copy has passed the line wraps to 0
            c.Tick(0.1);
            Assert.That(c.X, Is.EqualTo(270f).Within(1e-3), label);
            Assert.That(Field().x, Is.EqualTo(270), label);
            bool wrapped = false;
            for (int i = 0; i < 10_000 && !wrapped; i++)
            {
                float before = c.X;
                c.Tick(0.1);
                if (c.X > before) { wrapped = true; Assert.That(c.X, Is.EqualTo(0f), label); }
                else Assert.That(c.X, Is.GreaterThanOrEqualTo(-c.TextWidth - 5), label);
            }
            Assert.That(wrapped, Is.True, label);

            // 120 seconds is still the line; past it the queued one replaces it in the same movie
            now += CommunityPopupController.LineSeconds;
            c.Tick(0.1);
            Assert.That(c.Current.Text, Does.StartWith("Alpha"), label);
            now += 1;
            c.Tick(0.1);
            Assert.That((c.Current, c.Queued, loads, unloads), Is.EqualTo((("Beta line" + CommunityPopupController.Padding, "https://example.com/b"), 0, 1, 0)), label);
            Assert.That(c.X, Is.EqualTo(CommunityPopupController.Lane - 50 * 0.1f).Within(1e-3), label);   // FUN_140bea730 ends with a scroll by the frame's time
            p.Tick();
            Assert.That(Count(Field().Text, "Beta line"), Is.GreaterThanOrEqualTo(2), label);
            Assert.That(Field().Text, Does.Not.Contain("Alpha"), label);

            // none left: the movie unloads
            now += CommunityPopupController.LineSeconds + 1;
            c.Tick(0.1);
            Assert.That((c.IsOpen, unloads, c.Current), Is.EqualTo((false, 1, ("", ""))), label);

            // a click opens the address and the line ends at the next tick
            c.Show("Gamma", "https://example.com/c");
            Assert.That(loads, Is.EqualTo(2), label);
            c.OnClick();
            Assert.That(opened, Is.EqualTo(new[] { "https://example.com/c" }), label);
            c.Tick(0.1);
            Assert.That((c.IsOpen, unloads), Is.EqualTo((false, 2)), label);

            // vfunc_43 runs on the loaded movie
            c.Show("Delta", "");
            c.Layout(1280, 720);
            Assert.That(rt.Errors, Is.Empty, label);
        }
    }

    [Test]
    public void ThePopupTextCommandShowsItsFirstTwoWords()
    {
        // ShowPopupText (usercommandcli.cpp:2099): argument 0 the text, argument 1 the address, the arguments split at " "
        var seen = new List<(string, string)>();
        var unported = new List<string>();
        var hooks = new UserCommands.Hooks { ShowPopupText = (t, u) => seen.Add((t, u)), Unported = (n, a) => { unported.Add(n); return 1; } };
        var u = UserCommands.Empty;
        Assert.That(u.Execute("/PopupText Hello https://example.com/x", hooks), Is.EqualTo(1));
        Assert.That(u.Execute("/PopupText  Only", hooks), Is.EqualTo(1));
        Assert.That(u.Execute("/PopupText", hooks), Is.EqualTo(1));
        Assert.That(u.Execute("/PopupText", new UserCommands.Hooks()), Is.EqualTo(1), "1 without the popup");
        Assert.That(seen, Is.EqualTo(new[] { ("Hello", "https://example.com/x"), ("Only", ""), ("", "") }));
        Assert.That(unported, Is.Empty);
    }
}
