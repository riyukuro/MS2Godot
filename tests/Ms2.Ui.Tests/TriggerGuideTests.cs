using Maple2.PacketLib.Tools;
using Ms2.Net;
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
}
