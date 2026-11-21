using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>CSceneStateGame::OnReactor (@0x14173c870, scenestategame.cpp:6699-6770) and CReactor::SendStateReact.</summary>
public class ReactorPacketTests
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
        // commonfiles/opcodes.h:276 RecvOp::Reactor = 0x0115, :472 SendOp::StateReact = 0x00A9
        Assert.That((ushort)Inbound.Reactor, Is.EqualTo(0x0115));
        Assert.That((ushort)Outbound.StateReact, Is.EqualTo(0x00A9));
    }

    static void State(ByteWriter w, int idx, long v150, long[] players, int v158)
    {
        w.WriteInt(idx); w.WriteLong(v150); w.WriteByte((byte)players.Length);
        foreach (var p in players) w.WriteLong(p);
        w.WriteInt(v158);
    }

    [Test]
    public void Mode0ReadsTheAddInTheClientsOrder()
    {
        // FUN_140b79490: i32 object id, ReadString name, i32 reactor id, vec3 pos, vec3 rot, then the state block
        var m = ReactorMessage.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(42); w.WriteString("pot_1"); w.WriteInt(10000000);
            w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90);
            State(w, 2, 77, new[] { 5L, 6L, 5L }, -1);
        }));
        var a = m.Add;
        Assert.That((m.Mode, a.ObjectId, a.Name, a.ReactorId), Is.EqualTo(((byte)0, 42, "pot_1", 10000000)));
        Assert.That((a.Position, a.Rotation), Is.EqualTo((new Vector3(1, 2, 3), new Vector3(0, 0, 90))));
        Assert.That((a.State.StateIndex, a.State.Value150, a.State.Value158), Is.EqualTo((2, 77L, 0xFFFFFFFFu)));
        Assert.That(a.State.Players, Is.EqualTo(new[] { 5L, 6L }));   // m_setPlayers keeps a repeated id once
    }

    [Test]
    public void Mode1IsARemoveById()
    {
        var m = ReactorMessage.Read(Reader(w => { w.WriteByte(1); w.WriteInt(42); }));
        Assert.That((m.Mode, m.ObjectId, m.Add, m.State), Is.EqualTo(((byte)1, 42, (ReactorAdd)null, (ReactorState)null)));
    }

    [Test]
    public void Mode3IsAStateUpdateOfAnObject()
    {
        var m = ReactorMessage.Read(Reader(w => { w.WriteByte(3); w.WriteInt(42); State(w, 1, 0, new[] { 9L }, 3); }));
        Assert.That((m.ObjectId, m.State.StateIndex, m.State.Value158), Is.EqualTo((42, 1, 3u)));
        Assert.That(m.State.Players, Is.EqualTo(new[] { 9L }));
    }

    [Test]
    public void Mode4ReadsItemsWithAnUnsignedGrade()
    {
        // count, then (i32 item id, u16) pairs; the u16 goes to FUN_141abf6e0's grade
        var m = ReactorMessage.Read(Reader(w =>
        {
            w.WriteByte(4); w.WriteInt(2);
            w.WriteInt(30001097); w.WriteShort(3);
            w.WriteInt(11); w.WriteShort(unchecked((short)0xFFFF));
        }));
        Assert.That(m.Rewards, Is.EqualTo(new[] { (30001097, 3), (11, 0xFFFF) }));
    }

    [Test]
    public void Mode5IsAReactorIdAndOtherModesReadNothing()
    {
        Assert.That(ReactorMessage.Read(Reader(w => { w.WriteByte(5); w.WriteInt(10000000); })).ReactorId, Is.EqualTo(10000000));
        var m = ReactorMessage.Read(Reader(w => { w.WriteByte(2); w.WriteInt(1); }));
        Assert.That((m.Mode, m.Add, m.State, m.Rewards, m.ObjectId), Is.EqualTo(((byte)2, (ReactorAdd)null, (ReactorState)null, (IReadOnlyList<(int, int)>)null, 0)));
    }

    [Test]
    public void StateReactWritesModeTwoAndTheObjectId()
    {
        var w = new ByteWriter();
        var req = new StateReactRequest(0x01020304);
        req.Write(w);
        Assert.That(req.Op, Is.EqualTo(Outbound.StateReact));
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 2, 4, 3, 2, 1 }));
    }
}
