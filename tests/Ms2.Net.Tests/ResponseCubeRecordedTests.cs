using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ResponseCube (0x6B GMS2) mode 0x14 (the home's map info, GMS2 width) and mode 0x11 (a held cube placed: error,
/// pc id, cube key, item id, value). The home name is masked with '*' by the masking pass.</summary>
public class ResponseCubeRecordedTests {
    // gms2 recording: packet 2306, 52 bytes (names masked with '*' by the masking pass; account/character id bytes replaced with 0x11).
    static readonly byte[] Gms2Home = new byte[] {
        0x14, 0x7F, 0xC0, 0x06, 0x00, 0x80, 0x0B, 0xB2, 0x03, 0x92, 0xAB, 0x1E, 0x00, 0x05, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A, 0x00, 0x2A,
        0x00, 0x2A, 0x00, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
        0x11, 0x11, 0x11, 0x01,
    };
    // gms2 recording: packet 13090, 18 bytes (names masked with '*' by the masking pass).
    static readonly byte[] Gms2Held = new byte[] {
        0x11, 0x00, 0x2D, 0x88, 0x19, 0x00, 0x12, 0xFC, 0x20, 0x00, 0xC2, 0xB5, 0x15, 0x01, 0xC6, 0x09,
        0x04, 0x0B,
    };

    [Test] public void Gms2HomeInfoReadsThePlotAndHome() {
        var r = new ByteReader(Gms2Home);
        Assert.That(r.ReadByte(), Is.EqualTo(ResponseCubeHomeInfo.Mode));
        var h = ResponseCubeHomeInfo.Read(r, ProtocolProfile.Gms2);
        Assert.That((h.OwnerObjectId, h.PlotId, h.Field20, h.Field24), Is.EqualTo((442495, 62000000, 2010002, 5)));
        Assert.That(h.Field28, Has.Length.EqualTo(6));
        Assert.That((h.HomeId, h.Field38, h.Result, h.OpenType), Is.EqualTo((0x1111111111111111L, 0x1111111111111111L, (byte) 1, (byte) 0)), "the two recorded i64 ids are masked");
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void Gms2HeldCubeReadsThePlacement() {
        var r = new ByteReader(Gms2Held);
        var h = ResponseCubeHeldItem.Read(r);
        Assert.That(h, Is.Not.Null);
        Assert.That((h.Mode, h.Error, h.Id, h.CubeKey, h.ItemId, h.Value), Is.EqualTo((ResponseCubeHeldItem.ModeHold, (byte) 0, 1673261, 2161682, 18200002, 184814022)));
        Assert.That(r.Available, Is.EqualTo(0));
    }
}
