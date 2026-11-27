using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MessengerBrowserStamp (CSceneStateGame::OnMessengerBrowserStamp, scenestategame.cpp:6842): the chat types the
/// browser hides leave every window's target list, and a window gets its kept target back once none is hidden (needs MS2_DATA).</summary>
public class MessengerStampTests
{
    static PackSet packs;
    static KeyTable keys;
    static MovieSource src;
    static GameTables tables;

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
        m.Player.Name = "Me";
        m.ServerTime = () => new DateTime(2026, 1, 1, 9, 5, 0);
        return m;
    }

    static (MessengerController c, UiHost h) Window(UiModel m, MessengerBrowser browser, int index)
    {
        var c = new MessengerController(m.Tables.DefaultMessengers[0], $"messengerdialog{index}.xml", true, browser);
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return (c, h);
    }

    static AsObject Subject(UiHost h) => (AsObject)h.Child("m_subject");
    static int Rows(UiHost h) => (int)Rt.ToNumber(h.Get(h.Get(Subject(h), "dataProvider"), "length"));
    static int Selected(UiHost h) => (int)Rt.ToNumber(h.Get(Subject(h), "selectedIndex"));
    static MessengerBrowserStamp Stamp(params int[] types) => new(types);

    [Test]
    public void EachListedTypeHiddenAloneTakesOneRowAndOthersNone()
    {
        var m = Model();
        var browser = new MessengerBrowser { Model = m };
        var (_, h) = Window(m, browser, 0);
        int all = Rows(h);
        bool super = m.IsFeatureEnabled(MessengerController.SuperChatFeature);
        var listed = new[] { MessengerController.Super, MessengerController.World, MessengerController.Channel, MessengerController.Social,
            MessengerController.Party, MessengerController.Guild, MessengerController.GuildMegaphone };
        Assert.That(all, Is.EqualTo(super ? 7 : 6), "the target list's rows");
        foreach (int t in listed)
        {
            browser.OnStamp(Stamp(t));
            int expected = t == MessengerController.Super && !super ? all : all - 1;
            Assert.That(Rows(h), Is.EqualTo(expected), $"type 0x{t:x} hidden");
        }
        // types no row stands for (whisper, the club and an unknown one) take none
        foreach (int t in new[] { MessengerController.Talk, 0x1b, 0x1c, 0xFFFF })
        {
            browser.OnStamp(Stamp(t));
            Assert.That(Rows(h), Is.EqualTo(all), $"type 0x{t:x} hidden");
        }
        browser.OnStamp(Stamp(MessengerController.World, MessengerController.Channel, MessengerController.Guild));
        Assert.That(Rows(h), Is.EqualTo(all - 3));
        Assert.That(browser.HiddenTypes, Is.EquivalentTo(new[] { 0xb, 0xc, 8 }), "a stamp replaces the hidden types");
        browser.OnStamp(Stamp());
        Assert.That(Rows(h), Is.EqualTo(all));
        Assert.That(browser.HasHiddenTypes, Is.False);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void EveryWindowKeepsItsTargetWhileTypesAreHiddenAndGetsItBack()
    {
        var m = Model();
        var browser = new MessengerBrowser { Model = m };
        var (c0, h0) = Window(m, browser, 0);
        var (c1, h1) = Window(m, browser, 1);
        int all = Rows(h0);
        // each window's own target: the last row and the second
        foreach (var (c, h, row) in new[] { (c0, h0, all - 1), (c1, h1, 1) })
        {
            h.Set(Subject(h), "selectedIndex", row);
            c.OnCallback("eventChangeTalkTarget", Array.Empty<object>());
            Assert.That(Selected(h), Is.EqualTo(row));
        }
        // world and channel hidden: the rows go in both windows, the last row's index is out of the list
        browser.OnStamp(Stamp(MessengerController.World, MessengerController.Channel));
        Assert.That((Rows(h0), Rows(h1)), Is.EqualTo((all - 2, all - 2)));
        // none hidden: each window's kept row (RestoreSubject's m_nHistoryIndex) is selected again
        browser.OnStamp(Stamp());
        Assert.That((Rows(h0), Rows(h1)), Is.EqualTo((all, all)));
        Assert.That((Selected(h0), Selected(h1)), Is.EqualTo((all - 1, 1)));
        Assert.That(h0.RT.Errors, Is.Empty);
        Assert.That(h1.RT.Errors, Is.Empty);
    }
}
