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

    static (UiHost, TalkCinematicController, Player, Runtime) Load(TalkCinematicController.TalkContent first, int buttons,
                                                                   Action<TalkCinematicController> setup = null, int jobGroup = 0)
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
        setup?.Invoke(c);
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1837; p.StageH = 1034;
        // the movie translator (GFxManager::Localize; the game's As3View reads korflash): the one key these pages format
        p.Translate = k => k == "$s_talk_script_reward_exp" ? "{0} Experience" : null;
        var model = new UiModel(new GameTables(Packs(), "en"));
        model.Player.JobGroup = jobGroup;
        var h = new UiHost(p, c, model);
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

    const int Quest = 90000000;     // "A New Path": exp 450, meso 1000, item 20000028 x10 (questdata.xml)

    static TalkCinematicController.QuestReward NewPathReward() => new() { Exp = 450, Meso = 1000, ChoiceRewards = { (20000028, 1, 10) } };

    [Test]
    public void TheAcceptPageAcceptsTheQuestAndAsksForTheNextPage()
    {
        var (h, c, p, rt) = Load(new() { Script = "Will you help me?", QuestId = Quest }, 6);
        var accepted = new List<(int, int)>(); int next = -1;
        c.SendQuestAccept += (q, n) => accepted.Add((q, n));
        c.SendNext += i => next = i;
        var buttons = h.Get(Script(h), "m_buttons");
        TestContext.WriteLine($"buttons: [{Rt.ToString(h.Get(h.Get(buttons, "m_next"), "label"))}] [{Rt.ToString(h.Get(h.Get(buttons, "m_end"), "label"))}]");
        Assert.That(Rt.ToString(h.Get(h.Get(buttons, "m_next"), "label")), Does.StartWith("Accept"), "s_quest_talk_accept (0x5d3)");
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(accepted, Is.EqualTo(new[] { (Quest, Guard) }), "Quest mode 2 (quest, talk npc)");
        Assert.That(next, Is.EqualTo(0), "then NpcTalk mode 2 with 0");
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void ARedirectPagesAcceptGoesOnWithTheQuestInsteadOfTheNextPage()
    {
        var (h, c, p, rt) = Load(new() { Script = "Will you help me?", QuestId = Quest, Redirect = 1 }, 6, x => x.AcceptRedirects = q => q == Quest);
        var redirect = new List<(int, short)>(); int next = -1; bool accepted = false;
        c.SendQuestAccept += (q, n) => accepted = true;
        c.SendQuestRedirect += (q, k) => redirect.Add((q, k));
        c.SendNext += i => next = i;
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(accepted, Is.True, "Quest mode 2 first");
        Assert.That(redirect, Is.EqualTo(new[] { (Quest, (short)2) }), "NpcTalk mode 7 (quest, 2)");
        Assert.That(next, Is.EqualTo(-1), "no mode 2");
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void AProgressPageOfASummonPortalQuestSendsQuestMode0x12()
    {
        var (h, c, p, rt) = Load(new() { Script = "Not done yet?", QuestId = Quest }, 8, x =>
        {
            x.QuestOf = q => q == Quest ? new("A New Path", 10, "_epic") : null;
            x.HasSummonPortal = q => q == Quest;
        });
        var progress = new List<int>(); int next = -1;
        c.SendQuestProgress += progress.Add;
        c.SendNext += i => next = i;
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(progress, Is.EqualTo(new[] { Quest }));
        Assert.That(next, Is.EqualTo(0));
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void AnUnrequireGroupQuestAsksBeforeTheAccept()
    {
        foreach (int answer in new[] { 0, 1 })
        {
            MessageBoxData box = null;
            var (h, c, p, rt) = Load(new() { Script = "Will you help me?", QuestId = Quest }, 6, x =>
            {
                x.QuestOf = q => q == Quest ? new("A New Path", 10, "_epic") : null;
                x.HasUnrequireGroup = q => q == Quest;
                x.ShowBox = d => box = d;
            });
            bool accepted = false, ended = false; int next = -1;
            c.SendQuestAccept += (q, n) => accepted = true;
            c.SendEnd += () => ended = true;
            c.SendNext += i => next = i;
            c.Key(TalkCinematicController.TalkKey.Next);
            Assert.That(box, Is.Not.Null, "s_alliance_quest_unreq_group_warn");
            Assert.That((box.Type, accepted, next), Is.EqualTo((1, false, -1)), "nothing is sent before the answer");
            box.InvokeCallback(answer, null);
            if (answer == 0) Assert.That((accepted, next, ended), Is.EqualTo((true, 0, false)), "OK: SendQuestAccept");
            else Assert.That((accepted, ended), Is.EqualTo((false, true)), "cancel: SendNpcTalkMode0");
            Assert.That(rt.Errors, Is.Empty);
        }
    }

    [Test]
    public void AnActiveQuestIsNotAcceptedAgain()
    {
        var (h, c, p, _) = Load(new() { Script = "Will you help me?", QuestId = Quest }, 6, x => x.IsQuestActive = q => q == Quest);
        bool sent = false;
        c.SendQuestAccept += (q, n) => sent = true;
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(sent, Is.False, "IsQuestActive: the accept does nothing");
    }

    [Test]
    public void TheQuestTalkReadsItsScriptsAndTheirText()
    {
        var qs = new Ms2.Formats.World.QuestScriptTable(Packs(), (e, c, k) => FeatureElements.Collect(Packs(), e, c, k));
        Assert.That(qs.ScriptIds(Quest), Is.EqualTo(new[] { 100, 200, 300 }), "script/quest/questscript*.xml");
        var complete = qs.Find(Quest, 300);
        Assert.That(complete.Contents, Has.Count.EqualTo(1));
        Assert.That(complete.Contents[0].QuestId, Is.EqualTo(Quest), "FUN_140859160 :595: the page carries the quest id");
        // $script:0831180104000004$ is in string/en/scriptquest_*.xml, the text of the capture's complete page
        var npcs = new Ms2.Formats.World.NpcScriptTable(Packs(), new Ms2.Formats.World.GameStrings(Packs(), "en"));
        Assert.That(npcs.Resolve(complete.Contents[0].Text, "Solo"),
                    Does.StartWith("Solo, you completed your job advancement! Congratulations!"));
    }

    [Test]
    public void TheCompleteRewardKeepsTheItemsOfThePlayersJobAndAddsTheGuildCoins()
    {
        // 20000767 is recommended for job group 10, 20000768 for 20 (itemdata/200.xml); the player is a 20
        var (h, c, p, rt) = Load(new() { Script = "Well done.", QuestId = Quest }, 7, x =>
        {
            x.QuestOf = q => q == Quest ? new("A New Path", 10, "_epic") : null;
            x.QuestRewardOf = q => q == Quest ? new() { Exp = 1, GuildCoin = 3, ChoiceRewards = { (20000767, 1, 1), (20000768, 1, 2) } } : null;
        }, jobGroup: 20);
        Assert.That(c.Model.ItemSlots["TalkCinematic_0"].Item.ItemId, Is.EqualTo(20000768), "FUN_140748050: the job group's item only");
        Assert.That(c.Model.ItemSlots["TalkCinematic_0"].Item.Count, Is.EqualTo(2u));
        Assert.That(c.Model.ItemSlots["TalkCinematic_1"].Item.ItemId, Is.EqualTo(TalkCinematicController.GuildCoinItem), ":697 the guild coin item");
        Assert.That(c.Model.ItemSlots["TalkCinematic_1"].Item.Count, Is.EqualTo(3u));
        Assert.That(c.Model.ItemSlots.ContainsKey("TalkCinematic_2"), Is.False);
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void TheCompletePageShowsTheQuestRewardAndCompletes()
    {
        var (h, c, p, rt) = Load(new() { Script = "Well done.", QuestId = Quest }, 7, x =>
        {
            x.QuestOf = q => q == Quest ? new("A New Path", 10, "_epic") : null;
            x.QuestRewardOf = q => q == Quest ? NewPathReward() : null;
        });
        var buttons = h.Get(Script(h), "m_buttons");
        Assert.That(Rt.ToString(h.Get(h.Get(buttons, "m_next"), "label")), Does.StartWith("Complete"), "s_quest_talk_complete (0x5d4)");
        Assert.That(c.Model.ItemSlots.ContainsKey("TalkCinematic_0"), Is.True, "FillRewardItems: slot <type name>_0 for the first item");
        Assert.That(c.Model.ItemSlots["TalkCinematic_0"].Item.Count, Is.EqualTo(10u));
        var item1 = h.FindNamed("m_rewardItem1");
        Assert.That(item1, Is.Not.Null, "appendReward adds talk_movie_reward");
        Assert.That(Rt.ToBoolean(h.Get(item1, "visible")), Is.True);
        Assert.That(Rt.ToBoolean(h.Get(h.FindNamed("m_rewardItem2"), "visible")), Is.False);
        var exp = h.FindNamed("tfExp"); var meso = h.FindNamed("tfMeso");
        TestContext.WriteLine($"exp [{(exp == null ? "-" : Rt.ToString(h.Get(exp, "text")))}] meso [{(meso == null ? "-" : Rt.ToString(h.Get(meso, "text")))}]");
        if (exp != null) Assert.That(Rt.ToString(h.Get(exp, "text")), Does.Contain("450"));
        if (meso != null) Assert.That(Rt.ToString(h.Get(meso, "text")), Does.Contain("1,000"));
        var done = new List<(int, int, int)>();
        c.SendQuestComplete += (q, n, ch) => done.Add((q, n, ch));
        TestContext.WriteLine($"typed: {c.IsTalkComplete}");
        for (int i = 0; i < 120 && !c.IsTalkComplete; i++) p.Tick();      // the reward panel comes in after the typing
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(done, Is.EqualTo(new[] { (Quest, Guard, 0) }), "Quest mode 4 (quest, npc, choice 0)");
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void TheSelectListsTheQuestsThenTheTalk()
    {
        var (h, c, p, rt) = Load(new() { Script = "Hello there." }, 5, x =>
        {
            x.Flags = 2 | 4 | 8;
            x.QuestIds.Add(Quest);
            x.QuestOf = q => q == Quest ? new("A New Path", 10, "_epic") : null;
            x.QuestStateString = q => 0x5d9;
        });
        var d0 = h.FindNamed("distractor0"); var d1 = h.FindNamed("distractor1");
        Assert.That(d0, Is.Not.Null); Assert.That(d1, Is.Not.Null);
        string t0 = Rt.ToString(h.Get(d0, "label")), t1 = Rt.ToString(h.Get(d1, "label"));
        TestContext.WriteLine($"choices: [{t0}] [{t1}]");
        Assert.That(t0, Does.Contain("A New Path"), "s_quest_distactor_quest_name with the quest's name");
        Assert.That(t1, Is.Not.Empty, "the talk choice (s_action_talk_normal_cinematic)");
        int picked = -1;
        c.SendNext += i => picked = i;
        c.Key(TalkCinematicController.TalkKey.Next);
        Assert.That(picked, Is.EqualTo(0), "Next picks the focused choice: the quest");
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void ATalkRewardShowsOnAnOpenTalkRewardPageOnce()
    {
        var (h, c, p, rt) = Load(new() { Script = "Here you go.", OpenTalkReward = true }, 4, x => x.OnRewardMeso(500));
        Assert.That(h.FindNamed("m_rewardItem1"), Is.Not.Null, "FillTalkReward put a reward on the page");
        Assert.That(c.Reward.Meso, Is.EqualTo(0), "and cleared the reward data (FUN_141b1dbd0)");
        Assert.That(rt.Errors, Is.Empty);
    }
}
