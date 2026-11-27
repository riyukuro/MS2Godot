using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The DungeonMatch packet (OnDungeonMatch @0x14173e4b0, scenestategame.cpp:5184): mode 0 is the error of OnDungeonMatchError.</summary>
public class DungeonMatchPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test]
    public void TheOpIsTheClientsRecvOp() => Assert.That((ushort)Inbound.DungeonMatch, Is.EqualTo(0xEF));

    [Test]
    public void ModeZeroReadsTheErrorTheParameterAndTheName() {
        var m = DungeonMatchError.Read(R(w => { w.WriteByte(0); w.WriteByte(9); w.WriteInt(-3); w.WriteUnicodeString("Bob"); }));
        Assert.That(m, Is.EqualTo(new DungeonMatchError(9, -3, "Bob")));
    }

    [Test]
    public void AnotherModeReadsNoMoreAndGivesNothing() =>
        Assert.That(DungeonMatchError.Read(R(w => { w.WriteByte(1); w.WriteByte(9); w.WriteInt(1); })), Is.Null);

    [Test]
    public void ABodyLongerThanTheErrorIsRefused() =>
        Assert.That(() => DungeonMatchError.Read(R(w => { w.WriteByte(0); w.WriteByte(1); w.WriteInt(0); w.WriteUnicodeString(""); w.WriteByte(5); })), Throws.Exception);
}
