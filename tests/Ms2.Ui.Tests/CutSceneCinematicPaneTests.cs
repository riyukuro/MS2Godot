using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// UICutSceneCinematic (uicutscenecinematic.cpp) as the pane runs it, over every Cinematic type 1 to 12: what each type makes the
/// pane's movie do (the ICinematicPane calls, the same ones the controller makes on the movie), the talk timer (FUN_141908750,
/// FUN_141909880), the balloon timer (UpdateBalloons, FUN_141909eb0), the skip prompt (RequestSkip :469, SkipCinematic :482) and the
/// key mode. The samples are the recorded bodies of the tutorial maps (KMS) and of the GMS2 recordings; the layouts are the client's
/// own, so both profiles read the same way.
/// </summary>
public class CutSceneCinematicPaneTests
{
    sealed class FakePane : ICinematicPane
    {
        public readonly List<string> Calls = new();
        public bool Loaded { get; set; } = true;
        public void SetCinematic(int id, string text) => Calls.Add($"setCinematic {id} {text}");
        public void SetOpening(string text, bool isOpening) => Calls.Add($"setOpening {text} {isOpening}");
        public void SetIntro(string text) => Calls.Add($"setIntro {text}");
        public void SetScript(string name, string text, string illust, int align) => Calls.Add($"setScript {name}|{text}|{illust}|{align}");
        public void HideScript() => Calls.Add("hideScript");
        public void Close() => Calls.Add("close");
        public void SetSkip(bool on, string key, string label, bool skipVisible) => Calls.Add($"setSkip {on} {key} {label} {skipVisible}");
        public void SetSkipVisible(bool visible) => Calls.Add($"skipVisible {visible}");
        public void SetCaption(CinematicPacket.Caption caption, string text1, string text2) => Calls.Add($"setCaption {caption.Type}|{text1}|{text2}|{caption.Align}|{caption.Duration}");
    }

    sealed class Rig
    {
        public readonly CutSceneCinematic C = new();
        public readonly FakePane Pane = new();
        public long Now = 1000;
        public readonly List<string> Events = new();
        public readonly HashSet<int> Ready = new();
        public readonly List<(string Key, Action<int> Answer)> Boxes = new();

        public Rig()
        {
            C.Pane = Pane;
            C.Clock = () => Now;
            C.Translate = s => s.Replace("$", "T:");
            C.NpcName = id => $"npc{id}";
            C.PlayerName = () => "me";
            C.IllustPath = s => "ill:" + s;
            C.PlayVoice = t => Events.Add("voice " + t);
            C.StopVoice = () => Events.Add("voiceStop");
            C.SendTrigger = () => Events.Add("trigger");
            C.ShowSkipBox = (k, a) => { Events.Add("box " + k); Boxes.Add((k, a)); };
            C.BalloonActorReady = Ready.Contains;
            C.ShowBalloon = (id, t) => Events.Add($"balloon {id} {t}");
            C.HideBalloon = id => Events.Add($"balloonHide {id}");
            C.KeyModeChanged += on => Events.Add("keymode " + on);
            C.Skipped += () => Events.Add("skipped");
        }

