using System.Runtime.CompilerServices;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The trigger's SetVisibleUI (type 0x14, CTriggerService::FUN_141862d30, triggerservice.cpp:707-722): each named
/// UI object (FindObjectByName, by GetTypeName, case blind) whose vfunc_9 is not the state gets vfunc_8(state).</summary>
public class TriggerVisibleUiTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static KeyTable keys;

    static UiModel Model()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
            keys = new KeyTable(packs, "en");
        }
        var m = new UiModel(tables) { Keys = keys };
        UiFixture.Apply(m);
        m.HasLocalPlayer = true;
        return m;
    }

    static UiHost Load(UiController c, UiModel m)
    {
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return h;
    }

    static bool Shown(UiController c) => Rt.ToBoolean(c.Host.Get(c.Host.Entity, "visible"));

    [Test]
    public void TheNamesAreTheNonEmptyCommaTokensUntrimmed()
    {
        Assert.That(TriggerVisibleUi.Names("PartyDialog"), Is.EqualTo(new[] { "PartyDialog" }));
        Assert.That(TriggerVisibleUi.Names("A,,B,"), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(TriggerVisibleUi.Names(" A,B"), Is.EqualTo(new[] { " A", "B" }));
        Assert.That(TriggerVisibleUi.Names(""), Is.Empty);
        Assert.That(TriggerVisibleUi.Names(null), Is.Empty);
    }

    /// <summary>GetTypeName of the HUD movies the trigger data names: what FindObjectByName finds them by.</summary>
    [TestCase(typeof(ExpBarController), "ExpBar", "UIExpBar::GetTypeName @0x141931f90 uiexpbar.cpp:94")]
    [TestCase(typeof(UpperHudController), "UpperHudDialog", "UIUpperHudDialog::GetTypeName @0x141353e80 uiupperhuddialog.cpp:245")]
    [TestCase(typeof(MinimapController), "MinimapDialog", "UIMinimapDialog::GetTypeName @0x141a35ba0 uiminimapentity.cpp:1099")]
    [TestCase(typeof(QuestGuideController), "QuestGuideDialog", "UIQuestGuideDialog::GetTypeName @0x141aa9a90 uiquesteventlistener.cpp:350")]
    [TestCase(typeof(PartyController), "PartyDialog", "UIPartyDialog::GetTypeName @0x141a74d00 uipartydialog.cpp:412")]
    [TestCase(typeof(GroupMessengerBrowserController), "GroupMessengerBrowser", "UIGroupMessengerBrowser::GetTypeName @0x1419509c0 uigroupmessenger.cpp:1451")]
    public void TheHudMoviesHaveTheClientsTypeNames(Type controller, string name, string cite) =>
        Assert.That(((UiController)RuntimeHelpers.GetUninitializedObject(controller)).TypeName, Is.EqualTo(name), cite);

    [Test]
    public void EachFoundObjectTakesTheStateAndAMissingOneIsSkipped()
    {
        var m = Model();
        var exp = new ExpBarController();
        var upper = new UpperHudController { MouseMode = false };
        Load(exp, m);
        Load(upper, m);
        var browser = new MessengerBrowser { Model = m };
        var chat = new MessengerController(m.Tables.DefaultMessengers[0], "messengerdialog0.xml", true, browser);
        Load(chat, m);
        var movies = new List<UiController> { exp, upper, chat };
        Assert.That((Shown(exp), Shown(upper), Shown(chat)), Is.EqualTo((true, true, true)), "a loaded movie is shown (m_58 1)");

        // the longest list the data has: the names with no object here (the ad push, the snowman event, the quest tracker
        // and minimap, which this list of movies lacks) find nothing and are skipped
        const string list = "UpperHudDialog,MessengerBrowser,ExpBar,GroupMessengerBrowser,QuestGuideDialog,MinimapDialog,AdPushDialog,SnowmanEventDialog";
        var found = TriggerVisibleUi.Apply(0, list, movies, browser);
        Assert.That(found, Is.EqualTo(new[] { "UpperHudDialog", "MessengerBrowser", "ExpBar" }));
        Assert.That((Shown(exp), Shown(upper), Shown(chat)), Is.EqualTo((false, false, false)));
        Assert.That(TriggerVisibleUi.Visible(browser), Is.EqualTo(0));

        // case blind (CStringElementTraitsI), and state 1 shows them again
        Assert.That(TriggerVisibleUi.Apply(1, "expbar,messengerbrowser", movies, browser), Is.EqualTo(new[] { "expbar", "messengerbrowser" }));
        Assert.That((Shown(exp), Shown(upper), Shown(chat)), Is.EqualTo((true, false, true)));
        Assert.That(TriggerVisibleUi.Visible(browser), Is.EqualTo(1), "vfunc_9: any window shown");
    }

    [Test]
    public void AnUnloadedMovieAnswersZeroAndTakesNothing()
    {
        var exp = new ExpBarController();
        Assert.That(TriggerVisibleUi.Visible(exp), Is.EqualTo(0), "vfunc_9 with no clip");
        Assert.DoesNotThrow(() => TriggerVisibleUi.SetVisible(exp, 1));
        Assert.That(TriggerVisibleUi.Apply(1, "ExpBar", new List<UiController> { exp }, null), Is.EqualTo(new[] { "ExpBar" }));
        Assert.That(TriggerVisibleUi.Apply(1, "MessengerBrowser", new List<UiController>(), null), Is.Empty, "no browser, no object");
    }

    /// <summary>The client's object for each name the trigger data gives (GetTypeName), and the port that answers it here:
    /// a controller type, the messenger browser, or none (no port: the name is skipped, as for an unloaded movie).</summary>
    static readonly Dictionary<string, (Type Port, string Cite)> ClientObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PartyDialog"] = (typeof(PartyController), "uipartydialog.cpp:412"),
        ["MessengerBrowser"] = (typeof(MessengerBrowser), "uimessenger.cpp:2721"),
        ["GroupMessengerBrowser"] = (typeof(GroupMessengerBrowserController), "uigroupmessenger.cpp:1451"),
        ["HighlightGameMenu"] = (null, "UIHighlightGameMenu::GetTypeName @0x140c22690 uihighlightgamemenu.cpp:429"),
        ["UpperHudDialog"] = (typeof(UpperHudController), "uiupperhuddialog.cpp:245"),
        ["ExpBar"] = (typeof(ExpBarController), "uiexpbar.cpp:94"),
        ["QuestGuideDialog"] = (typeof(QuestGuideController), "uiquesteventlistener.cpp:350"),
        ["MinimapDialog"] = (typeof(MinimapController), "uiminimapentity.cpp:1099"),
        ["AdPushDialog"] = (null, "a registered dialog name (RegisteredDialogs); no class in the client source"),
        ["SnowmanEventDialog"] = (null, "UISnowmanEventDialog::GetTypeName @0x140e94560 uisnowmaneventdialog.cpp:104"),
    };

    /// <summary>Every SetVisibleUI action in the trigger xmls (Xml.m2d trigger/): its visible and its uiName list.</summary>
    static List<(string File, int State, string Names)> Uses(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var uses = new List<(string, int, string)>();
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("SetVisibleUI"u8) < 0 && bytes.AsSpan().IndexOf("setvisibleui"u8) < 0) continue;
            System.Xml.Linq.XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            foreach (var a in doc.Descendants("action"))
                if (string.Equals((string)a.Attribute("name"), "SetVisibleUI", StringComparison.OrdinalIgnoreCase))
                    uses.Add((entry, int.Parse((string)a.Attribute("visible") ?? "0"), (string)a.Attribute("uiName") ?? ""));
        }
        return uses;
    }

    static void EveryUse(string variable)
    {
        var uses = Uses(variable);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (file, state, names) in uses)
        {
            Assert.That(state, Is.InRange(0, 1), file);
            var split = TriggerVisibleUi.Names(names);
            Assert.That(split, Is.Not.Empty, file);
            foreach (var n in split)
            {
                Assert.That(ClientObjects.ContainsKey(n), Is.True, $"{file}: {n} is a client UI object name");
                counts[n] = counts.GetValueOrDefault(n) + 1;
                var port = ClientObjects[n].Port;
                if (port == null || port == typeof(MessengerBrowser)) continue;
                var name = ((UiController)RuntimeHelpers.GetUninitializedObject(port)).TypeName;
                Assert.That(name, Is.EqualTo(n).IgnoreCase, $"{file}: {n} finds {port.Name} ({ClientObjects[n].Cite})");
            }
        }
        TestContext.WriteLine($"{variable}: {uses.Count} uses in {uses.Select(u => u.File).Distinct().Count()} files; "
            + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}{(ClientObjects[kv.Key].Port == null ? " (no port)" : "")}")));
        Assert.That(uses.Count, Is.GreaterThan(0));
    }

    [Test] public void EveryKmsUse() => EveryUse("MS2_DATA");
    [Test] public void EveryGms2Use() => EveryUse("MS2_GMS2_DATA");
}
