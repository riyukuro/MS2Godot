using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 MeretMarket browse answers (Inbound 0x6E): every recorded packet of each mode the client handles reads whole.
/// STILL-OPEN: mode 0x65 (home page, 7 identical recordings of 955 bytes: the four products read at fixed offsets but the 4 bytes before them and the 8 after are not explained by the client's layout), and with no recording, not read: modes 0x0e, 0x12, 0x14, 0x17-0x1a, 0x1c, 0x1f, 0x22, 0x6e, 0x72 and error 0x00; mode 201 has no client handler.</summary>
public class Gms2MeretMarketBrowseTests {
    [TestCase(0x66)] [TestCase(0x67)] [TestCase(0x0b)] [TestCase(0x0c)] [TestCase(0x0d)]
    [TestCase(0x15)] [TestCase(0x16)] [TestCase(0x1b)] [TestCase(0x6b)]
    public void EveryRecordedAnswerOfTheModeReadsWhole(int mode) {
        var all = Gms2Sniffs.Inbound("gms2", 0x6E).Where(p => p.Data[0] == mode).ToList();
        Assert.That(all, Is.Not.Empty, $"mode 0x{mode:x2}");
        foreach (var (file, d) in all) {
            var r = new ByteReader(d);
            var m = MeretMarketBrowse.Read(r, ProtocolProfile.Gms2);
            Assert.That(m.Known, Is.True, file);
            Assert.That(r.Available, Is.EqualTo(0), $"{file} mode 0x{mode:x2}: bytes left");
        }
    }

    [Test]
    public void SearchConditionRoundTrips() {
        var c = new MeratSearchCond(300, 3, 0xFFF, 4, "", 0, 0, 0, 0, 0);
        var w = new ByteWriter(); c.Write(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(MeratSearchCond.Read(r), Is.EqualTo(c));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test]
    public void EveryRecordedSearchRequestRewrites() {
        var all = Gms2Sniffs.Outbound("gms2", 0x3A).Where(p => p.Data[0] == 0x1b).ToList();
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, d) in all) {
            var r = new ByteReader(d); r.ReadByte();
            var c = MeratSearchCond.Read(r);
            Assert.That(r.Available, Is.EqualTo(0), file);
            var w = new ByteWriter(); w.WriteByte(0x1b); c.Write(w);
            Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(d), file);
        }
    }
}
