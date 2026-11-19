using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Synthetic Buff (0x47) packets laid out as the Maple2 server's BuffPacket writes them (an int BuffFlag).</summary>
public class BuffPacketTests {
    static BuffPacket Read(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return BuffPacket.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())); }
    static void Header(ByteWriter w, byte mode) { w.WriteByte(mode); w.WriteInt(7); w.WriteInt(900); w.WriteInt(8); }
    static void State(ByteWriter w) { w.WriteInt(1000); w.WriteInt(61000); w.WriteInt(10500111); w.WriteShort(1); w.WriteInt(3); w.WriteBool(true); }

    [Test] public void AddRemoveModify() {
        var add = Read(w => { Header(w, 0); State(w); w.WriteLong(55); });
        Assert.That(add, Is.EqualTo(new BuffPacket(BuffPacket.Add, 7, 900, 8, 0, new BuffState(1000, 61000, 10500111, 1, 3, 1), 55)));
        var remove = Read(w => Header(w, 1));
        Assert.That((remove.Mode, remove.Key, remove.State, remove.Shield), Is.EqualTo((BuffPacket.Remove, 900, (BuffState?)null, (long?)null)));
        var modState = Read(w => { Header(w, 2); w.WriteInt(1); State(w); });
        Assert.That((modState.Flags, modState.State?.OverlapCount, modState.Shield), Is.EqualTo((1u, (int?)3, (long?)null)));
        var modShield = Read(w => { Header(w, 2); w.WriteInt(2); w.WriteLong(12); });
        Assert.That((modShield.State, modShield.Shield), Is.EqualTo(((BuffState?)null, (long?)12)));
    }

    [Test] public void FieldAddUserBuffIsAnAdd() {
        var b = BuffPacket.FromAdd(new Buff(7, 900, 8, 1000, 61000, 10500111, 1, 3, 1, 55));
        Assert.That(b, Is.EqualTo(new BuffPacket(BuffPacket.Add, 7, 900, 8, 0, new BuffState(1000, 61000, 10500111, 1, 3, 1), 55)));
    }
}
