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
        Assert.That(s.Text(5), Is.Not.Empty, "a plain text loads");
        Assert.That(s.Text(10591), Does.Contain("\n"), "the \\n of the xml is a line break");
        Assert.That(s.Text(10591), Does.Contain("<font"), "the html tags are kept as loaded");
        // CStringTable's load replaces the object words ("$item:30000598$"), the [[..]] tags are kept for the pane
        Assert.That(s.Text(10720), Does.Not.Contain("$"));
        Assert.That(s.Text(10720), Does.Contain("[[keyAction:get]]"));
        // the voices are kept apart from the texts
        Assert.That(s.Voice(1030), Is.Not.Empty);
        Assert.That(s.Voice(5), Is.Empty);
        Assert.That(s.Text(420), Is.Empty, "a key of an off feature (SurvivalTutorial) is skipped");
        // the shared table is built once per language
        Assert.That(GuideStrings.Shared(packs, "en"), Is.SameAs(GuideStrings.Shared(packs, "en")));
    }

    [Test]
    public void TheStringsCacheGivesTheLoadedTable()
    {
        Model();
        using (var shared = new PackSet(packs.DataDir))
        {
            var sw = Stopwatch.StartNew();
            var body = GuideCache.WriteStringsBody(GuideStrings.Parse(shared, "en"));
            TestContext.Progress.WriteLine($"guide strings: built in {sw.Elapsed.TotalMilliseconds:F0} ms, {body.Length / 1024} KB");

            sw.Restart();
            var parsed = GuideStrings.Parse(shared, "en");
            double parseMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var cached = GuideCache.ReadStringsBody(body);
            TestContext.Progress.WriteLine($"guide strings: parse {parseMs:F1} ms, cache read {sw.Elapsed.TotalMilliseconds:F1} ms");

            Assert.That(cached, Is.Not.Null);
            Assert.That(GuideCache.FirstStringsDifference(parsed, cached), Is.Null);
        }

        // a corrupted body reads as null: cut short, a trailing byte, another version, a bad end mark
        var bytes = GuideCache.WriteStringsBody(GuideStrings.Parse(packs, "en"));
        Assert.That(GuideCache.ReadStringsBody(bytes[..^6]), Is.Null);
        Assert.That(GuideCache.ReadStringsBody(bytes.Concat(new byte[] { 0 }).ToArray()), Is.Null);
        var v = (byte[])bytes.Clone(); v[4]++;
        Assert.That(GuideCache.ReadStringsBody(v), Is.Null);
        var m = (byte[])bytes.Clone(); m[^1] ^= 0xFF;
        Assert.That(GuideCache.ReadStringsBody(m), Is.Null);
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
        // 110's parts: a text, the moveRight key button, a text (no exact text asserted: the data's words are not ours)
        Assert.That(contents.Length, Is.EqualTo(3));
        Assert.That(TextOf(h, contents[0]), Is.Not.Empty);
        Assert.That(TextOf(h, contents[2]), Is.Not.Empty);
        // the key button: the default binding of moveRight is a direction key, so it shows its icon (GuideKey.setButtonText)
        Assert.That((contents[1] as AsObject)?.Class?.LocalName, Is.EqualTo("guide_key"));
        Assert.That(Rt.ToBoolean(h.Get(h.Get(contents[1], "arrow"), "visible")), Is.True, "the moveRight key's icon");
        Assert.That(Rt.ToBoolean(h.Get(h.Get(contents[1], "textField"), "visible")), Is.False);

        c.ShowSummary(1, 2220, 0);
        contents = Contents(h, summary);
        // 2220's parts: a text, the portal icon, a text
        Assert.That(contents.Length, Is.EqualTo(3));
        Assert.That(TextOf(h, contents[0]), Is.Not.Empty);
        Assert.That(TextOf(h, contents[2]), Is.Not.Empty);
        Assert.That((contents[1] as AsObject)?.Class?.LocalName, Is.EqualTo("guide_summary_icon"));
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
        // 1030's parts: the four direction keys of its [[keyDirection:1]] tag, then its text
        Assert.That(contents.Length, Is.EqualTo(5));
        for (int i = 0; i < 4; i++)
        {
            Assert.That((contents[i] as AsObject)?.Class?.LocalName, Is.EqualTo("guide_key"), $"direction key {i}");
            Assert.That(Rt.ToBoolean(h.Get(h.Get(contents[i], "arrow"), "visible")), Is.True, $"the direction key {i} icon");
        }
        Assert.That(TextOf(h, contents[4]), Is.Not.Empty);

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
        Assert.That(Rt.ToString(h.Get(h.FindNamed("textField", tip), "text")), Is.Not.Empty, "the tip's text");

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
    public void InventoryAndSkillArrowsResolveTheirKinds()
    {
        // UIGuideEventArrowDialog::vfunc_2's kind resolvers (uiguideeventarrow.cpp:569-581): the skill slot's grid
        // math (GetSkillSlotPosition :677-700), the inventory item's first-match walk with the scroll page's window
        // (GetInventoryItemPosition :717-752, IsItemMatchingKind :703-714) and the tab bar's button place
        // (GetInventoryTabPosition :755-778) — over fake roots, so the client's own formulas are the assert.
        Model();
        var c = new GuideEventController
        {
            Strings = GuideStrings.Parse(packs, "en"),
            DialogOfName = n => n switch
            {
                "MainPlayerInfo" => new FakeDialog(new FakeNode(100, 200)),
                "InventoryDialog" => new FakeDialog(new FakeNode(300, 400)),
                _ => null,
            },
            // the kinds 2-6 ask the inventory by its id (Unk_BreakDialog<UIInventoryDialog>(0x4e2d))
            DialogOfId = id => id == 0x4e2d ? new FakeDialog(new FakeNode(300, 400)) : null,
            // CKeyTable::FindQuickSlotBySkillAndItemUid: the lesson's skill sits in slot 5, another skill in none
            SkillQuickSlot = skill => skill == 10900031 ? 5 : -1,
            InventoryOf = () => new GuideInventory
            {
                // the equip tab's items by slot: an expired match first, then the weapon (kind 5's), a type-2 item,
                // a UGC cube item, and an item of the type the kind 3 arrow asks
                Items = new[]
                {
                    new GuideInventoryItem(0, 15400274, 1, 40, false, false, false),
                    new GuideInventoryItem(6, 15000224, 1, 40, false, false, true),
                    new GuideInventoryItem(12, 11200005, 2, 51, true, false, true),
                    new GuideInventoryItem(18, 11300006, 3, 52, true, true, true),
                    new GuideInventoryItem(24, 11400007, 3, 53, false, false, true),
                },
                ScrollPage = 0,
            },
            InventoryTabOf = name => name == "equip" ? ((double)14, 64, 0, 31) : null,
        };
        var h = Load(c);
        Ms2.Avm2.Flash.AsContainer Panel()
        {
            var root = (Ms2.Avm2.Flash.AsContainer)h.Entity;
            return root.Children.FirstOrDefault(k => string.IsNullOrEmpty(k.InstanceName)) as Ms2.Avm2.Flash.AsContainer;
        }
        static void Place(GuideEventController c, int id, double x, double y, string because)
        {
            var p = c.ArrowPosition(id);
            Assert.That(p, Is.Not.Null, because);
            Assert.That(p.Value.X, Is.EqualTo(x).Within(0.5), because);
            Assert.That(p.Value.Y, Is.EqualTo(y).Within(0.5), because);
        }

        // the skill lesson's row (guide_tutorial_10012000.xml, event 10012052): the arrow at the quick slot of the
        // skill 10900031 — slot 5 of the 4-7 range, slot % 4 = 1: baseX = -68 + 0x44 + 0x20
        c.AddArrow(new GuideDialogArrow(10012052, new(-4, 0, 0), 2, 2, 10012053, 0, "MainPlayerInfo", -1,
            Array.Empty<string>(), Array.Empty<string>(), "myInfoSkillID", 10900031));
        c.Process();
        Place(c, 10012052, 100 - 68 + 0x44 + 0x20 + 57 - 4, 200 + 103, "the grid place of slot 5 plus the row's offset");
        Assert.That(Rt.ToBoolean(h.Get(Panel().Children.Single(), "visible")), Is.True, "the movie draws it");

        // a skill no quick slot holds hides it (FindQuickSlotBySkillAndItemUid's -1)
        c.ReleaseArrow(10012052);
        c.AddArrow(new GuideDialogArrow(1, new(0, 0, 0), 2, 2, 10012053, 0, "MainPlayerInfo", -1,
            Array.Empty<string>(), Array.Empty<string>(), "myInfoSkillID", 10200001));
        c.Process();
        Assert.That(c.ArrowPosition(1), Is.Null, "no slot: hidden");
        c.ReleaseArrow(1);

        // the equip lesson's row (guide_tutorial_100.xml, event 210): the arrow at the first valid item of the kind —
        // the expired one is skipped, the weapon's slot 6 is col 1, row 1 of page 0's window
        c.AddArrow(new GuideDialogArrow(214, new(110, 50, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            Array.Empty<string>(), new[] { "InventoryDialog", "MyInfoDialog" }, "inventoryItemID", 15000224));
        c.Process();
        Place(c, 214, 300 + 0x3f + 17 + 110, 400 + 0x3e + 99 + 50, "col 1 of the page, plus the row's offset");
        Assert.That(Rt.ToBoolean(h.Get(Panel().Children.Single(), "visible")), Is.True, "the movie draws it");
        c.ReleaseArrow(214);

        // kind 3 matches the data's type: the first valid item of type 3 is the UGC cube one (slot 18, col 1 row 3)
        c.AddArrow(new GuideDialogArrow(215, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            Array.Empty<string>(), Array.Empty<string>(), "inventoryItemType", 3));
        c.Process();
        Place(c, 215, 300 + 0x3f + 17, 400 + 3 * 0x3e + 99, "the first valid item of the type");
        c.ReleaseArrow(215);

        // kind 4 matches the icon code; kind 2 a cube mesh item without a UGC id (the UGC one fails, the slot 12 one matches)
        c.AddArrow(new GuideDialogArrow(216, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            Array.Empty<string>(), Array.Empty<string>(), "inventoryIconType", 51));
        c.Process();
        Place(c, 216, 300 + 0x3f + 17, 400 + 2 * 0x3e + 99, "the icon's item is row 2");
        c.ReleaseArrow(216);
        c.AddArrow(new GuideDialogArrow(217, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            Array.Empty<string>(), Array.Empty<string>(), "inventoryDesignItem", 0));
        c.Process();
        Place(c, 217, 300 + 0x3f + 17, 400 + 2 * 0x3e + 99, "the UGC cube item does not match kind 2");
        c.ReleaseArrow(217);

        // an item outside the scroll page's window fails the lookup — the client does not look past the first match
        c.InventoryOf = () => new GuideInventory { Items = new[] { new GuideInventoryItem(40, 15000224, 1, 40, false, false, true) }, ScrollPage = 0 };
        c.AddArrow(new GuideDialogArrow(218, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            Array.Empty<string>(), Array.Empty<string>(), "inventoryItemID", 15000224));
        c.Process();
        Assert.That(c.ArrowPosition(218), Is.Null, "the first match is off the page");
        c.ReleaseArrow(218);

        // the gates hide a resolved kind too (vfunc_2's, before the switch)
        c.InventoryOf = null;
        c.DialogArrowBlocked = () => true;
        c.AddArrow(new GuideDialogArrow(219, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            new[] { "equip" }, Array.Empty<string>(), "inventoryTab", 0));
        c.Process();
        Assert.That(c.ArrowPosition(219), Is.Null, "the cinematic gate");
        c.ReleaseArrow(219);
        c.DialogArrowBlocked = () => false;

        // the tab arrow (event 210's lesson row shape): the tab bar's and the equip button's places sum from the root
        c.AddArrow(new GuideDialogArrow(220, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            new[] { "equip" }, Array.Empty<string>(), "inventoryTab", 0));
        c.Process();
        Place(c, 220, 300 + 14 + 0, 400 + 64 + 31, "the tab's button resolves");
        Assert.That(Rt.ToBoolean(h.Get(Panel().Children.Single(), "visible")), Is.True, "the movie draws it");
        c.ReleaseArrow(220);

        // a name no tab of the inventory holds, and the inventory not there at all, hide the arrow
        c.AddArrow(new GuideDialogArrow(221, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            new[] { "nosuchtab" }, Array.Empty<string>(), "inventoryTab", 0));
        c.Process();
        Assert.That(c.ArrowPosition(221), Is.Null, "no such tab");
        c.ReleaseArrow(221);
        c.DialogOfId = null;
        c.AddArrow(new GuideDialogArrow(222, new(0, 0, 0), 0, 1, 3191, 0x4e2d, "InventoryDialog", -1,
            new[] { "equip" }, Array.Empty<string>(), "inventoryTab", 0));
        c.Process();
        Assert.That(c.ArrowPosition(222), Is.Null, "no inventory: hidden");
        c.ReleaseArrow(222);
    }

    [Test]
    public void EscClosesTheTooltipsBeforeTheGuidesTextTip()
    {
        // FUN_1418fcb70's order (uichatballoon.cpp:524-532): with a tooltip open and a guide text tip showing, the
        // first Esc closes only the tooltips (GFxManager::CloseTooltips) and the second skips the tip
        // (UIGuideEvent::SkipTextTip); the option dialog is not there, so the tooltips take it first.
        Model();
        var c = new GuideEventController { Strings = GuideStrings.Parse(packs, "en") };
        var h = Load(c);
        c.QueueOrShowTextTip(1, 5);
        Assert.That(c.IsTextTipShowing, Is.True);

        var tc = new TooltipController();
        var (rt, p) = src.Open(tc.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        var th = new UiHost(p, tc, Model());
        Assert.That(th.Load(), Is.True, "the tooltip movie: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        tc.Call("createTooltip", new object[] { 1, new Dictionary<string, object> { ["text"] = "a tooltip" },
            new Dictionary<string, object> { ["x"] = 600, ["y"] = 300, ["width"] = 40, ["height"] = 20 } });
        Assert.That(tc.Open, Has.Count.EqualTo(1), "a tooltip is shown");

        Assert.That(FieldEsc.Run(() => false, tc.DestroyTooltips, c.SkipTextTip), Is.True, "the first Esc is consumed");
        Assert.That(tc.Open, Is.Empty, "CloseTooltips destroyed it");
        Assert.That(c.IsTextTipShowing, Is.True, "the guide's tip stays for the next Esc");
        Assert.That(FieldEsc.Run(() => false, tc.DestroyTooltips, c.SkipTextTip), Is.True, "the second Esc is consumed");
        Assert.That(c.IsTextTipShowing, Is.False, "SkipTextTip closed the tip");
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
        Assert.That(voices, Has.Count.EqualTo(1), "PlayVoice of the arrow's text");
        c.ReleaseArrow(60);
        c.ReleaseArrow(61);
        Assert.That(Panel().Children, Is.Empty);
    }

    [Test]
    public void AGameMenuArrowPointsAtTheMovedInventoryButton()
    {
        // Event 170 of guide_tutorial_100.xml: the arrow at the game menu's inventory button. The menu table moves the
        // inventory into the character group, so the path's "inven" resolves through the movie's getChildObjectPos
        // (GetGameMenuPathPosition @0x14195fc40) — the same wiring the HUD's game menu gives the pane.
        Model();
        var menu = new GameMenuController { Table = GameMenuTable.Load(packs, Model().IsFeatureEnabled) };
        var (rt, menuPlayer) = src.Open(menu.Movie, r => r.Budget = 20_000_000);
        menuPlayer.StageW = 1280; menuPlayer.StageH = 720;
        var strs = new Dictionary<string, string>();
        using (var xr = System.Xml.XmlReader.Create(new MemoryStream(packs.Read("Xml", "string/en/korflash.xml"))))
            while (xr.Read())
                if (xr.NodeType == System.Xml.XmlNodeType.Element && xr.Name == "key" && xr.GetAttribute("id") is { } id && xr.GetAttribute("value") is { } v)
                    strs[id] = v.Replace("\\n", "\n");
        menuPlayer.Translate = k => strs.TryGetValue(k, out var t) ? t : k;
        var menuHost = new UiHost(menuPlayer, menu, Model());
        Assert.That(menuHost.Load(), Is.True, "game menu load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) menuPlayer.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("menu error: " + e);

        // the menu table's remap of a moved entry — the game menu's own step, as the HUD wires it
        var c = new GuideEventController
        {
            Strings = GuideStrings.Parse(packs, "en"),
            DialogOfName = n => string.Equals(n, "GameMenuDialog", StringComparison.OrdinalIgnoreCase)
                ? new GuideControllerDialog(menu) : null,
            GameMenuChildPos = (element, clip) => GameMenuController.MenuChildPos(menu.Table, element, clip),
        };
        var h = Load(c);

        // the dialog the pane found is the game menu, open at its movie
        var d = c.DialogOfName("GameMenuDialog");
        Assert.That(d, Is.Not.Null);
        Assert.That(d.Hidden, Is.False);
        Assert.That(d.Root, Is.Not.Null);
        Assert.That(d.Root.Child("m_menus"), Is.Not.Null, "the menu bar child");

        // the tutorial's scene: the pointer over the character group's button opens its dropdown (the mouse-over
        // lesson), and the arrow of the event 170 row points at the moved inventory button in it — the position
        // resolves through the movie's getChildObjectPos over the open list
        var charGroup = ((Ms2.Avm2.Flash.AsContainer)menuHost.Entity).Children
            .SelectMany(k => k is Ms2.Avm2.Flash.AsContainer mc ? mc.Children : Enumerable.Empty<Ms2.Avm2.Flash.AsDisplayObject>())
            .FirstOrDefault(k => k.InstanceName == "charactergroup");
        Assert.That(charGroup, Is.Not.Null, "the character group entry");
        var groupButton = ((Ms2.Avm2.Flash.AsContainer)charGroup).Children.FirstOrDefault(k => k.InstanceName == "m_button");
        Assert.That(groupButton, Is.Not.Null, "the group's button");
        var gb = charGroup.GetBounds(null);
        bool hitGroup = false; double gx = 0, gy = 0;
        for (double y = gb.Y; !hitGroup && y < gb.Y + gb.H; y += 2)
            for (double x = gb.X; !hitGroup && x < gb.X + gb.W; x += 2)
                for (var t = menuPlayer.MouseTarget(x, y); t != null; t = t.Parent)
                    if (t == groupButton) { gx = x; gy = y; hitGroup = true; break; }
        Assert.That(hitGroup, Is.True, "the group button takes the pointer");
        menuPlayer.MouseMove(gx + 1, gy + 1);
        for (int i = 0; i < 10; i++) menuPlayer.Tick();

        c.AddArrow(new GuideDialogArrow(170, new(-80, 23, 0), 2, 1, 2, 0, "GameMenuDialog", -1,
            new[] { "m_menus", "inven", "m_button" }, Array.Empty<string>(), "", 0));
        var panel = ((Ms2.Avm2.Flash.AsContainer)h.Entity).Children
            .FirstOrDefault(k => string.IsNullOrEmpty(k.InstanceName)) as Ms2.Avm2.Flash.AsContainer;
        var arrow = panel.Children.Single();
        c.Process();
        Assert.That(Rt.ToBoolean(h.Get(arrow, "visible")), Is.True, "the arrow points at the moved button");
        Assert.That(Rt.ToBoolean(h.Invoke(h.Entity, "hasGuideArrow")), Is.True);

        // the exact place, GetGameMenuPathPosition's own sum (:905-943): the root's and m_menus' places, the moved
        // entry's place and half width (:926-928), and after the break still the last plain element's half width
        // (:941-942) — the walk ends at the moved entry, it does not return early
        var (rx, ry, _) = c.DialogOfName("GameMenuDialog").Root.Position;
        var menus = c.DialogOfName("GameMenuDialog").Root.Child("m_menus");
        var (mx, my, _) = menus.Position;
        var moved = GameMenuController.MenuChildPos(menu.Table, "inven", menus).Value;
        Assert.That(moved.Fail, Is.False, "the open dropdown answers the moved entry");
        var p170 = c.ArrowPosition(170).Value;
        Assert.That(p170.X, Is.EqualTo(rx + mx + moved.X + moved.HalfWidth + menus.WidthCeil / 2 - 80).Within(0.5),
            "the last plain element's half width is part of the place");
        Assert.That(p170.Y, Is.EqualTo(ry + my + moved.Y + 23).Within(0.5));

        // a moved entry whose movie answer is no object fails the whole lookup — no plain-walk fallback
        c.ReleaseArrow(170);
        c.GameMenuChildPos = (element, clip) => element == "inven" ? new GuideMenuMoved(Fail: true, 0, 0, 0) : null;
        c.AddArrow(new GuideDialogArrow(170, new(-80, 23, 0), 2, 1, 2, 0, "GameMenuDialog", -1,
            new[] { "m_menus", "inven", "m_button" }, Array.Empty<string>(), "", 0));
        c.Process();
        Assert.That(c.ArrowPosition(170), Is.Null, "getChildObjectPos not an object: hidden (:920-921)");
        c.GameMenuChildPos = (element, clip) => GameMenuController.MenuChildPos(menu.Table, element, clip);
        c.ReleaseArrow(170);

        // the gates hide it: a popup or the message box (FUN_141274f80), the cinematic sub scene for the dialog kinds
        c.AddArrow(new GuideDialogArrow(170, new(-80, 23, 0), 2, 1, 2, 0, "GameMenuDialog", -1,
            new[] { "m_menus", "inven", "m_button" }, Array.Empty<string>(), "", 0));
        c.Process();
        arrow = panel.Children.Single();
        bool blocked = true;
        c.DialogArrowBlocked = () => blocked;
        c.Process();
        Assert.That(Rt.ToBoolean(h.Get(arrow, "visible")), Is.False, "the gate hides the arrow");
        blocked = false;
        c.Process();
        Assert.That(Rt.ToBoolean(h.Get(arrow, "visible")), Is.True, "and it shows again when the gate opens");

        // a path element that is no moved entry (the menu group itself, event 60540's) takes the plain walk
        c.ReleaseArrow(170);
        c.AddArrow(new GuideDialogArrow(60540, new(-20, 0, 0), 2, 1, 5, 0, "GameMenuDialog", -1,
            new[] { "m_menus", "charactergroup" }, Array.Empty<string>(), "", 0));
        c.Process();
        var group = panel.Children.Single();
        Assert.That(Rt.ToBoolean(h.Get(group, "visible")), Is.True, "the arrow at the menu group");
        Assert.That(Rt.ToNumber(h.Get(group, "x")), Is.GreaterThan(0));
    }

    [Test]
    public void TheGameMenuWalkAddsTheLastPlainElementsHalfWidth()
    {
        // GetGameMenuPathPosition's end (uiguideeventarrow.cpp:929-942): the walk breaks at the moved entry and still
        // adds the last plain element's half width — the movie's m_menus answers width zero here, so the place is
        // pinned over a fake root that has one.
        Model();
        var button = new FakeNode(0, 0, 40);
        var inven = new FakeNode(30, 40, 60);
        inven.With("m_button", button);
        var menus = new FakeNode(10, 20, 100);
        menus.With("inven", inven);
        var root = new FakeNode(100, 200);
        root.With("m_menus", menus);
        var c = new GuideEventController
        {
            Strings = GuideStrings.Parse(packs, "en"),
            DialogOfName = n => string.Equals(n, "GameMenuDialog", StringComparison.OrdinalIgnoreCase) ? new FakeMenuDialog(root) : null,
            GameMenuChildPos = (element, clip) => element == "inven" ? new GuideMenuMoved(Fail: false, 5, 6, 30) : null,
        };
        var h = Load(c);
        c.AddArrow(new GuideDialogArrow(170, new(0, 0, 0), 2, 1, 2, 0, "GameMenuDialog", -1,
            new[] { "m_menus", "inven", "m_button" }, Array.Empty<string>(), "", 0));
        c.Process();
        var p = c.ArrowPosition(170).Value;
        Assert.That(p.X, Is.EqualTo(100 + 10 + 5 + 30 + 100 / 2.0).Within(0.5), "root + m_menus + the moved entry + m_menus' half width");
        Assert.That(p.Y, Is.EqualTo(200 + 20 + 6).Within(0.5));

        // the moved entry's fail value ends the lookup — the plain walk does not take over even where it would
        // resolve (the movie's answer was no object, :920-921)
        c.ReleaseArrow(170);
        c.GameMenuChildPos = (element, clip) => element == "inven" ? new GuideMenuMoved(Fail: true, 0, 0, 0) : null;
        c.AddArrow(new GuideDialogArrow(171, new(0, 0, 0), 2, 1, 2, 0, "GameMenuDialog", -1,
            new[] { "m_menus", "inven", "m_button" }, Array.Empty<string>(), "", 0));
        c.Process();
        Assert.That(c.ArrowPosition(171), Is.Null, "the fail hides the arrow, no fallback walk");
    }

    [Test]
    public void TheWorldArrowIsBlockedOnlyByAWindowDialog()
    {
        // FUN_141274f80's first gate (unit_141274d10_uihelpers.cpp:183): CUIService::FUN_141adcbb0's walk over the
        // registry — every HUD-resident movie at field entry is shown and none blocks, because the client's
        // always-on flag (vfunc_56) covers the built-ins and the chat, the quest tracker, the notice banner, the
        // group messenger bar, the party window and the event widget; one open window (the inventory) blocks.
        Model();
        var shown = new List<Ms2.Ui.UiController>();
        T Open<T>(T c) where T : Ms2.Ui.UiController
        {
            var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            var host = new UiHost(p, c, Model());
            Assert.That(host.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
            shown.Add(c);
            return c;
        }

        // the HUD's own movies at field entry: the built-ins and the head-up strips
        var menu = Open(new GameMenuController { Table = GameMenuTable.Load(packs, Model().IsFeatureEnabled) });
        Open(new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false) });
        Open(new MainPlayerInfoController());
        Open(new UpperHudController());
        Open(new ExpBarController());
        Open(new AdventureLevelExpBarController());
        Open(new ScreenMaskController(""));
        Open(new TooltipController());
        // the flag-0 dialog windows that live on the HUD: the chat, the quest tracker, the notice banner, the group
        // messenger bar, the party window, the event widget
        Open(new MessengerController(Model().Tables.DefaultMessengers[0], "messengerdialog0.xml", true));
        Open(new NoticeDialogController());
        var quests = new Ms2.Quest.QuestTable("KR", _ => true) { Common = k => k == "s_quest_name_with_level" ? "{0} (Lv. {1})" : "" };
        quests.AddQuests(System.Xml.Linq.XDocument.Parse("""
            <ms2><quest id="1"><basic questType="0" standardLevel="3" /><condition type="npc" code="7" value="1" /></quest></ms2>
            """));
        quests.BuildIndexes();
        quests.AddDescriptions(System.Xml.Linq.XDocument.Parse("""<ms2><quest questID="1" name="First" /></ms2>"""));
        Open(new QuestGuideController { Table = quests, State = new Ms2.Quest.QuestShotState(quests, 10, new[] { 1 }, tracking: 1) });
        Open(new GroupMessengerBrowserController());
        var partyBook = Ms2.Ui.Hud.PartyFixture.Book(2000062, Model().Player);
        var party = Open(new PartyController { Party = partyBook, SelfCharacterId = Ms2.Ui.Hud.PartyFixture.SelfCharacterId, MapName = _ => "Field" });
        partyBook.MemberAdded += party.AddMember;
        Open(new Ms2.Ui.Hud.BurningEventWidgetController(Model()));

        foreach (var c in shown)
            if (c.IsDialogEntity && c is not Ms2.Ui.Hud.EventWidgetController)
                Assert.That(c.IsClosed, Is.False, c.Movie + " is shown");
        // the event widget opens only while its event runs (ShouldOpen), so with none it reads closed as the
        // client's no-event one does — its flag-0 is what keeps it from ever blocking
        Assert.That(shown.OfType<Ms2.Ui.Hud.EventWidgetController>().Single().AlwaysOn, Is.True);
        Assert.That(Ms2.Ui.Hud.DialogGates.AnyDialogShown(shown), Is.False,
            "the always-on HUD never blocks the world arrow");

        // the pane over the HUD's registry, and a world arrow through it
        Ms2.Ui.UiController[] registry = shown.ToArray();
        var pane = new GuideEventController
        {
            Strings = GuideStrings.Parse(packs, "en"),
            ProjectWorld = _ => (400, 300),
            ArrowBlocked = () => Ms2.Ui.Hud.DialogGates.AnyDialogShown(registry),
        };
        var h = Load(pane);
        pane.AddArrow(new GuideArrow(50, new(0, 0, 0), 2, 1, 0));
        pane.Process();
        var p = pane.ArrowPosition(50).Value;
        Assert.That(p.X, Is.EqualTo(400).Within(0.5), "the arrow shows over the whole HUD");
        Assert.That(p.Y, Is.EqualTo(300).Within(0.5));

        // one open window among them blocks
        var book = new Ms2.Net.Packets.ItemBook { TabOf = _ => 0, BaseSlots = _ => 24 };
        var inv = new InventoryController { View = new Ms2.Net.Packets.InventoryView(book) };
        var (irt, ip) = src.Open(inv.Movie, r => r.Budget = 20_000_000);
        ip.StageW = 1280; ip.StageH = 720;
        var ih = new UiHost(ip, inv, Model());
        Assert.That(ih.Load(), Is.True, "inventory load: " + string.Join("; ", irt.Errors));
        for (int i = 0; i < 10; i++) { ip.Tick(); inv.Process(); }
        Assert.That(inv.AlwaysOn, Is.False, "a window dialog");
        registry = shown.Append(inv).ToArray();
        Assert.That(Ms2.Ui.Hud.DialogGates.AnyDialogShown(registry), Is.True, "the open window blocks");
        pane.Process();
        Assert.That(pane.ArrowPosition(50), Is.Null, "the world arrow hides");

        inv.Close();
        for (int i = 0; i < 30 && !inv.IsClosed; i++) { ip.Tick(); inv.Process(); }
        registry = shown.ToArray();
        pane.Process();
        Assert.That(pane.ArrowPosition(50), Is.Not.Null, "closed again: the arrow shows");
    }

    sealed class FakeNode : GuideNode
    {
        readonly double x, y, width;
        readonly Dictionary<string, GuideNode> children = new();
        public FakeNode(double x, double y, double width = 0) { this.x = x; this.y = y; this.width = width; }
        public void With(string name, GuideNode child) => children[name] = child;
        public override bool Visible => true;
        public override (double X, double Y, double Z) Position => (x, y, 0);
        public override double WidthCeil => width;
        public override GuideNode Child(string name) => children.TryGetValue(name, out var c) ? c : null;
    }

    sealed class FakeMenuDialog : GuideDialog
    {
        readonly GuideNode root;
        public FakeMenuDialog(GuideNode root) => this.root = root;
        public override string TypeName => "GameMenuDialog";
        public override bool Hidden => false;
        public override GuideNode Root => root;
    }

    sealed class FakeDialog : GuideDialog
    {
        readonly GuideNode root;
        public FakeDialog(GuideNode root) => this.root = root;
        public override bool Hidden => false;
        public override GuideNode Root => root;
    }

    [Test]
    public void TheOpenRevivalDialogLeavesTheWorldArrowAndTheCountdownBlocksIt()
    {
        // UIRevivalDialog's ctor sets flag 0, always on (uirevivaldialog.cpp:163), so FUN_141adcbb0's walk skips it;
        // UIAutoReviveCountDownDialog's sets only flag 1 (not closable), so it counts as a shown dialog
        Model();
        UiController Open(UiController c)
        {
            var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            var host = new UiHost(p, c, Model());
            Assert.That(host.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 10; i++) p.Tick();
            Assert.That(c.IsClosed, Is.False, c.Movie + " is shown");
            return c;
        }
        var revival = new Ms2.Ui.Hud.RevivalDialogController { CommonById = id => $"#{id:x}", CommonByKey = k => k + "|{0}" };
        var e = revival.Env;
        e.Field = () => new Ms2.Ui.Hud.RevivalField(0, 1, false, false);
        e.FieldId = () => 100;
        e.Send = _ => { };
        e.NoticeById = _ => { };
        e.Notice = _ => { };
        e.ShowBox = _ => { };
        e.ReplaceBox = _ => { };
        e.Level = () => 30;
        e.Meso = () => 0;
        Open(revival);
        Assert.That(Ms2.Ui.Hud.DialogGates.AnyDialogShown(new UiController[] { revival }), Is.False, "the revival dialog does not block");
        var countdown = new Ms2.Ui.Hud.AutoReviveCountDownController();
        Open(countdown);
        Assert.That(Ms2.Ui.Hud.DialogGates.AnyDialogShown(new UiController[] { revival, countdown }), Is.True, "the countdown blocks");
    }
}
