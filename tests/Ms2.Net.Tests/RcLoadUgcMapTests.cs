using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>LoadUgcMap: hasInfo=0 passes; the 458-byte hasInfo=1 packet carries the design/blueprint lists after the home
/// and the current caller leaves 186 bytes when the account is unknown. Layouts: the GMS2 packet layout notes §B9.</summary>
public class RcLoadUgcMapTests {
    // gms2 recording: packet 2251, 9 bytes
    static readonly byte[] NoInfo = RcPkt.Hex(
        "000000000000000000");
    // gms2 recording: packet 3915, 458 bytes
    static readonly byte[] HasInfo = RcPkt.Hex(
        "410B0000000000000111111111111111110D002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A0000",
        "00000000630000000100000097841E0000190F0204000900000000000000000005040000000A002A002A002A002A002A",
        "002A002A002A002A002A005B6F815E00000000020000000D002A002A002A002A002A002A002A002A002A002A002A002A",
        "002A00361AA55C00000000050000000B002A002A002A002A002A002A002A002A002A002A002A00287D2C5E0000000003",
        "0000000C002A002A002A002A002A002A002A002A002A002A002A002A001D7B125E00000000010000000B002A002A002A",
        "002A002A002A002A002A002A002A002A00DDC59F5D0000000000");

    [Test] public void HasInfoZeroReadsTheMapOnly() {
        var r = new ByteReader(NoInfo);
        var m = LoadUgcMap.Read(r, 0);
        Assert.That((m.Mode, m.MapId), Is.EqualTo((0, 0)));
        Assert.That(m.Home, Is.Null);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Ignore("GMS2 design lists not read yet")]
    [Test] public void HasInfoOneReadsTheDesignListsEvenForAnUnknownAccount() {
        var r = new ByteReader(HasInfo);
        var m = LoadUgcMap.Read(r, 0L);
        Assert.That((m.Mode, m.MapId), Is.EqualTo((2881, 0)));
        var h = m.Home;
        Assert.That(h, Is.Not.Null);
        Assert.That(h.Owner, Is.EqualTo(0x1111111111111111L), "the recorded account id is masked");
        Assert.That(h.Name, Has.Length.EqualTo(13));
        Assert.That(h.Message, Has.Length.EqualTo(96));
        Assert.That(h.HeadByte, Is.EqualTo((byte) 0));
        Assert.That((h.CurrentArchitectScore, h.ArchitectScore), Is.EqualTo((0, 99)));
        Assert.That((h.PlotMapId, h.PlotNumber), Is.EqualTo((1, 2000023)));
        Assert.That((h.PlotMode, h.Area, h.Height), Is.EqualTo(((byte) 0, (byte) 25, (byte) 15)));
        Assert.That((h.Background, h.Lighting, h.Camera), Is.EqualTo(((byte) 2, (byte) 4, (byte) 0)));
        Assert.That(h.Permissions.Count, Is.EqualTo(9));
        Assert.That(h.Designs.Count, Is.EqualTo(5));
        Assert.That(h.Blueprints, Is.Empty);
        Assert.That((h.Designs[0].Id, h.Designs[0].Name.Length, h.Designs[0].Time), Is.EqualTo((4, 10, 1585540955L)));
        Assert.That(r.Available, Is.EqualTo(0), "the 186-byte tail (5 designs, 0 blueprints) must be read");
    }
}
