using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The chat balloon (uipopupimageballoon) run by its own ActionScript and fed by ChatBalloonController (needs MS2_DATA).</summary>
public class ChatBalloonTests
{
    static PackSet packs;
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
        }
        return new UiModel(tables);
    }

    static UiHost Load(UiController c)
    {
        var m = Model();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return h;
    }

    [Test]
    public void AnNpcsBalloonIsMadeOnFirstUseAndOpensWithItsTextAndPortrait()
    {
        long now = 10_000;
        var c = new ChatBalloonController { TickMs = () => now, ProgressiveLoading = true, DefaultImage = "default.png" };
        var h = Load(c);
        var npc = new ChatBalloonController.Actor { ObjectId = 7, IsNpc = true, Portrait = "portrait.png", Offset = 20, Distance = 100 };
        // CNpc::vfunc_85: none before the actor is added under ProgressiveLoading
        Assert.That(c.NpcBalloon(npc), Is.Null);
        Assert.That(c.ClipsMade, Is.EqualTo(0));
        npc.ActorAdded = true;
        var e = c.NpcBalloon(npc);
        Assert.That(e, Is.Not.Null);
        Assert.That(c.NpcBalloon(npc), Is.SameAs(e));
        Assert.That(c.ClipsMade, Is.EqualTo(1));
        Assert.That(e.Active, Is.False);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
        // an inactive balloon is skipped
        c.Process(_ => (500f, 400f));
        Assert.That(e.Opens, Is.EqualTo(0));
        ChatBalloonController.SetText(e, "0123");
        c.Start(e);
        c.Process(_ => (500f, 400f));
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(e.Opens, Is.EqualTo(1));
        Assert.That(e.Opened, Is.EqualTo(("0123", "portrait.png", true, "")));
        // open() adds the new imageballoon_balloon (a PopupImageBalloon) to m_dummy after its m_normal and m_dead
        var dummy = h.Get(e.Clip, "m_dummy");
        var popup = h.Invoke(dummy, "getChildAt", Rt.ToNumber(h.Get(dummy, "numChildren")) - 1);
        Assert.That(Rt.ToString(h.Invoke(popup, "toString")), Is.EqualTo("PopupImageBalloon"));
        Assert.That(Rt.ToString(h.Get(h.Get(popup, "m_textField"), "text")), Is.EqualTo("0123"));
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "x")), Is.EqualTo(500 - 110));
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "y")), Is.EqualTo(400 + 20 - 90));
        // the timer runs out: closed once, still active (m_4c) until stopped
        now += c.Duration + 1;
        c.Process(_ => (500f, 400f));
        c.Process(_ => (500f, 400f));
        Assert.That(e.Closes, Is.EqualTo(1));
        Assert.That(e.Active, Is.True);
        c.Stop(e);
        Assert.That(e.Active, Is.False);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void AnNpcWithoutPortraitShowsTheDefaultImageAndAReleasedClipIsPooled()
    {
        var c = new ChatBalloonController { ProgressiveLoading = false, DefaultImage = "default.png" };
        Load(c);
        var a = new ChatBalloonController.Actor { ObjectId = 1, IsNpc = true };
        Assert.That(c.ImagePath(a), Is.EqualTo("default.png"));
        var e = c.NpcBalloon(a);
        c.RemoveNpc(a);
        Assert.That(c.Entities.ContainsKey(1), Is.False);
        var b = new ChatBalloonController.Actor { ObjectId = 2, IsNpc = true };
        Assert.That(c.NpcBalloon(b), Is.SameAs(e));
        Assert.That(c.ClipsMade, Is.EqualTo(1));
    }

    [Test]
    public void AnActiveBalloonHidesTheNpcsEmoticonOfKindZero()
    {
        long now = 10_000;
        var bc = new ChatBalloonController { TickMs = () => now, ProgressiveLoading = false };
        Load(bc);
        var ec = new EmoticonController { TickMs = () => now };
        var h = Load(ec);
        var b = new ChatBalloonController.Actor { ObjectId = 1, IsNpc = true, Distance = 100 };
        var npc = new EmoticonController.Actor
        {
            ObjectId = 1, IsNpc = true, ActorAdded = true, NpcKind = 0xb, NpcId = 11000004, Friendly = 2, Distance = 100,
            Balloon = () => bc.NpcBalloon(b),
        };
        var e = ec.SpawnNpc(npc, () => NpcQuestMarks.Mark.None);
        Assert.That(e.Kind, Is.EqualTo(0));
        ec.Process(_ => (500f, 400f, 0f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True);
        // the hide test made the balloon (vfunc_85); started, it hides the emoticon
        Assert.That(bc.Entities.ContainsKey(1), Is.True);
        bc.Start(bc.NpcBalloon(b));
        ec.Process(_ => (500f, 400f, 0f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
        bc.Stop(bc.NpcBalloon(b));
        ec.Process(_ => (500f, 400f, 0f));
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True);
        Assert.That(h.RT.Errors, Is.Empty);
    }
}