        public void Send(Action<ByteWriter> f)
        {
            var w = new ByteWriter();
            f(w);
            C.Apply(CinematicPacket.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())));
        }
    }

    [Test]
    public void EveryTypeFrom1To12DoesWhatTheClientDoes()
    {
        var r = new Rig();
        r.Send(w => { w.WriteByte(1); w.WriteBool(true); });
        Assert.That(r.Events, Is.EqualTo(new[] { "keymode True" }), "type 1 KeyMode on");
        r.Send(w => { w.WriteByte(3); w.WriteInt(4); w.WriteUnicodeString("$title"); w.WriteUnicodeString(""); });
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setCinematic 4 T:title" }), "type 3 Start");
        r.Pane.Calls.Clear();
        r.Send(w => { w.WriteByte(4); w.WriteByte(2); w.WriteString(""); });
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setSkip True $s_cutscene_skip_key $s_cutscene_skip_state True" }), "type 4 SetSkip kind 2");
        r.Pane.Calls.Clear();
        r.Send(w => { w.WriteByte(6); w.WriteInt(11001217); w.WriteString("11001217"); w.WriteUnicodeString("hello"); w.WriteInt(2000); w.WriteByte(1); });
        r.C.Update(r.Now);
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setScript npc11001217|hello|ill:11001217|1" }), "type 6 Talk");
        r.Pane.Calls.Clear();
        r.Send(w => { w.WriteByte(8); w.WriteByte(0); w.WriteInt(694978); w.WriteUnicodeString("yo"); w.WriteInt(3000); w.WriteInt(0); });
        r.Ready.Add(694978);
        r.Now += 1;
        r.C.Update(r.Now);
        Assert.That(r.Events, Does.Contain("balloon 694978 yo"), "type 8 BalloonTalk");
        r.Send(w => { w.WriteByte(9); w.WriteInt(694978); });
        Assert.That(r.Events, Does.Contain("balloonHide 694978"), "type 9 ClearBalloonGroup");
        r.Send(w => w.WriteByte(7));
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "hideScript" }), "type 7 ClearTalks");
        r.Pane.Calls.Clear();
        r.Send(w =>
        {
            w.WriteByte(10); w.WriteUnicodeString("11"); w.WriteUnicodeString("$one"); w.WriteUnicodeString("$two$");
            w.WriteUnicodeString("6"); w.WriteInt(3500); w.WriteFloat(-0.15f); w.WriteFloat(0.15f); w.WriteFloat(2f);
        });
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setCaption 11|T:one|T:twoT:|6|3500" }), "type 10 Caption");
        Assert.That(r.Events, Does.Contain("voice two"), "the caption's second text is the voice key");
        r.Send(w => w.WriteByte(5));
        Assert.That(r.C.SkipState, Is.EqualTo(0), "type 5 ResetSkip");
        r.Pane.Calls.Clear();
        r.Send(w => { w.WriteByte(2); });
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "close" }), "type 2 Close");
        r.Pane.Calls.Clear();
        r.Send(w => { w.WriteByte(11); w.WriteUnicodeString("$open"); w.WriteBool(true); });
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setOpening T:open True" }), "type 11 Opening");
        r.Pane.Calls.Clear();
        r.Send(w => { w.WriteByte(12); w.WriteUnicodeString("intro"); });
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setIntro intro" }), "type 12 Intro");
        Assert.That(r.C.Mode, Is.EqualTo(3));
    }

    [Test]
    public void ATalkStaysForItsDurationThenTheNextLineComes()
    {
        var r = new Rig();
        void Talk(int npc, string text, int ms) => r.Send(w => { w.WriteByte(6); w.WriteInt(npc); w.WriteString(""); w.WriteUnicodeString(text); w.WriteInt(ms); w.WriteByte(0); });
        Talk(5, "one", 2000);
        Talk(0, "two", 1000);
        r.C.Update(r.Now);
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setScript npc5|one|ill:|0" }));
        r.Now += 1999; r.C.Update(r.Now);
        Assert.That(r.Pane.Calls, Has.Count.EqualTo(1), "the line stays until its duration has passed");
        r.Now += 1; r.C.Update(r.Now);
        Assert.That(r.Pane.Calls.Skip(1), Is.EqualTo(new[] { "hideScript", "setScript me|two||0" }),
            "the line ends and the next one comes in the same update; npc 0 is the player's own name, with no illustration");
        Assert.That(r.C.Talks, Is.Empty);
    }

    [Test]
    public void ATalkWithoutATextIsNotQueuedAndClearTalksEndsTheLine()
    {
        var r = new Rig();
        r.Send(w => { w.WriteByte(6); w.WriteInt(5); w.WriteString(""); w.WriteUnicodeString(""); w.WriteInt(2000); w.WriteByte(0); });
        Assert.That(r.C.Talks, Is.Empty);
        r.Send(w => { w.WriteByte(6); w.WriteInt(5); w.WriteString(""); w.WriteUnicodeString("x"); w.WriteInt(2000); w.WriteByte(0); });
        r.C.Update(r.Now);
        Assert.That(r.C.CurrentTalk, Is.Not.Null);
        r.Send(w => w.WriteByte(7));
        Assert.That(r.C.CurrentTalk, Is.Null);
        Assert.That(r.Pane.Calls.Last(), Is.EqualTo("hideScript"));
    }

    [Test]
    public void ABalloonLineStartsAfterItsDelayInAnActorThatIsThereAndEndsAtItsDuration()
    {
        var r = new Rig();
        r.Send(w => { w.WriteByte(8); w.WriteByte(0); w.WriteInt(9); w.WriteUnicodeString("a"); w.WriteInt(500); w.WriteInt(300); });
        r.Send(w => { w.WriteByte(8); w.WriteByte(0); w.WriteInt(9); w.WriteUnicodeString("b"); w.WriteInt(500); w.WriteInt(2000); });
        r.Now = 1500; r.C.Update(r.Now);
        Assert.That(r.Events, Is.Empty, "no actor, no line");
        r.Ready.Add(9);
        r.C.Update(r.Now);
        Assert.That(r.Events, Is.EqualTo(new[] { "balloon 9 a", "voice a" }), "the line shows and its sound key plays");
        r.Now = 1799; r.C.Update(r.Now);
        Assert.That(r.Events, Has.Count.EqualTo(2));
        r.Now = 1800; r.C.Update(r.Now);
        Assert.That(r.Events.Skip(2), Is.EqualTo(new[] { "balloonHide 9" }), "the line is hidden at its end");
        r.Now = 3001; r.C.Update(r.Now);
        Assert.That(r.Events, Does.Contain("balloon 9 b"), "the delayed line follows");
        r.C.RemoveGameObject(9);
        Assert.That(r.C.BalloonGroups, Is.Empty);
    }

    [Test]
    public void TheSkipPromptSendsTheTriggerOnlyForTheStatesThatHaveOne()
    {
        var r = new Rig();
        r.C.RequestSkip();
        Assert.That(r.Events, Is.Empty, "no prompt, nothing");
        r.Send(w => { w.WriteByte(4); w.WriteByte(1); w.WriteString(""); });
        Assert.That(r.Pane.Calls.Last(), Is.EqualTo("setSkip True $s_cutscene_skip_key $s_cutscene_skip_scene True"), "kind 1 is the scene label");
        r.C.RequestSkip();
        Assert.That(r.Events, Is.EqualTo(new[] { "trigger", "skipped" }));
        Assert.That(r.Pane.Calls.Last(), Is.EqualTo("skipVisible False"));
        r.Events.Clear();
        r.C.RequestSkip();
        Assert.That(r.Events, Is.Empty, "skipped once");
    }

    [Test]
    public void ASkipMessageAsksFirstAndOnlyYesSkips()
    {
        var r = new Rig();
        r.Send(w => { w.WriteByte(4); w.WriteByte(2); w.WriteString("tutorial"); });
        r.C.RequestSkip();
        Assert.That(r.Boxes.Single().Key, Is.EqualTo("s_cutscene_skip_msg_tutorial"));
        Assert.That(r.Events, Is.EqualTo(new[] { "box s_cutscene_skip_msg_tutorial" }));
        r.Boxes[0].Answer(1);
        Assert.That(r.Events, Has.Count.EqualTo(1), "no answer skips nothing");
        r.Boxes[0].Answer(0);
        Assert.That(r.Events.Skip(1), Is.EqualTo(new[] { "trigger", "skipped" }));
    }

    [Test]
    public void OpeningAndIntroTurnTheKeyModeOnAndShiftEscEndsTheCinematic()
    {
        var r = new Rig();
        r.Send(w => { w.WriteByte(11); w.WriteUnicodeString("open"); w.WriteBool(false); });
        Assert.That(r.C.KeyMode, Is.True);
        r.Send(w => { w.WriteByte(12); w.WriteUnicodeString("intro"); });
        Assert.That(r.Events.Count(e => e == "keymode True"), Is.EqualTo(1), "an intro with the key group on leaves it on");
        r.Send(w => { w.WriteByte(4); w.WriteByte(2); w.WriteString(""); });
        r.Events.Clear();
        r.C.ForceEnd();
        Assert.That(r.Events, Is.EqualTo(new[] { "trigger", "keymode False" }));
        Assert.That(r.Pane.Calls.Last(), Is.EqualTo("close"));
    }

    [Test]
    public void ACaptionNeedsTheMoviesEntityAndTheOpenedPaneReplaysTheModeInForce()
    {
        var r = new Rig();
        r.Pane.Loaded = false;
        r.Send(w =>
        {
            w.WriteByte(10); w.WriteUnicodeString("1"); w.WriteUnicodeString("a"); w.WriteUnicodeString("");
            w.WriteUnicodeString("1"); w.WriteInt(1); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(1);
        });
        Assert.That(r.Pane.Calls, Is.Empty, "OnPacketSetCaption returns without the entity");
        r.Send(w => { w.WriteByte(11); w.WriteUnicodeString("open"); w.WriteBool(true); });
        r.Send(w => { w.WriteByte(4); w.WriteByte(1); w.WriteString(""); });
        r.Pane.Calls.Clear();
        r.C.Replay();
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setOpening open True", "setSkip True $s_cutscene_skip_key $s_cutscene_skip_scene True" }));
    }
}
