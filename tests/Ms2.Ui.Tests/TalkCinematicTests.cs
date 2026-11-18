using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The npc talk window (uitalkcinematic) run by its own ActionScript with UITalkCinematic's C++ side (needs MS2_DATA).</summary>
public class TalkCinematicTests
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

    const int Guard = 11000349;     // the Royal Guard by the Tria spawn plaza; the illust name is synthetic

    static TalkCinematicController.TalkContent Choices() => new()
    {
        Script = "Welcome to Tria. How may I help you?",
        Distractors = { ("Sell me on this Tria business.", true), ("What happened to the empress's celebration?", true), ("Hey there. I'm new in town.", true) },
    };

    static (UiHost, TalkCinematicController, Player, Runtime) Load(TalkCinematicController.TalkContent first, int buttons)
    {
        Packs();
        var c = new TalkCinematicController
        {
            Talking = true, TalkNpcId = Guard, Content = first, ButtonSet = buttons,
            NpcName = id => id == Guard ? "Royal Guard" : "?",
            NpcIllust = id => id == Guard ? "guard_normal" : "",
            FileExists = p => p.EndsWith("guard_normal.dds"),
            ClientWidth = 1280, ClientHeight = 720, MovieScale = 1.43,
        };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1837; p.StageH = 1034;
        var h = new UiHost(p, c, new UiModel(new GameTables(Packs(), "en")));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 60 && !c.IsLoaded; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, p, rt);
    }

    static object Script(UiHost h) => h.Invoke(h.Get(h.Entity, "m_container"), "getChildByName", "m_script");

    [Test]
    public void TheChoicePageShowsNameTextChoicesAndBothButtons()
    {
        var (h, c, p, rt) = Load(Choices(), 9);
        Assert.That(c.IsLoaded, Is.True, "the movie calls OnLoadComplete when its script panel comes in (onShowScript, frame 30)");
        Assert.That(c.IsTalkComplete, Is.True, "the typing reports OnTalkComplete");
        var script = Script(h);
        // getUniformFontSize: the text size steps down only while a line (height + leading) is taller than 46
        var tf = h.Get(h.FindNamed("typingText"), "textField");
        var lm = h.Invoke(tf, "getLineMetrics", 0);
        double size = Rt.ToNumber(h.Get(h.Invoke(tf, "getTextFormat"), "size"));
        TestContext.WriteLine($"text size {size}, line {h.Get(lm, "height")} + {h.Get(lm, "leading")}");
        Assert.That(Rt.ToNumber(h.Get(lm, "height")) + Rt.ToNumber(h.Get(lm, "leading")), Is.LessThanOrEqualTo(46));
        Assert.That(size, Is.GreaterThan(20), "the line height excludes the leading (it is not counted twice)");
        var npc = h.Get(script, "m_npc");
        Assert.That(Rt.ToString(h.Get(h.Get(npc, "textField"), "text")), Is.EqualTo("Royal Guard"));
        // appendDistractor: talk_button_distractor "distractor0".. under the script's container, the first selected
        var labels = Enumerable.Range(0, 3).Select(i => h.FindNamed("distractor" + i)).ToList();
        Assert.That(labels, Has.None.Null);
        Assert.That(labels.Select(b => Rt.ToString(h.Get(b, "label"))), Is.EqualTo(Choices().Distractors.Select(d => d.Text)));
        Assert.That(h.FindNamed("distractor3"), Is.Null);
        Assert.That(Rt.ToBoolean(h.Get(labels[0], "selected")), Is.True, "the first choice is highlighted");
        Assert.That(Rt.ToBoolean(h.Get(labels[1], "selected")), Is.False);
        var buttons = h.Get(script, "m_buttons");
        var end = h.Get(buttons, "m_end"); var next = h.Get(buttons, "m_next");
        Assert.That(Rt.ToBoolean(h.Get(end, "visible")) && Rt.ToBoolean(h.Get(next, "visible")), Is.True);
        TestContext.WriteLine($"buttons: [{Rt.ToString(h.Get(next, "label"))}] [{Rt.ToString(h.Get(end, "label"))}]");
        Assert.That(Rt.ToString(h.Get(next, "label")), Does.StartWith("Next"));
        Assert.That(Rt.ToString(h.Get(end, "label")), Does.StartWith("Close"));
        // the illust: lNpc is the talk npc's, speaking (setNpcMovie keeps it in the clip's illust)
        var left = h.FindNamed("m_lNpc");
        Assert.That(Rt.ToString(h.Get(left, "illust")), Is.EqualTo("./Data/Resource/Image/Quest/illust/guard_normal.dds"));
        Assert.That(Rt.ToBoolean(h.Get(left, "visible")), Is.True);
        Assert.That(Rt.ToString(h.Get(h.FindNamed("m_rNpc"), "illust")), Is.EqualTo(""), "no right illust: setNpcMovie(m_rNpc, \"\")");
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void NextOnAChoicePicksTheFocusedChoice()
    {
        var (h, c, p, _) = Load(Choices(), 9);
        int picked = -1;
        c.SendNext += i => picked = i;
        c.Key(TalkCinematicController.TalkKey.Down);
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(picked, Is.EqualTo(1), "Down then Next: onSelect calls back OnSelect(1), SendNpcTalkMode2(1)");
    }

    [Test]
    public void PagesAdvanceAndTheLastHasOnlyClose()
    {
        var (h, c, p, _) = Load(new() { Script = "Page one." }, 4);
        int requests = 0;
        c.SendNext += i => requests++;
        c.Key(TalkCinematicController.TalkKey.Next);
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(requests, Is.EqualTo(1), "one request until the answer comes (m_nStarted, m_nPending)");
        Assert.That(c.OnServerPage(new() { Script = "The last page." }, 3), Is.True);
        var buttons = h.Get(Script(h), "m_buttons");
        Assert.That(Rt.ToBoolean(h.Get(h.Get(buttons, "m_end"), "visible")), Is.False);
        Assert.That(Rt.ToString(h.Get(h.Get(buttons, "m_next"), "label")), Does.StartWith("Close"));
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(requests, Is.EqualTo(2));
    }

    [Test]
    public void EscEndsTheTalkAndTheMovieClosesItself()
    {
        var (h, c, p, _) = Load(new() { Script = "Page one." }, 4);
        bool sent = false, reset = false, closed = false;
        c.SendEnd += () => sent = true;
        c.TalkReset += () => reset = true;
        c.CloseComplete += () => closed = true;
        c.Key(TalkCinematicController.TalkKey.End);
        Assert.That(sent && reset, Is.True);
        Assert.That(c.Talking, Is.False);
        for (int i = 0; i < 40 && !closed; i++) p.Tick();
        Assert.That(closed, Is.True, "the hide timeline ends with onCloseComplete (onHideScript, frame 56)");
    }
}
