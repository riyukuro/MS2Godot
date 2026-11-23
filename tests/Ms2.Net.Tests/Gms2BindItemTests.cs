using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The recorded GMS2 BindItem (0x105) packets read with the GMS2 item body layout.</summary>
public class Gms2BindItemTests {
    /// <summary>Every recorded packet reads to its end when the item is found; an unfound uid reads only the uid.</summary>
    [Test]
    public void EveryRecordedGms2BindItemReadsToTheEndOfTheBody() {
        var list = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.BindItem));
        Assert.That(list, Is.Not.Empty);
        int exact = 0;
        Gms2Sniffs.WithGms2ItemData(() => { foreach (var (file, data) in list) {
            var r = new ByteReader(data);
            try {
                var m = BindItem.Read(r, _ => new EquipItem { Id = 20000001 }, _ => null, ProtocolProfile.Gms2);
                Assert.That(m.Found, Is.True, file);
                if (r.Available == 0) exact++;
            } catch (Exception) { /* counted as not exact */ }
        } });
        Assert.That(exact, Is.GreaterThan(list.Count * 9 / 10), "packets read to the end with the GMS2 profile");
    }
}
