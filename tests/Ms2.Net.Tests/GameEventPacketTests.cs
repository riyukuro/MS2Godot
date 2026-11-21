using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class GameEventPacketTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    static void Shutdown(ByteWriter w, int id, params (int Field, int Obj)[] pairs)
    {
        // GameEventManagerCli::Packet0 (gameeventmanagercli.cpp:129): ReadUnicodeString name, then the type's vfunc_4;
        // GameEvent::vfunc_4 i32 id, GameEventShutdownInteractObjectId::vfunc_4 (gameevent.cpp:4306-4317) i32 count,
        // each u32 field id, i32 object id
        w.WriteUnicodeString(GameEventMessage.ShutdownInteractObjectName);
        w.WriteInt(id);
        w.WriteInt(pairs.Length);
        foreach (var (f, o) in pairs) { w.WriteInt(f); w.WriteInt(o); }
    }

    [Test]
    public void OpcodeMatchesClientTable()
    {
        // commonfiles/opcodes.h:185 RecvOp::GameEvent = 0x00B2
        Assert.That((ushort)Inbound.GameEvent, Is.EqualTo(0x00B2));
    }

    [Test]
    public void ListReadsShutdownEvents()
    {
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            Shutdown(w, 7, (2000001, 10), (2000001, 11));
            Shutdown(w, 8, (2000002, 5));
        }));
        Assert.That(m.Kind, Is.EqualTo(0));
        Assert.That(m.Truncated, Is.False);
        Assert.That(m.Shutdowns.Count, Is.EqualTo(2));
        Assert.That(m.Shutdowns[0].Id, Is.EqualTo(7));
        Assert.That(m.Shutdowns[0].Objects, Is.EqualTo(new[] { (2000001u, 10), (2000001u, 11) }));
        Assert.That(m.Shutdowns[1].Objects, Is.EqualTo(new[] { (2000002u, 5) }));
    }

    [Test]
    public void RemovedReadsIds()
    {
        // Packet2 (:171-185): i32 count, each i32 id
        var m = GameEventMessage.Read(Reader(w => { w.WriteByte(2); w.WriteInt(2); w.WriteInt(7); w.WriteInt(9); }));
        Assert.That(m.RemovedIds, Is.EqualTo(new[] { 7, 9 }));
        Assert.That(m.Shutdowns, Is.Empty);
    }

    [Test]
    public void OtherTypeIsPassedOverToTheNextShutdown()
    {
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(3);
            w.WriteUnicodeString("StringBoard"); w.WriteInt(3); w.WriteUnicodeString("x"); w.WriteInt(0);
            Shutdown(w, 4, (2000003, 1));
            Shutdown(w, 5, (2000003, 2));
        }));
        Assert.That(m.Truncated, Is.True);
        Assert.That(m.Shutdowns.Select(e => e.Id), Is.EqualTo(new[] { 4, 5 }));
        Assert.That(m.Shutdowns[1].Objects, Is.EqualTo(new[] { (2000003u, 2) }));
    }

    [Test]
    public void ShortLookAlikeDoesNotThrow()
    {
        var m = GameEventMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(2);
            w.WriteUnicodeString("Other");
            w.WriteUnicodeString(GameEventMessage.ShutdownInteractObjectName); w.WriteInt(1);
        }));
        Assert.That(m.Truncated, Is.True);
        Assert.That(m.Shutdowns, Is.Empty);
    }
}
