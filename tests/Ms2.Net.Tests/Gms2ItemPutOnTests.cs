using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class Gms2ItemPutOnTests {
    [Test]
    public void EveryRecordedGms2PutOnReadsToTheEnd() {
        var list = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.ItemPutOn));
        Assert.That(list, Is.Not.Empty);
        foreach (var (file, data) in list) {
            var p = ItemPutOn.Read(new ByteReader(data), ProtocolProfile.Gms2);
            Assert.That(p.Item.Id, Is.GreaterThan(0), file);
        }
    }

    [Test]
    public void KmsReaderStillReadsKmsRecordings() {
        foreach (var (file, data) in Gms2Sniffs.Inbound("kms2", ProtocolProfile.Kms2525.Number(Inbound.ItemPutOn)))
            Assert.DoesNotThrow(() => ItemPutOn.Read(new ByteReader(data), ProtocolProfile.Kms2525), file);
    }

    /// <summary>Every recorded GMS2 ItemPutOff (0x26) reads to the end (0x20 has no GMS2 recording).</summary>
    [Test]
    public void EveryRecordedGms2PutOffReadsToTheEnd() {
        var list = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.ItemPutOff));
        Assert.That(list, Is.Not.Empty);
        foreach (var (file, data) in list) Assert.DoesNotThrow(() => ItemPutOff.Read(new ByteReader(data)), file);
    }
}
