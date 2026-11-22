using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Trigger request (0x2F) the guide and a cut scene's end send: mode 9 on KMS2, 8 on GMS2.</summary>
public class Gms2TriggerRequestTests {
    static byte[] Bytes(IProfileOutPacket p, ProtocolProfile profile) { var w = new ByteWriter(); p.Write(w, profile); return w.ToArray(); }

    [Test] public void GuideServerTriggerEventAndCutSceneExitStartWith8OnGms2And9OnKms2() {
        Assert.That(Bytes(new GuideServerTriggerEvent(251), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 8, 1, 0xFB, 0, 0, 0 }));
        Assert.That(Bytes(new GuideServerTriggerEvent(251), ProtocolProfile.Kms2525), Is.EqualTo(new byte[] { 9, 1, 0xFB, 0, 0, 0 }));
        Assert.That(Bytes(new CutSceneExitTrigger(1), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 8, 5, 1, 0, 0, 0 }));
        Assert.That(Bytes(new CutSceneExitTrigger(1), ProtocolProfile.Kms2525), Is.EqualTo(new byte[] { 9, 5, 1, 0, 0, 0 }));
        Assert.That(Bytes(new CinematicSkipTrigger(), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 7 }));
        Assert.That(Bytes(new CinematicSkipTrigger(), ProtocolProfile.Kms2525), Is.EqualTo(new byte[] { 8 }));
    }

    [Test] public void APacketHeldDuringAReconnectIsQueuedInTheConnectionsProfileForm() {
        Assert.That(TcpTransport.HeldBody(new GuideServerTriggerEvent(251), ProtocolProfile.Gms2), Is.EqualTo(new byte[] { 8, 1, 0xFB, 0, 0, 0 }));
        Assert.That(TcpTransport.HeldBody(new GuideServerTriggerEvent(251), ProtocolProfile.Kms2525), Is.EqualTo(new byte[] { 9, 1, 0xFB, 0, 0, 0 }));
        var b = new OutPacketBuffer();
        b.PushPending(0x2F, TcpTransport.HeldBody(new CutSceneExitTrigger(7), ProtocolProfile.Gms2));
        Assert.That(b.TakePending()[0].Body, Is.EqualTo(new byte[] { 8, 5, 7, 0, 0, 0 }));
    }

    [Test] public void EveryRecordedGms2TriggerRequestIsOurLayout() {
        int total = 0, guide = 0, cut = 0, skip = 0; var odd = new List<string>();
        foreach (var (file, data) in Gms2Sniffs.Outbound("gms2", 0x2F)) {
            if (data.Length == 1 && data[0] == 7) { Assert.That(Bytes(new CinematicSkipTrigger(), ProtocolProfile.Gms2), Is.EqualTo(data)); skip++; continue; }
            if (!(data.Length == 6 && data[0] == 8 && data[1] is 1 or 5)) { odd.Add(Convert.ToHexString(data)); continue; }
            total++;
            int id = BitConverter.ToInt32(data, 2);
            IProfileOutPacket ours = data[1] == 1 ? new GuideServerTriggerEvent(id) : new CutSceneExitTrigger(id);
            Assert.That(Bytes(ours, ProtocolProfile.Gms2), Is.EqualTo(data), file);
            if (data[1] == 1) guide++; else cut++;
        }
        Assert.That(odd, Is.Empty, "Trigger requests of another layout");
        TestContext.Out.WriteLine($"{total} Trigger requests: {guide} guide, {cut} cut scene exit, {skip} cinematic skip");
        Assert.That(total + skip, Is.GreaterThan(30));
        Assert.That(guide, Is.GreaterThan(0));
    }
}
