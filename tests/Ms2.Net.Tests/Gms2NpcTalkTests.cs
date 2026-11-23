using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The recorded GMS2 NpcTalk (0x4C) reward items (mode 3, sub 5) carry item bodies in the GMS2 layout.</summary>
public class Gms2NpcTalkTests {
    [Test]
    public void EveryRecordedGms2RewardItemsPacketReadsToTheEnd() {
        var list = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.NpcTalk));
        int n = 0;
        Gms2Sniffs.WithGms2ItemData(() => { foreach (var (file, data) in list) {
            if (data.Length < 2 || data[0] != 3 || data[1] != 5) continue;
            var r = new ByteReader(data);
            Assert.DoesNotThrow(() => NpcTalkMessage.Read(r, ProtocolProfile.Gms2), file);
            Assert.That(r.Available, Is.EqualTo(0), file);
            n++;
        } });
        Assert.That(n, Is.GreaterThan(0), "recorded reward items packets");
    }

    [Test]
    public void KmsReaderStillReadsKmsRecordings() {
        foreach (var (file, data) in Gms2Sniffs.Inbound("kms2", ProtocolProfile.Kms2525.Number(Inbound.NpcTalk)))
            Assert.DoesNotThrow(() => NpcTalkMessage.Read(new ByteReader(data)), file);
    }
}
