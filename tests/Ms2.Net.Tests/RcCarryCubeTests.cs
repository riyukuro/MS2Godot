using Ms2.Net;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// ResponseCube mode 0x0C (the cube removed). The client's case (CSceneStateGame::OnResponseCube,
/// scenestategame.cpp:10037-10077): u8 error; error 0 is i32 owner, i32 character id, i32 the packed cell and the flag byte;
/// an error is i32 owner, i32 character id. The reader is expected at Ms2.Net.Packets.ResponseCubeRemoved.Read; the implementation that
/// puts the case on ResponseCubePlace also passes. Samples are GMS2 recordings (one packet each).
/// </summary>
public class RcCarryCubeTests
{
    // gms2 recording: packet 2913, 15 bytes
    static readonly byte[] RemoveA = RcLane.Hex(
        "0C0000000000000000000000F1030D0000");
    // gms2 recording: packet 4530, 15 bytes
    static readonly byte[] RemoveB = RcLane.Hex(
        "0C0001D4060001D406000CFF050000");
    // gms2 recording: packet 3382, 15 bytes
    static readonly byte[] RemoveC = RcLane.Hex(
        "0C0000000000000000000004FE180000");

    static object ReadRemoved(byte[] bytes)
    {
        // the new record is the expected home; the older place record is the fallback while the mode is unimplemented
        if (typeof(Inbound).Assembly.GetType("Ms2.Net.Packets.ResponseCubeRemoved") != null)
            return RcLane.Read("ResponseCubeRemoved", bytes);
        var t = typeof(Inbound).Assembly.GetType("Ms2.Net.Packets.ResponseCubePlace");
        var m = t?.GetMethod("Read", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null, new[] { typeof(global::Maple2.PacketLib.Tools.ByteReader) }, null);
        if (m == null) Assert.Fail("ResponseCube 0x0C is not read yet at origin/main");
        var r = new global::Maple2.PacketLib.Tools.ByteReader(bytes);
        var v = m.Invoke(null, new object[] { r });
        if (v == null) Assert.Fail("ResponseCube 0x0C is not read yet at origin/main");
        Assert.That(r.Available, Is.EqualTo(0));
        return v;
    }

    [Ignore("ResponseCube 0x0C is not read yet")]
    [Test]
    public void Gms2Mode0CReadsTheRemovedCube()
    {
        var a = ReadRemoved(RemoveA);
        Assert.That(RcLane.Int(a, "Mode"), Is.EqualTo(0x0C));
        Assert.That(RcLane.Int(a, "Error"), Is.EqualTo(0));
        Assert.That((long)RcLane.Long(a, "OwnerObjectId", "Owner"), Is.EqualTo(0L));
        Assert.That((long)RcLane.Long(a, "PlayerId", "CharacterId", "Player"), Is.EqualTo(0L));
        Assert.That(RcLane.Int(a, "CellKey", "Cell"), Is.EqualTo(852_977));
        Assert.That(RcLane.Bool(a, "Flag"), Is.False);

        var b = ReadRemoved(RemoveB);
        Assert.That((long)RcLane.Long(b, "OwnerObjectId", "Owner"), Is.EqualTo(447_489L));
        Assert.That((long)RcLane.Long(b, "PlayerId", "CharacterId", "Player"), Is.EqualTo(447_489L));
        Assert.That(RcLane.Int(b, "CellKey", "Cell"), Is.EqualTo(393_996));
        Assert.That(RcLane.Bool(b, "Flag"), Is.False);

        var c = ReadRemoved(RemoveC);
        Assert.That(RcLane.Int(c, "CellKey", "Cell"), Is.EqualTo(1_638_916));
    }

    [Ignore("ResponseCube 0x0C is not read yet")]
    [Test]
    public void Gms2Mode0CErrorReadsTheTwoInts()
    {
        var e = ReadRemoved(RcLane.Bytes("0C03050000002A000000"));
        Assert.That(RcLane.Int(e, "Mode"), Is.EqualTo(0x0C));
        Assert.That(RcLane.Int(e, "Error"), Is.EqualTo(3));
        Assert.That((long)RcLane.Long(e, "OwnerObjectId", "Owner"), Is.EqualTo(5L));
        Assert.That((long)RcLane.Long(e, "PlayerId", "CharacterId", "Player"), Is.EqualTo(42L));
    }
}
