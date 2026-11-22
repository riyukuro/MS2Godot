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
    }

    [Test] public void EveryRecordedGms2TriggerRequestIsOurLayout() {
        string root = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("MS2_SNIFFS needed");
        string dir = Path.Combine(root, "gms2-sniffs");
        if (!Directory.Exists(dir)) Assert.Ignore("MS2_SNIFFS has no gms2-sniffs folder");
        int total = 0, guide = 0, cut = 0;
        foreach (var path in Directory.GetFiles(dir, "*.msb").OrderBy(x => x))
            foreach (var p in MsbFile.Read(path).Packets) {
                if (p.ServerToClient || p.Opcode != 0x2F) continue;
                if (p.Data.Length != 6 || p.Data[0] != 8) continue; // other Trigger modes are not ours
                total++;
                int id = BitConverter.ToInt32(p.Data, 2);
                IProfileOutPacket ours = p.Data[1] == 1 ? new GuideServerTriggerEvent(id) : p.Data[1] == 5 ? new CutSceneExitTrigger(id) : null;
                Assert.That(ours, Is.Not.Null, $"{Path.GetFileName(path)}: Trigger request sub {p.Data[1]}");
                Assert.That(Bytes(ours, ProtocolProfile.Gms2), Is.EqualTo(p.Data), Path.GetFileName(path));
                if (p.Data[1] == 1) guide++; else cut++;
            }
        TestContext.Out.WriteLine($"{total} Trigger requests: {guide} guide, {cut} cut scene exit");
        Assert.That(total, Is.GreaterThan(30));
        Assert.That(guide, Is.GreaterThan(0));
    }
}
