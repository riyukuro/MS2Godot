using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The GMS2 create answer: the client has no separate name check. The create request is answered by Inbound 0x47 (u8 kind, unicode message), kind 0xB being "name already taken" (CreateRefusalText).</summary>
public class Gms2CreateAnswerTests {
    [Test]
    public void Gms2MapsTheCreateAnswerToTheRecordedOpcode() {
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.CharacterCreate), Is.EqualTo(0x47));
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.CharacterCreate), Is.EqualTo(0x46));
    }

    [Test]
    public void NameTakenAnswerWithEmptyMessageReads() {
        var a = CharacterCreateAnswer.Read(new ByteReader(new byte[] { 0x0B, 0x00, 0x00 }));
        Assert.That(a.Kind, Is.EqualTo((byte) 0x0B));
        Assert.That(a.Message, Is.EqualTo(""));
    }

    [Test]
    public void EveryRecordedGms2CreateAnswerReadsWhole() {
        var all = Gms2Sniffs.Inbound("gms2", 0x47);
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, data) in all) {
            CharacterCreateAnswer a = null;
            Assert.DoesNotThrow(() => a = CharacterCreateAnswer.Read(new ByteReader(data)), file + " " + Convert.ToHexString(data));
            Assert.That(a.Kind, Is.AnyOf((byte) 0x0B), file + ": only the name-taken kind is recorded so far");
        }
    }
}
