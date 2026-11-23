using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The recorded GMS2 StorageInventory (0x22) packets: modes 0 and 5 carry item records.</summary>
public class Gms2StorageInventoryTests {
    /// <summary>The store id every id-carrying mode starts with (the book's own id on a match).</summary>
    static long IdOf(byte[] d) => d.Length >= 9 && d[0] != 3 && d[0] != 0xd ? BitConverter.ToInt64(d, 1) : 0;

    [Test]
    public void EveryRecordedGms2StoragePacketReadsToTheEnd() {
        var list = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.StorageInventory));
        Assert.That(list, Is.Not.Empty);
        Gms2Sniffs.WithGms2ItemData(() => { foreach (var (file, data) in list) {
            var r = new ByteReader(data);
            StorageMessage m = null;
            Assert.DoesNotThrow(() => m = StorageMessage.Read(r, IdOf(data), ProtocolProfile.Gms2), file);
            if (m is StorageMessage.IdMismatch) continue;   // Currency and Extend carry the id differently; the client reads nothing more
            Assert.That(r.Available, Is.EqualTo(0), $"{file} mode {data[0]} len {data.Length}");
        } });
    }
}
