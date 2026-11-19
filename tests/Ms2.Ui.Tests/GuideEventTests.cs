using System.Diagnostics;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Guide;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The guide event pane (UIGuideEvent, uiguideeventarrow.cpp) run by its movie: the guide strings it reads, the
/// tutorial balloon, the summary panel and the text tips with their [[name:value]] tags (needs MS2_DATA).
/// </summary>
public class GuideEventTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static KeyTable keys;

    static UiModel Model()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        if (packs == null)
        {
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
            keys = new KeyTable(packs, "en");
        }
        return new UiModel(tables) { Keys = keys };
    }

    static UiHost Load(GuideEventController c)
    {
        var m = Model();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        TestContext.WriteLine($"{c.Movie}: calls [{string.Join(", ", h.Calls)}] unhandled [{string.Join(", ", h.Unhandled)}]");
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return h;
    }

    /// <summary>The movie's child of the entity (the guideTutorial / guideSummary script).</summary>
    static object Child(UiHost h, string name) => h.Child(name);

    /// <summary>The text container's children (clearContents empties m_container; every add appends to it).</summary>
    static object[] Contents(UiHost h, object child)
    {
        var container = h.Get(h.Get(h.Get(child, "m_text"), "container"), "instance");
        int n = (int)Rt.ToNumber(h.Get(container, "numChildren"));
        var r = new object[n];
        for (int i = 0; i < n; i++) r[i] = h.Invoke(container, "getChildAt", i);
        return r;
    }

    /// <summary>The plain text of a text part child (htmlText gives the movie's canonicalised html back).</summary>
    static string TextOf(UiHost h, object child) => Rt.ToString(h.Get(h.Get(child, "textField"), "text"));

    [Test]
    public void TheStringsLoadWithTheirVoicesAndObjectWords()
    {
        Model();
        var s = GuideStrings.Parse(packs, "en");
        Assert.That(s.Text(5), Is.EqualTo("Mouse over and open the menu"));          // a plain text
        Assert.That(s.Text(10591), Does.Contain("\n"), "the \\n of the xml is a line break");
        Assert.That(s.Text(10591), Does.Contain("<font color="), "the html tags are kept as loaded");
        // CStringTable's load replaces the object words: "$item:30000598$" is the item's name, the [[..]] tags are kept
        Assert.That(s.Text(10720), Is.EqualTo("Press the [[keyAction:get]] key and pick up Hard Egg"));
        // the voices are kept apart from the texts
        Assert.That(s.Voice(1030), Is.EqualTo("ko/Tutorial/3"));
        Assert.That(s.Voice(5), Is.Empty);
        Assert.That(s.Text(420), Is.Empty, "a key of an off feature (SurvivalTutorial) is skipped");
        // the shared table is built once per language
        Assert.That(GuideStrings.Shared(packs, "en"), Is.SameAs(GuideStrings.Shared(packs, "en")));
    }

    [Test]
    public void TheStringsCacheGivesTheLoadedTable()
    {
        Model();
        var dir = Path.Combine(Path.GetTempPath(), "ms2-guide-strings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, GuideCache.StringsFileName("test", "en"));
        var keepOf = GuideStrings.CacheOf;
        var keepLog = GuideStrings.Log;
        var keepMode = Environment.GetEnvironmentVariable("MS2_UICACHE");
        try
        {
            using (var shared = new PackSet(packs.DataDir))
            {
                var sw = Stopwatch.StartNew();
                GuideCache.BuildStrings(shared, "en", path);
                TestContext.Progress.WriteLine($"guide strings: built in {sw.Elapsed.TotalMilliseconds:F0} ms, {new FileInfo(path).Length / 1024} KB");

                sw.Restart();
                var parsed = GuideStrings.Parse(shared, "en");
                double parseMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                var cached = GuideCache.ReadStrings(path);
                TestContext.Progress.WriteLine($"guide strings: parse {parseMs:F1} ms, cache read {sw.Elapsed.TotalMilliseconds:F1} ms");

                Assert.That(cached, Is.Not.Null);
                Assert.That(GuideCache.FirstStringsDifference(parsed, cached), Is.Null);
            }

            // a changed text is a difference
            var changed = GuideCache.ReadStrings(path);
            Assert.That(GuideCache.FirstStringsDifference(changed, GuideStrings.Parse(packs, "en")), Is.Null);
            // Load: the cache when it is there; off parses; a bad cache falls back to the parse
            GuideStrings.CacheOf = (d, lang) => path;
            GuideStrings.Log = _ => { };
            Environment.SetEnvironmentVariable("MS2_UICACHE", null);
            Assert.That(GuideCache.FirstStringsDifference(GuideStrings.Parse(packs, "en"), GuideStrings.Load(packs, "en")), Is.Null);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.That(GuideStrings.Load(packs, "en").Text(5), Is.EqualTo("Mouse over and open the menu"));
            Environment.SetEnvironmentVariable("MS2_UICACHE", "off");
            Assert.That(GuideStrings.Load(packs, "en").Text(10720), Is.EqualTo("Press the [[keyAction:get]] key and pick up Hard Egg"));

            // a corrupted cache reads as null: cut short, a trailing byte, another version, a bad end mark
            GuideCache.WriteStrings(GuideStrings.Parse(packs, "en"), path);
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..^6]);
            Assert.That(GuideCache.ReadStrings(path), Is.Null);
            File.WriteAllBytes(path, bytes.Concat(new byte[] { 0 }).ToArray());
            Assert.That(GuideCache.ReadStrings(path), Is.Null);
            var v = (byte[])bytes.Clone(); v[4]++;
            File.WriteAllBytes(path, v);
            Assert.That(GuideCache.ReadStrings(path), Is.Null);
            var m = (byte[])bytes.Clone(); m[^1] ^= 0xFF;
            File.WriteAllBytes(path, m);
            Assert.That(GuideCache.ReadStrings(path), Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MS2_UICACHE", keepMode);
            GuideStrings.CacheOf = keepOf;
            GuideStrings.Log = keepLog;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Test]
    public void ASummaryShowsItsTextKeyButtonAndIconAndExpires()
    {
        // ShowSummary (@0x14195ab40): the panel's contents from the text's parts — 110 is
        // "Move using the [[keyAction:moveRight]] directional keys", so a text, the key of moveRight (the Right arrow
        // key's icon) and a text; 2220's [[icon:portal]] is the portal icon
        Model();
        uint now = 10_000;
        var sounds = new List<string>();
        var c = new GuideEventController
        {
            NowMs = () => now,
            Strings = GuideStrings.Parse(packs, "en"),
            SystemSound = sounds.Add,
        };
        var h = Load(c);
        Assert.That(c.Loaded, Is.True);
        var summary = Child(h, "guideSummary");
        Assert.That(Rt.ToBoolean(h.Get(summary, "visible")), Is.False);

        c.ShowSummary(1, 110, 20000);
        Assert.That(c.SummaryShowing, Is.True);
        Assert.That(Rt.ToBoolean(h.Get(summary, "visible")), Is.True);
        Assert.That(Rt.ToBoolean(h.Invoke(summary, "isShowing")), Is.True);
        var contents = Contents(h, summary);
        Assert.That(contents.Length, Is.EqualTo(3));
        Assert.That(TextOf(h, contents[0]), Is.EqualTo("Move using the "));
        Assert.That(TextOf(h, contents[2]), Is.EqualTo(" directional keys"));
        // the key button: the Right arrow key shows its direction icon, not a text (GuideKey.setButtonText)
        Assert.That(Rt.ToBoolean(h.Get(h.Get(contents[1], "arrow"), "visible")), Is.True, "the moveRight key's icon");
        Assert.That(Rt.ToBoolean(h.Get(h.Get(contents[1], "textField"), "visible")), Is.False);

        c.ShowSummary(1, 2220, 0);
        contents = Contents(h, summary);
        Assert.That(contents.Length, Is.EqualTo(3));
        Assert.That(TextOf(h, contents[0]), Is.EqualTo("Use the "));
        // the icon child: a guide_summary_icon (addIcon), not a text or a key button
        Assert.That((contents[1] as AsObject)?.Class?.LocalName, Is.EqualTo("guide_summary_icon"));
        Assert.That(TextOf(h, contents[2]), Is.EqualTo(" portal and move to the Fluttering Garden"));
        Assert.That(c.SummaryShowing, Is.True);
        now += 1;
        c.Process();
        Assert.That(c.SummaryShowing, Is.True, "no duration, no expiry");
        Assert.That(sounds, Is.Empty, "a summary plays no system sound");

        // the duration (the server's trigger packets pass one): the panel hides when it is up
        c.ShowSummary(7, 110, 100);
        now += 101;
        c.Process();
        Assert.That(c.SummaryShowing, Is.False);
        // HideSummary of another guide id is ignored; of its own it hides
        c.ShowSummary(7, 110, 0);
        c.HideSummary(9);
        Assert.That(c.SummaryShowing, Is.True);
        c.HideSummary(0);
        Assert.That(c.SummaryShowing, Is.False);
    }

    [Test]
    public void ATutorialBalloonShowsItsStepsAndDirectionKeys()
    {
        // ShowTutorial (@0x14195a520): 1030's "[[keyDirection:1]]" is the four direction keys, then its text
        Model();
        var c = new GuideEventController { Strings = GuideStrings.Parse(packs, "en") };
        var h = Load(c);
        var tutorial = Child(h, "guideTutorial");

        c.ShowTutorial(4, 1030, true);
        Assert.That(c.TutorialShowing, Is.True);
        Assert.That(Rt.ToBoolean(h.Get(tutorial, "visible")), Is.True);
        var contents = Contents(h, tutorial);
        Assert.That(contents.Length, Is.EqualTo(5));
        Assert.That(Rt.ToBoolean(h.Get(h.Get(contents[0], "arrow"), "visible")), Is.True, "the Up direction key's icon");
        Assert.That(Rt.ToBoolean(h.Get(h.Get(contents[1], "arrow"), "visible")), Is.True, "the Down one's");
        Assert.That(TextOf(h, contents[4]), Does.StartWith(" Use the directional keys"));

        // a later step does not run the show again (the alignment with gotoAndPlayText)
        c.ShowTutorial(4, 1030, false);
        Assert.That(Rt.ToString(h.Get(h.Get(tutorial, "m_text"), "container") is { } box ? h.Get(box, "currentLabel") : null),
            Is.EqualTo("show"), "gotoAndPlayText went to the show label without showing again");

        c.HideTutorial(9);
        Assert.That(c.TutorialShowing, Is.True, "the tutorial of another guide id is left");
        c.HideTutorial(4);
        Assert.That(c.TutorialShowing, Is.False, "hideTutorial went to the hide label");
    }

    [Test]
    public void TextTipsShowQueueAdvanceAndClose()
    {
        // QueueOrShowTextTip (@0x14195b330) and AdvanceTextTip (@0x14195b600): a tip while one shows is queued; the
        // close (the tip's button, onTextTipClose) advances to it; the last close empties the pane's tip
        Model();
        var sounds = new List<string>();
        var c = new GuideEventController { Strings = GuideStrings.Parse(packs, "en"), SystemSound = sounds.Add };
        var h = Load(c);

        Assert.That(c.IsTextTipShowing, Is.False);
        c.QueueOrShowTextTip(1, 5);
        Assert.That(c.IsTextTipShowing, Is.True);
        Assert.That(sounds, Is.EqualTo(new[] { "system_guide_tooltip" }));
        var tip = h.Get(h.Entity, "guideTextTip") as Ms2.Avm2.Flash.AsDisplayObject;
        Assert.That(Rt.ToString(h.Get(h.FindNamed("textField", tip), "text")), Does.StartWith("Mouse over"), "the tip's text");

        c.QueueOrShowTextTip(2, 10591);     // queued while 1 shows
        c.QueueOrShowTextTip(3, 5);         // queued behind it
        Assert.That(sounds.Count, Is.EqualTo(1), "no sound for a queued tip");

        // a close of another id is ignored
        c.AdvanceTextTip(2);
        Assert.That(c.IsTextTipShowing, Is.True);
        Assert.That(c.LastClosedTip, Is.EqualTo(0));

        // the movie's close button: remove the shown tip and show the next
        Assert.That(c.OnCallback("onTextTipClose", Array.Empty<object>()), Is.True);
        Assert.That(c.IsTextTipShowing, Is.True);
        Assert.That(c.LastClosedTip, Is.EqualTo(2), "the tip that followed the closed one");
        Assert.That(sounds.Count, Is.EqualTo(2), "the next tip's sound");
        c.AdvanceTextTip(2);
        Assert.That(c.LastClosedTip, Is.EqualTo(3));
        c.AdvanceTextTip(3);
        Assert.That(c.IsTextTipShowing, Is.False);
        Assert.That(c.LastClosedTip, Is.EqualTo(0), "no tip followed the last close");

        // Esc closes a showing tip (FUN_1418fcb70)
        c.QueueOrShowTextTip(9, 5);
        Assert.That(c.SkipTextTip(), Is.True);
        Assert.That(c.IsTextTipShowing, Is.False);
        Assert.That(c.SkipTextTip(), Is.False);

        // vfunc_3 (leaving the scene): the pane forgets its tip and its queue, so the next one shows instead of queueing
        c.QueueOrShowTextTip(1, 5);
        c.QueueOrShowTextTip(2, 5);         // queued behind it
        c.Hide();
        c.AdvanceTextTip(0);                // the queue was dropped with the scene: no tip follows
        Assert.That(c.LastClosedTip, Is.EqualTo(0));
        c.QueueOrShowTextTip(4, 5);
        Assert.That(sounds.Count, Is.EqualTo(6), "the tip after the scene shows at once");
    }

    [Test]
    public void ArrowsAreKeptWithUseCountsAndStayHiddenWithoutTheirPositions()
    {
        // AddArrow (@0x141959a60) / ReleaseArrow (@0x14195a0c0): a second add bumps the use count, a release drops it,
        // and only at zero does the movie remove the arrow. The positions are not ported, so every arrow is hidden
        // (its info's hide flag, GuideArrow.setData).
        Model();
        var c = new GuideEventController { Strings = GuideStrings.Parse(packs, "en") };
        var h = Load(c);
        var arrow = new GuideArrow(50, new System.Numerics.Vector3(100, 200, 0), 2, 1, 5);

        // the movie's arrow panel: the entity's unnamed child (GuideEvent's ctor builds it; its fields are protected,
        // so this walks the display tree the movie builds)
        Ms2.Avm2.Flash.AsContainer Panel()
        {
            var root = (Ms2.Avm2.Flash.AsContainer)h.Entity;
            return root.Children.FirstOrDefault(k => string.IsNullOrEmpty(k.InstanceName)) as Ms2.Avm2.Flash.AsContainer;
        }
        Assert.That(Panel(), Is.Not.Null);

        c.AddArrow(arrow);
        c.AddArrow(arrow);
        var entity = Panel().Children.Single();
        Assert.That(Rt.ToBoolean(h.Get(entity, "visible")), Is.False, "an arrow without a position stays hidden");
        Assert.That(Rt.ToBoolean(h.Invoke(h.Entity, "hasGuideArrow")), Is.False);

        c.Process();        // vfunc_15: every arrow's position is pushed (hideArrow while the lookup fails)
        Assert.That(Panel().Children.Count, Is.EqualTo(1), "the update leaves the arrow in place");

        c.ReleaseArrow(50);
        Assert.That(Panel().Children, Has.Count.EqualTo(1), "the second holder keeps the arrow");
        c.ReleaseArrow(50);
        Assert.That(Panel().Children, Is.Empty, "at zero the movie removes it");
        c.ReleaseArrow(50);     // gone: no call, no throw
        Assert.That(Panel().Children, Is.Empty);

        // a text arrow plays its voice and carries its text (addTextArrow, GuideTextArrow.setData)
        var voices = new List<string>();
        c.PlayVoiceAt = voices.Add;
        c.AddArrow(new GuideArrow(60, new System.Numerics.Vector3(0, 0, 0), 0, 1, 5));
        Assert.That(Panel().Children, Has.Count.EqualTo(1));
        Assert.That(voices, Is.Empty, "text 5 has no voiceID");
        c.AddArrow(new GuideArrow(61, new System.Numerics.Vector3(0, 0, 0), 0, 1, 1030));
        Assert.That(voices, Is.EqualTo(new[] { "ko/Tutorial/3" }), "PlayVoice of the arrow's text");
        c.ReleaseArrow(60);
        c.ReleaseArrow(61);
        Assert.That(Panel().Children, Is.Empty);
    }
}
