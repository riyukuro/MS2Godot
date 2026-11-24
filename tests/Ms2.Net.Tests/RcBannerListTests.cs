using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>BannerList (KMS 0xB3, GMS2 0xB5): u16 count, then AdBannerData records (ReadBanners, adbannerdata.cpp:176).</summary>
public class RcBannerListTests {
    static void Banner(ByteWriter w, int type, string name, string url, int slot, long begin, long end) {
        w.WriteInt(type);
        w.WriteUnicodeString("cash");
        w.WriteUnicodeString(name);
        w.WriteUnicodeString("");
        w.WriteUnicodeString("");
        w.WriteUnicodeString(url);
        w.WriteInt(slot);
        w.WriteLong(begin);
        w.WriteLong(end);
    }

    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test] public void ReadsEveryRecordOfTheList() {
        var r = R(w => {
            w.WriteShort(2);
            Banner(w, 82, "main", "http://x/a.png", 2, 1542502800, 1858122000);
            Banner(w, 821, "event", "http://x/b.png", -1, 1584545400, 1590883200);
        });
        var m = BannerList.Read(r);
        Assert.That(m.Banners, Has.Count.EqualTo(2));
        Assert.That(m.Banners[0], Is.EqualTo(new AdBannerData(82, "cash", "main", "", "", "http://x/a.png", 2, 1542502800, 1858122000)));
        Assert.That(m.Banners[1].Slot, Is.EqualTo(-1));
        Assert.That(m.Banners[1].End, Is.EqualTo(1590883200));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void EmptyListIsValid() {
        Assert.That(BannerList.Read(R(w => w.WriteShort(0))).Banners, Is.Empty);
    }

    [Test] public void ImpossibleCountIsRefused() {
        Assert.Throws<InvalidDataException>(() => BannerList.Read(R(w => { w.WriteShort(500); Banner(w, 1, "a", "b", 0, 0, 0); })));
    }

    [Test] public void TrailingBytesAreRefused() {
        Assert.Throws<PacketLayoutException>(() => BannerList.Read(R(w => { w.WriteShort(1); Banner(w, 1, "a", "b", 0, 0, 0); w.WriteByte(0); })));
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.BannerList));
        Assert.That(all, Is.Not.Empty);
        var counts = new HashSet<int>();
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var m = BannerList.Read(r);
            counts.Add(m.Banners.Count);
            Assert.That(m.Banners, Is.Not.Empty, file);
            Assert.That(r.Available, Is.EqualTo(0), file);
            foreach (var b in m.Banners) {
                Assert.That(b.Type, Is.InRange(1, 100000), file);
                Assert.That(b.End, Is.GreaterThanOrEqualTo(b.Begin), file);
            }
        }
        Assert.That(counts, Is.EquivalentTo(new[] { 13, 14, 25 }));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.BannerList), Is.EqualTo((ushort) 0xB3));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.BannerList), Is.EqualTo((ushort) 0xB5));
    }
}
