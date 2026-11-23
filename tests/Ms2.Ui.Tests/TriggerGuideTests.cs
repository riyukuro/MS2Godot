using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The guide part of the Trigger packet (CTriggerService::FUN_1418627c0, triggerservice.cpp:631): the summary
/// show and hide subs reach the guide event pane, on the KMS2 type 9 and the GMS2 type 8.</summary>
public class TriggerGuideTests
{
    sealed class Pane : IGuideEventPane
    {
        public readonly List<string> Calls = new();
        public bool Loaded => true;
        public void ShowTutorial(int entityId, int textId, bool first) => Calls.Add($"tutorial {entityId} {textId}");
        public void HideTutorial(int entityId) => Calls.Add($"hidetutorial {entityId}");
        public bool TutorialShowing => false;
        public void ShowSummary(int entityId, int textId, int durationMs = 0) => Calls.Add($"show {entityId} {textId} {durationMs}");
        public void HideSummary(int entityId) => Calls.Add($"hide {entityId}");
        public bool SummaryShowing => false;
        public void AddArrow(GuideArrow arrow) => Calls.Add("arrow");
        public void ReleaseArrow(int entityId) => Calls.Add("release");
        public void QueueOrShowTextTip(int entityId, int textId) => Calls.Add("tip");
        public void AdvanceTextTip(int entityId) => Calls.Add("advance");
        public int LastClosedTip => 0;
    }

