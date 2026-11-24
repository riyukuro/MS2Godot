using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The massive event pane (UIMassiveEvent, uimassiveevent.cpp) run by its movie: each MassiveEvent kind's panel members,
/// the step countdown's digits and end, the stage, the sounds and the chat line of message type 6 (needs MS2_DATA).
/// </summary>
public class MassiveEventTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    sealed class Rig
    {
        public MassiveEventController C;
        public UiHost H;
        public Runtime RT;
        public uint Now = 100_000;
        public readonly List<string> Sounds = new();
        public readonly List<(string Text, int Kind)> Chat = new();
        public readonly List<MassiveEventMessage> Unapplied = new();
    }

    static Rig Load()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        if (packs == null)
        {
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var g = new Rig();
        g.C = new MassiveEventController
        {
            NowMs = () => g.Now,
            SystemSound = g.Sounds.Add,
            ChatLine = (t, k) => g.Chat.Add((t, k)),
            Text = t => t.Text,
            Unapplied = g.Unapplied.Add,
        };
        var (rt, p) = src.Open(g.C.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        g.RT = rt;
        g.H = new UiHost(p, g.C, new UiModel(tables));
        Assert.That(g.H.Load(), Is.True, g.C.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        g.C.Layout(1920, 1080);
        return g;
    }

    static int Int(Rig g, object o, string m) => (int)Rt.ToNumber(g.H.Get(o, m));
    static object Number(Rig g) => g.H.Child("m_number", g.H.Child("time", g.H.Child("m_counter")));

    [Test]
    public void TheMovieLoadsWithItsCounter()
    {
        var g = Load();
        Assert.That(g.C.Loaded, Is.True);
        Assert.That(g.H.Child("m_counter"), Is.Not.Null, "OnLoadComplete finds m_counter");
        Assert.That(Number(g), Is.Not.Null, "and its time clip's m_number");
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void ATypedMessageSetsThePanelAndPlaysItsSounds()
    {
        var g = Load();
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.TypedKey) { MessageType = 2, Key = "plain", Duration = 5000 });
        Assert.That((Int(g, g.H.Entity, "m_subType"), Int(g, g.H.Entity, "m_duration")), Is.EqualTo((2, 5000)));
        Assert.That(g.Sounds, Is.EqualTo(new[] { MassiveEventController.PcWinSound, MassiveEventController.SuccessSound, MassiveEventController.BattleWinSound }));
        Assert.That(g.Chat, Is.Empty, "only type 6 writes the chat line");

        g.Sounds.Clear();
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.Lose) { Message = new StringIdText(false, 0, 0, Array.Empty<string>(), "x"), Duration = 3000 });
        Assert.That(Int(g, g.H.Entity, "m_subType"), Is.EqualTo(1));
        Assert.That(g.Sounds, Is.EqualTo(new[] { MassiveEventController.FailSound }));
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void TypeSixAlsoWritesTheSystemEventChatLine()
    {
        var g = Load();
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.Text) { Message = new StringIdText(false, 0, 0, Array.Empty<string>(), "a\\nb"), Duration = 3000 });
        Assert.That(Int(g, g.H.Entity, "m_subType"), Is.EqualTo(6));
        Assert.That(g.Chat, Is.EqualTo(new[] { ("a\nb", TalkKind.SystemEvent) }), "the \\n made a line break, kind 0x13");
        Assert.That(g.Sounds, Is.Empty);
    }

    [Test]
    public void AStepMessageCountsDownItsDigitsAndEnds()
    {
        var g = Load();
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.StepKey) { Key = "plain", Step = 2, Duration = 3, SoundType = 1 });
        Assert.That((Int(g, g.H.Entity, "m_subType"), Int(g, g.H.Entity, "m_stepNum")), Is.EqualTo((8, 2)));
        Assert.That(g.C.Remaining, Is.EqualTo(3));
        Assert.That(Int(g, Number(g), "currentFrame"), Is.EqualTo(7), "second 3 shows frame 10 - 3");
        Assert.That(g.Sounds, Is.EqualTo(new[] { "Count03", MassiveEventController.CountEffectSound }));

        g.Sounds.Clear();
        g.Now += 1500; g.C.Tick();    // 1.5 s left: second 2
        Assert.That(g.C.Remaining, Is.EqualTo(2));
        Assert.That(Int(g, Number(g), "currentFrame"), Is.EqualTo(8));
        Assert.That(g.Sounds, Is.EqualTo(new[] { "Count02", MassiveEventController.CountEffectSound }));

        g.Sounds.Clear();
        g.Now += 100; g.C.Tick();     // the same second: nothing new
        Assert.That(g.Sounds, Is.Empty);

        g.Now += 1400; g.C.Tick();    // the deadline: endCount
        Assert.That(g.C.Remaining, Is.Zero);
        g.Now += 5000; g.C.Tick();
        Assert.That(g.Sounds, Is.Empty, "no digit after the end");
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void ThePvpCountdownUsesOnlyTheEffectSound()
    {
        var g = Load();
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.PvpCountdown) { Duration = 2 });
        Assert.That((Int(g, g.H.Entity, "m_subType"), g.C.Remaining), Is.EqualTo((8, 2)));
        Assert.That(g.Sounds, Is.EqualTo(new[] { MassiveEventController.CountEffectSound }));
    }

    [Test]
    public void TheStageFollowsItsSecondValueAndTheSceneExitClearsIt()
    {
        var g = Load();
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.Stage) { StageNumber = 1, A = 5, B = 1, C = 0 });
        Assert.That(g.C.StageShown, Is.True);
        g.C.ClearStage();
        Assert.That(g.C.StageShown, Is.False);
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void ARoundWithAGoalIsReportedUnappliedAndOtherKindsDoNothing()
    {
        var g = Load();
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.Round) { RoundNumber = 1, Goal = 3000 });
        g.C.Apply(new MassiveEventMessage(MassiveEventMessage.Round) { RoundNumber = 1, Goal = 0 });
        g.C.Apply(new MassiveEventMessage(9));
        Assert.That(g.Unapplied.Select(m => m.Goal), Is.EqualTo(new[] { 3000 }));
        Assert.That((g.Sounds.Count, g.Chat.Count, g.C.Remaining), Is.EqualTo((0, 0, 0)));
    }
}
