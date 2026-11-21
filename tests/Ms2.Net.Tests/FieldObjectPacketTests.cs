using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class FieldObjectPacketTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:87 RecvOp::Breakable = 0x004F, :123 Liftable = 0x0073, :203 Fishing = 0x00C4
        Assert.That((ushort)Inbound.Breakable, Is.EqualTo(0x004F));
        Assert.That((ushort)Inbound.Liftable, Is.EqualTo(0x0073));
        Assert.That((ushort)Inbound.Fishing, Is.EqualTo(0x00C4));
    }

    static void Breakable(ByteWriter w, string name, byte state, byte a, int b, int c)
    {
        w.WriteString(name); w.WriteByte(state); w.WriteByte(a); w.WriteInt(b); w.WriteInt(c);
    }

    [Test]
    public void BreakableMode0ReadsTheCountedEntries()
    {
        // OnBreakable (scenestategame.cpp:3667-3694): u8 mode 0, i32 count, each ReadString, u8, u8, i32, i32
        var u = BreakableUpdate.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            Breakable(w, "a1", 2, 1, 300, -4);
            Breakable(w, "b2", 3, 0, 0, 7);
        }));
        Assert.That(u.Entries, Is.EqualTo(new[] { new BreakableEntry("a1", 2, 1, 300, -4), new BreakableEntry("b2", 3, 0, 0, 7) }));
    }

    [Test]
    public void BreakableMode1IsOneEntryWithoutACount()
    {
        var u = BreakableUpdate.Read(Reader(w => { w.WriteByte(1); Breakable(w, "x", 5, 2, 9, 10); }));
        Assert.That(u.Entries, Is.EqualTo(new[] { new BreakableEntry("x", 5, 2, 9, 10) }));
    }

    [Test]
    public void BreakableTrailingBytesStillRead()
    {
        // OnBreakable never checks the end: a trailing byte leaves the entries as read
        var u = BreakableUpdate.Read(Reader(w => { w.WriteByte(1); Breakable(w, "x", 5, 2, 9, 10); w.WriteByte(0xEE); }));
        Assert.That(u.Entries, Is.EqualTo(new[] { new BreakableEntry("x", 5, 2, 9, 10) }));
    }

    [Test]
    public void LiftableTrailingBytesStillRead()
    {
        // OnLiftable never checks the end either
        var u = LiftableUpdate.Read(Reader(w => { w.WriteByte(2); w.WriteString("l"); w.WriteByte(0); w.WriteInt(3); w.WriteByte(1); w.WriteByte(0xEE); }));
        Assert.That(u.Entries, Is.EqualTo(new[] { new LiftableEntry("l", false, 3, 1) }));
        var gone = LiftableUpdate.Read(Reader(w => { w.WriteByte(4); w.WriteString("gone"); w.WriteByte(0xEE); }));
        Assert.That(gone.Name, Is.EqualTo("gone"));
    }

    [Test]
    public void BreakableOtherModeReadsNothingMore()
    {
        // :3670-3674: nCount stays 0 for another mode, the rest is left unread
        var u = BreakableUpdate.Read(Reader(w => { w.WriteByte(2); w.WriteInt(99); }));
        Assert.That(u.Mode, Is.EqualTo(2));
        Assert.That(u.Entries, Is.Empty);
    }

    [Test]
    public void LiftableMode0ReadsKindAsInt32AndStateAsByte()
    {
        // OnLiftable case 0 (:11946-11972): ReadString, u8 enabled, i32 (SetKind), u8 (SetState), ReadUnicodeStringA x4, bool
        var u = LiftableUpdate.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(1);
            w.WriteString("lift1"); w.WriteByte(1); w.WriteInt(70000); w.WriteByte(2);
            w.WriteUnicodeString("a"); w.WriteUnicodeString("b"); w.WriteUnicodeString("c"); w.WriteUnicodeString("d");
            w.WriteBool(true);
        }));
        Assert.That(u.Entries, Is.EqualTo(new[] { new LiftableEntry("lift1", true, 70000, 2, "a", "b", "c", "d", true) }));
    }

    [Test]
    public void LiftableMode2ReadsNameEnabledKindState()
    {
        // case 2 (:11994-12003)
        var u = LiftableUpdate.Read(Reader(w => { w.WriteByte(2); w.WriteString("l"); w.WriteByte(0); w.WriteInt(3); w.WriteByte(1); }));
        Assert.That(u.Entries, Is.EqualTo(new[] { new LiftableEntry("l", false, 3, 1) }));
    }

    [Test]
    public void LiftableMode3IsANewLiftWithoutEnabledOrState()
    {
        // case 3 -> WorldService::FUN_141bc5420 (worldservice_1.cpp:4429-4447): slot 7 ReadString, i32, slot 6 x4, bool
        var u = LiftableUpdate.Read(Reader(w =>
        {
            w.WriteByte(3); w.WriteString("new"); w.WriteInt(4);
            w.WriteUnicodeString("q1"); w.WriteUnicodeString("q2"); w.WriteUnicodeString("q3"); w.WriteUnicodeString("q4");
            w.WriteBool(false);
        }));
        Assert.That(u.Entries, Is.EqualTo(new[] { new LiftableEntry("new", false, 4, 0, "q1", "q2", "q3", "q4", false) }));
    }

    [Test]
    public void LiftableMode4ReadsTheNameToRemove()
    {
        var u = LiftableUpdate.Read(Reader(w => { w.WriteByte(4); w.WriteString("gone"); }));
        Assert.That(u.Name, Is.EqualTo("gone"));
        Assert.That(u.Entries, Is.Empty);
    }

    [Test]
    public void LiftableModeWithoutACaseReadsNothingMore()
    {
        var u = LiftableUpdate.Read(Reader(w => { w.WriteByte(1); w.WriteString("x"); }));
        Assert.That(u.Mode, Is.EqualTo(1));
        Assert.That(u.Entries, Is.Empty);
        Assert.That(u.Name, Is.Null);
    }
}