    static byte[] Body(byte type, byte sub, params int[] ints)
    {
        var w = new ByteWriter();
        w.WriteByte(type);
        w.WriteByte(sub);
        foreach (int i in ints) w.WriteInt(i);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void SummaryShowAndHideReachThePane()
    {
        var pane = new Pane();
        Assert.That(TriggerGuide.Apply(Body(9, 2, 7, 20001, 5000), ProtocolProfile.Kms2525, pane), Is.True);
        Assert.That(TriggerGuide.Apply(Body(9, 3, 7), ProtocolProfile.Kms2525, pane), Is.True);
        Assert.That(TriggerGuide.Apply(Body(8, 2, 3, 20002, 0), ProtocolProfile.Gms2, pane), Is.True);
        Assert.That(TriggerGuide.Apply(Body(8, 3, 3), ProtocolProfile.Gms2, pane), Is.True);
        Assert.That(pane.Calls, Is.EqualTo(new[] { "show 7 20001 5000", "hide 7", "show 3 20002 0", "hide 3" }));
    }

    /// <summary>Sub 1 adds a ServerTriggerEvent guide trigger with the id (AddGuideTrigger, triggerservice.cpp:1006-1011);
    /// sub 0xa turns the own pc to the point's z (:1072-1083); a sub not applied here reaches the unapplied hook read whole.</summary>
    [Test]
    public void GuideTriggerTurnAndUnappliedSubs()
    {
        var g = new GuideManager(new GuideTests.World());
        var left = new List<TriggerUiCommand>();
        float? yaw = null;
        Assert.That(TriggerGuide.Apply(Body(8, 1, 260), ProtocolProfile.Gms2, null, null, g, z => yaw = z, left.Add), Is.True);
        Assert.That(g.CheckTrigger(GuideTrigger.ServerTriggerEvent, 260, null), Is.True);
        Assert.That(g.CheckTrigger(GuideTrigger.ServerTriggerEvent, 261, null), Is.False);

        var w = new ByteWriter(); w.WriteByte(9); w.WriteByte(0xa); w.WriteShort(10); w.WriteShort(20); w.WriteShort(-90); w.WriteBool(false);
        Assert.That(TriggerGuide.Apply(w.Buffer.AsSpan(0, w.Length).ToArray(), ProtocolProfile.Kms2525, null, null, g, z => yaw = z, left.Add), Is.True);
        Assert.That(yaw, Is.EqualTo(-90f));
        Assert.That(left, Is.Empty);

        w = new ByteWriter(); w.WriteByte(9); w.WriteByte(9); w.WriteInt(11); w.WriteString("hello");
        Assert.That(TriggerGuide.Apply(w.Buffer.AsSpan(0, w.Length).ToArray(), ProtocolProfile.Kms2525, null, null, g, null, left.Add), Is.True);
        Assert.That(left.Select(c => (c.Sub, c.Id, c.Text)), Is.EqualTo(new[] { ((byte)9, 11, "hello") }));
    }

    [Test]
    public void OtherTypesAndSubsAreNotTheGuides()
    {
        var pane = new Pane();
        // KMS2 type 8 has no case; GMS2 type 9 is the client's 0xa
        Assert.That(TriggerGuide.Apply(Body(8, 2, 1, 2, 3), ProtocolProfile.Kms2525, pane), Is.False);
        Assert.That(TriggerGuide.Apply(Body(9, 2, 1, 2, 3), ProtocolProfile.Gms2, pane), Is.False);
        Assert.That(TriggerGuide.Apply(Body(3, 2, 1, 2, 3), ProtocolProfile.Kms2525, pane), Is.False);
        Assert.That(TriggerGuide.Apply(System.Array.Empty<byte>(), ProtocolProfile.Kms2525, pane), Is.False);
        // a guide packet of another sub is the guide's but does not touch the summary
        Assert.That(TriggerGuide.Apply(Body(9, 1, 44), ProtocolProfile.Kms2525, pane), Is.True);
        // without the pane nothing is shown
        Assert.That(TriggerGuide.Apply(Body(9, 2, 7, 20001, 0), ProtocolProfile.Kms2525, null), Is.True);
        Assert.That(pane.Calls, Is.Empty);
    }

    static byte[] MovieStart(byte type, string path, int id)
    {
        var w = new ByteWriter();
        w.WriteByte(type);
        w.WriteByte(4);
        w.WriteString(path);
        w.WriteInt(id);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    /// <summary>Start movie (sub 4) opens the pane; the skip (Esc, UICutSceneMovie::Exit) sends the Trigger request with
    /// the client's type (9, GMS2 8) and the id; nothing is sent before the skip, and the server's sub 5 stops it.</summary>
    [TestCase(9, 9, false)]
    [TestCase(8, 8, true)]
    public void StartMovieOpensThePaneAndOnlyTheSkipSends(byte inType, byte sentType, bool gms2)
    {
        var profile = gms2 ? ProtocolProfile.Gms2 : ProtocolProfile.Kms2525;
        var sent = new List<byte[]>();
        var movie = new Ms2.Ui.Hud.CutSceneMovie(pk =>
        {
            var w = new ByteWriter();
            ((Ms2.Net.Wire.IProfileOutPacket) pk).Write(w, profile);
            sent.Add(w.Buffer.AsSpan(0, w.Length).ToArray());
        });
        Assert.That(TriggerGuide.Apply(MovieStart(inType, "cut/intro.usm", 1), profile, null, movie), Is.True);
        Assert.That(movie.IsPlaying, Is.True);
        Assert.That(movie.Path, Is.EqualTo("cut/intro.usm"));
        Assert.That(sent, Is.Empty, "no send without the skip");
        Assert.That(Ms2.Ui.Hud.FieldEsc.Run(() => false, () => false, () => false, movie.SkipOnEsc), Is.True);
        Assert.That(sent.Count, Is.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(new byte[] { sentType, 5, 1, 0, 0, 0 }));
        Assert.That(movie.IsPlaying, Is.True, "it plays on until the server's stop");
        Assert.That(TriggerGuide.Apply(Body(inType, 5, 2), profile, null, movie), Is.True);
        Assert.That(movie.IsPlaying, Is.True, "another cut scene's stop is not this one's");
        Assert.That(TriggerGuide.Apply(Body(inType, 5, 1), profile, null, movie), Is.True);
        Assert.That(movie.IsPlaying, Is.False);
        Assert.That(movie.SkipOnEsc(), Is.False, "Esc is not taken without a movie");
        Assert.That(sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void ASecondStartStopsTheFirstAndAZeroIdSkipStopsAtOnce()
    {
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        var movie = new Ms2.Ui.Hud.CutSceneMovie(sent.Add);
        TriggerGuide.Apply(MovieStart(9, "a.usm", 0), ProtocolProfile.Kms2525, null, movie);
        TriggerGuide.Apply(MovieStart(9, "b.usm", 4), ProtocolProfile.Kms2525, null, movie);
        Assert.That((movie.Path, movie.CutSceneId), Is.EqualTo(("b.usm", 4)));
        TriggerGuide.Apply(MovieStart(9, "c.usm", 0), ProtocolProfile.Kms2525, null, movie);
        movie.Exit();
        Assert.That(movie.IsPlaying, Is.False, "id 0: Exit stops without a request");
        Assert.That(sent, Is.Empty);
    }
}
