using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// ResponseCube mode 0x0C (the cube removed). The client's case (CSceneStateGame::OnResponseCube,
/// scenestategame.cpp:10037-10077): u8 error; error 0 is i32 (unused), i32 character id, i32 the packed cell and the keep byte;
/// an error is i32 (unused), i32 character id. Samples are GMS2 recordings (one 15-byte packet each).
/// </summary>
public class RcCarryCubeTests
{
    // gms2 recording: packet 2913, 15 bytes
    static readonly byte[] RemoveA = RcLane.Hex(
        "0C000000000000000000F1030D0000");
    // gms2 recording: packet 4530, 15 bytes
    static readonly byte[] RemoveB = RcLane.Hex(
        "0C0001D4060001D406000CFF050000");
    // gms2 recording: packet 3382, 15 bytes
    static readonly byte[] RemoveC = RcLane.Hex(
        "0C00000000000000000004FE180000");

    static ResponseCubeRemove ReadRemoved(byte[] bytes)
    {
        var r = new ByteReader(bytes);
        var v = ResponseCubeRemove.Read(r);
        Assert.That(v, Is.Not.Null);
        Assert.That(r.Available, Is.EqualTo(0));
        return v;
    }

    [Test]
    public void Gms2Mode0CReadsTheRemovedCube()
    {
        var a = ReadRemoved(RemoveA);
        Assert.That(a, Is.EqualTo(new ResponseCubeRemove(0, 0, 852_977, false)));

        var b = ReadRemoved(RemoveB);
        Assert.That(b, Is.EqualTo(new ResponseCubeRemove(0, 447_489, 392_972, false)));

        var c = ReadRemoved(RemoveC);
        Assert.That(c, Is.EqualTo(new ResponseCubeRemove(0, 0, 1_637_892, false)));
    }

    [Test]
    public void Gms2Mode0CErrorReadsTheTwoInts()
    {
        var e = ReadRemoved(RcLane.Bytes("0C03050000002A000000"));
        Assert.That(e, Is.EqualTo(new ResponseCubeRemove(3, 42)));
    }
}
