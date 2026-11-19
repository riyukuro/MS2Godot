using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The npc monologue (CNpc::UpdateMonologue, UITalkManager::ShowNpcMonologue) over the npc scripts and the
/// uipopupimageballoon movie (needs MS2_DATA).
/// </summary>
public class NpcMonologueTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static NpcScriptTable scripts;

    static void Data()
    {
        if (packs != null) return;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        tables = new GameTables(packs, "en");
        scripts = new NpcScriptTable(packs, new GameStrings(packs, "en"), e => FeatureElements.IsAvailable(packs, e));
    }

    static UiHost Load(UiController c)
    {
        Data();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return h;
    }

    static Func<uint> Seq(params uint[] v)
    {
        int i = 0;
        return () => v[Math.Min(i++, v.Length - 1)];
    }

    [Test]
    public void MonologuesCarryTheirPopupStateAndWeight()
    {
        Data();
        // npc 11003520: two <monologue popupProp="10">, no popupState (the CTalkScript default 9)
        var list = scripts.Monologues(11003520);
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Select(s => s.Id), Is.EqualTo(new[] { 10, 20 }));
        Assert.That(list.All(s => s.PopupState == 9 && s.PopupProp == 10), Is.True);
        // an npc without popup attributes: weight 100
        Assert.That(scripts.Monologues(11000004)?.All(s => s.PopupState == 9 && s.PopupProp == 100), Is.True);
        Assert.That(scripts.Monologues(11003520).All(s => s.Kind == "monologue" && s.Contents.Count > 0), Is.True);
        // the npc keeps them among its scripts too
        Assert.That(scripts.Find(11003520).Scripts.Count(s => s.Kind == "monologue"), Is.EqualTo(2));
        // a mob (friendly 0) has monologues too: UpdateMonologue has no mob test
        Assert.That(scripts.Monologues(21000003), Is.Not.Null.And.Not.Empty);
        Assert.That(new NpcTable(packs).Find(21000003)?.Friendly, Is.EqualTo(0));
    }

    [Test]
    public void ThePickRollsUnderTheWeightsThenByWeight()
    {
        Data();
        // total 20: a first roll of 5 (< 20) picks by 13 % 20: 10 > 13 no, 20 > 13 the second
        Assert.That(scripts.PickMonologue(11003520, 9, Seq(5, 13))?.Id, Is.EqualTo(20));
        Assert.That(scripts.PickMonologue(11003520, 9, Seq(5, 9))?.Id, Is.EqualTo(10));
        // a first roll at or over the total shows nothing (105 % 100 = 5 is under it again)
        Assert.That(scripts.PickMonologue(11003520, 9, Seq(20)), Is.Null);
        Assert.That(scripts.PickMonologue(11003520, 9, Seq(99)), Is.Null);
        Assert.That(scripts.PickMonologue(11003520, 9, Seq(105, 0))?.Id, Is.EqualTo(10));
        // no monologue of that state, or none at all
        Assert.That(scripts.PickMonologue(11003520, 7, Seq(0, 0)), Is.Null);
        Assert.That(scripts.PickMonologue(1, 9, Seq(0, 0)), Is.Null);
    }

    [Test]
    public void TheTimerShowsAMonologueThenStopsItAfterTheShowTime()
    {
        var balloons = new ChatBalloonController { ProgressiveLoading = false };
        Load(balloons);
        int talkingWith = 0;
        var voices = new List<string>();
        var m = new NpcMonologue(balloons, scripts)
        {
            IntervalMs = 15000,
            RandomMs = 10000,
            ShowTimeMs = 5000,
            Rand = Seq(3, 0, 0),
            TalkingObjectId = () => talkingWith,
            Resolve = t => scripts.Resolve(t, "P"),
            PlayVoice = (_, v) => voices.Add(v),
        };
        var npc = new NpcMonologue.Npc { ObjectId = 5, NpcId = 11003520, Balloon = new ChatBalloonController.Actor { ObjectId = 5, IsNpc = true } };
        // a quest mark: no monologue
        m.QuestState = _ => 0;
        m.Update(npc, 30f);
        Assert.That(npc.Elapsed, Is.EqualTo(0));
        m.QuestState = _ => 4;
        // the CNpc constructor's 0 head start: 24.999 s is short of 15 s + 10 s
        m.Update(npc, 24.999f);
        var e = balloons.NpcBalloon(npc.Balloon);
        Assert.That(e.Active, Is.False);
        m.Update(npc, 0.001f);
        // shown: the picked monologue's text (Rand 3 < 20, then 0 % 20: the first) with its words replaced
        Assert.That(e.Active, Is.True);
        Assert.That(e.Text, Is.EqualTo(scripts.Resolve(scripts.Monologues(11003520)[0].Contents[0].Text, "P")));
        Assert.That(e.Text, Is.Not.Empty.And.Not.Contain("$script"));
        // ResetMonologueTimer: elapsed 0, the head start from Unk_Random(5 * (Rand() % 10000)) mod 10000
        Assert.That(npc.Elapsed, Is.EqualTo(0));
        Assert.That(npc.HeadStart, Is.EqualTo((int)(new QuestRandom(0).Next() % 10000)));
        // active: MonologueShowTime stops it
        m.Update(npc, 4.9f);
        Assert.That(e.Active, Is.True);
        m.Update(npc, 0.1f);
        Assert.That(e.Active, Is.False);
        Assert.That(npc.Elapsed, Is.EqualTo(0));
        // the npc in talk gets none
        talkingWith = 5;
        m.Show(npc);
        Assert.That(e.Active, Is.False);
        // a kept balloon (a chat) stops the timer
        talkingWith = 0;
        ChatBalloonController.Keep(e);
        m.Update(npc, 60f);
        Assert.That(npc.Elapsed, Is.EqualTo(0));
        Assert.That(e.Active, Is.False);
    }

    [Test]
    public void ASubSceneStopsTheTimer()
    {
        var balloons = new ChatBalloonController { ProgressiveLoading = false };
        Load(balloons);
        var m = new NpcMonologue(balloons, scripts) { InSubScene = () => true, Rand = Seq(0) };
        var npc = new NpcMonologue.Npc { ObjectId = 9, NpcId = 11003520, Balloon = new ChatBalloonController.Actor { ObjectId = 9, IsNpc = true } };
        m.Update(npc, 100f);
        Assert.That(npc.Elapsed, Is.EqualTo(0));
        Assert.That(balloons.Entities.Count, Is.EqualTo(0));
    }
}
