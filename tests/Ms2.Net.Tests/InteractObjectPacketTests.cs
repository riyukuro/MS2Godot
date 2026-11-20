using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class InteractObjectPacketTests
{
    static ByteReader R(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    static byte[] BytesOf(Ms2.Net.Wire.IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:107 RecvOp::InteractObject = 0x0063, :354 SendOp::InteractObject = 0x0031
        Assert.That((ushort)Inbound.InteractObject, Is.EqualTo(0x0063));
        Assert.That((ushort)Outbound.InteractObject, Is.EqualTo(0x0031));
    }

    [Test]
    public void StartAndCompleteRequestsWriteModeCountAndAsciiNames()
    {
        // CInteractObject::vfunc_8 (0x0b) / vfunc_10 (0x0c) and StateInteraction::SendInteractObjectMode11/12
        var startSingle = new InteractObjectStartRequest("11111111-2222-3333-4444-555555555555");
        Assert.That(startSingle.Op, Is.EqualTo(Outbound.InteractObject));
        var r1 = new ByteReader(BytesOf(startSingle));
        Assert.That(r1.ReadByte(), Is.EqualTo(0x0b));
        Assert.That(r1.ReadInt(), Is.EqualTo(1));
        Assert.That(r1.ReadString(), Is.EqualTo("11111111-2222-3333-4444-555555555555"));
        Assert.That(r1.Available, Is.EqualTo(0));

        var completeMulti = new InteractObjectCompleteRequest(new[] { "obj-a", "obj-b" });
        Assert.That(completeMulti.Op, Is.EqualTo(Outbound.InteractObject));
        var r2 = new ByteReader(BytesOf(completeMulti));
        Assert.That(r2.ReadByte(), Is.EqualTo(0x0c));
        Assert.That(r2.ReadInt(), Is.EqualTo(2));
        Assert.That(r2.ReadString(), Is.EqualTo("obj-a"));
        Assert.That(r2.ReadString(), Is.EqualTo("obj-b"));
        Assert.That(r2.Available, Is.EqualTo(0));
    }

    [Test]
    public void ModeStateKindStateAndAllStateReadExactWire()
    {
        var w4 = new ByteWriter();
        w4.WriteByte(InteractObjectMessage.ModeState);
        w4.WriteString("uuid-mesh");
        w4.WriteByte(1);
        w4.WriteByte(InteractObjectMessage.KindMesh);
        var m4 = InteractObjectMessage.Read(R(w4));
        Assert.That((m4.Mode, m4.Name, m4.State, m4.Kind), Is.EqualTo(((byte)4, "uuid-mesh", (byte)1, (byte)1)));

        var w6 = new ByteWriter();
        w6.WriteByte(InteractObjectMessage.ModeKindState);
        w6.WriteInt(10000001);
        w6.WriteByte(2);
        var m6 = InteractObjectMessage.Read(R(w6));
        Assert.That((m6.Mode, m6.RowId, m6.State), Is.EqualTo(((byte)6, 10000001, (byte)2)));

        var w7 = new ByteWriter();
        w7.WriteByte(InteractObjectMessage.ModeAllState);
        w7.WriteByte(0);
        var m7 = InteractObjectMessage.Read(R(w7));
        Assert.That((m7.Mode, m7.State), Is.EqualTo(((byte)7, (byte)0)));
    }

    [Test]
    public void ModeMessageReadsExtraFieldsOnlyForGathering()
    {
        var wUi = new ByteWriter();
        wUi.WriteByte(InteractObjectMessage.ModeMessage);
        wUi.WriteString("uuid-ui");
        wUi.WriteByte(InteractObjectMessage.KindUi);
        var mUi = InteractObjectMessage.Read(R(wUi));
        Assert.That((mUi.Mode, mUi.Name, mUi.Kind, mUi.GatherType, mUi.GatherCount),
            Is.EqualTo(((byte)5, "uuid-ui", (byte)3, (short)0, 0)));

        var wGather = new ByteWriter();
        wGather.WriteByte(InteractObjectMessage.ModeMessage);
        wGather.WriteString("uuid-ore");
        wGather.WriteByte(InteractObjectMessage.KindGathering);
        wGather.WriteShort(0);
        wGather.WriteInt(3);
        var mGather = InteractObjectMessage.Read(R(wGather));
        Assert.That((mGather.Mode, mGather.Name, mGather.Kind, mGather.GatherType, mGather.GatherCount),
            Is.EqualTo(((byte)5, "uuid-ore", (byte)6, (short)0, 3)));
    }

    [Test]
    public void ModeStatesReadsRespawnTickOnlyForGatheringEntries()
    {
        var w = new ByteWriter();
        w.WriteByte(InteractObjectMessage.ModeStates);
        w.WriteInt(3);
        w.WriteString("mesh-1"); w.WriteByte(1); w.WriteByte(InteractObjectMessage.KindMesh);
        w.WriteString("ore-1"); w.WriteByte(0); w.WriteByte(InteractObjectMessage.KindGathering); w.WriteInt(45000);
        w.WriteString("tel-1"); w.WriteByte(2); w.WriteByte(InteractObjectMessage.KindTelescope);
        var m = InteractObjectMessage.Read(R(w));
        Assert.That(m.Entries, Has.Count.EqualTo(3));
        Assert.That(m.Entries[0], Is.EqualTo(new InteractObjectMessage.StateEntry("mesh-1", 1, 1, 0)));
        Assert.That(m.Entries[1], Is.EqualTo(new InteractObjectMessage.StateEntry("ore-1", 0, 6, 45000)));
        Assert.That(m.Entries[2], Is.EqualTo(new InteractObjectMessage.StateEntry("tel-1", 2, 2, 0)));
    }

    [Test]
    public void ModeAddReadsMeshAndBillboardTail()
    {
        var wMesh = new ByteWriter();
        wMesh.WriteByte(InteractObjectMessage.ModeAdd);
        wMesh.WriteString("dyn-mesh");
        wMesh.WriteByte(1);
        wMesh.WriteByte(InteractObjectMessage.KindMesh);
        wMesh.WriteInt(10000001);
        wMesh.WriteFloat(150); wMesh.WriteFloat(300); wMesh.WriteFloat(450);
        wMesh.WriteFloat(0); wMesh.WriteFloat(0); wMesh.WriteFloat(90);
        wMesh.WriteUnicodeString("urn:llid:model");
        wMesh.WriteUnicodeString("Idle_A");
        wMesh.WriteUnicodeString("assetA");
        wMesh.WriteUnicodeString("assetB");
        wMesh.WriteFloat(1.25f);
        wMesh.WriteBool(true);
        var mMesh = InteractObjectMessage.Read(R(wMesh));
        Assert.That((mMesh.Name, mMesh.State, mMesh.Kind, mMesh.RowId, mMesh.Scale, mMesh.Flag),
            Is.EqualTo(("dyn-mesh", (byte)1, (byte)1, 10000001, 1.25f, true)));
        Assert.That(mMesh.Position, Is.EqualTo(new Vector3(150, 300, 450)));
        Assert.That(mMesh.Rotation, Is.EqualTo(new Vector3(0, 0, 90)));
        Assert.That((mMesh.Model, mMesh.Anim, mMesh.AssetA, mMesh.AssetB),
            Is.EqualTo(("urn:llid:model", "Idle_A", "assetA", "assetB")));

        var wBoard = new ByteWriter();
        wBoard.WriteByte(InteractObjectMessage.ModeAdd);
        wBoard.WriteString("dyn-board");
        wBoard.WriteByte(1);
        wBoard.WriteByte(InteractObjectMessage.KindBillBoard);
        wBoard.WriteInt(10000008);
        wBoard.WriteFloat(1); wBoard.WriteFloat(2); wBoard.WriteFloat(3);
        wBoard.WriteFloat(4); wBoard.WriteFloat(5); wBoard.WriteFloat(6);
        wBoard.WriteUnicodeString("m"); wBoard.WriteUnicodeString("a");
        wBoard.WriteUnicodeString("aa"); wBoard.WriteUnicodeString("ab");
        wBoard.WriteFloat(1f); wBoard.WriteBool(false);
        wBoard.WriteLong(9876543210L);
        wBoard.WriteUnicodeString("Hello Board");
        var mBoard = InteractObjectMessage.Read(R(wBoard));
        Assert.That((mBoard.Kind, mBoard.BoardOwner, mBoard.BoardText),
            Is.EqualTo(((byte)8, 9876543210L, "Hello Board")));
    }

    [Test]
    public void ModeRemoveResultNoticeWeaponAndUnknownReadExactWire()
    {
        var w10 = new ByteWriter();
        w10.WriteByte(InteractObjectMessage.ModeRemove);
        w10.WriteString("dyn-mesh");
        w10.WriteUnicodeString("fx_poof.nif");
        var m10 = InteractObjectMessage.Read(R(w10));
        Assert.That((m10.Name, m10.Effect), Is.EqualTo(("dyn-mesh", "fx_poof.nif")));

        var w13 = new ByteWriter();
        w13.WriteByte(InteractObjectMessage.ModeResult);
        w13.WriteByte(4);
        w13.WriteString("chest-1");
        w13.WriteByte(InteractObjectMessage.KindMesh);
        var m13 = InteractObjectMessage.Read(R(w13));
        Assert.That((m13.Result, m13.Name, m13.Kind), Is.EqualTo(((byte)4, "chest-1", (byte)1)));

        var w14 = new ByteWriter();
        w14.WriteByte(InteractObjectMessage.ModeNotice);
        w14.WriteShort(3);
        w14.WriteInt(20000062);
        w14.WriteInt(1001);
        w14.WriteUnicodeString("Hero");
        var m14 = InteractObjectMessage.Read(R(w14));
        Assert.That((m14.Channel, m14.MapId, m14.NameId, m14.NoticeArg),
            Is.EqualTo(((ushort)3, 20000062, 1001, "Hero")));

        var w15 = new ByteWriter();
        w15.WriteByte(InteractObjectMessage.ModeWeapon);
        w15.WriteInt(42);
        w15.WriteInt(13200001);
        var m15 = InteractObjectMessage.Read(R(w15));
        Assert.That((m15.PcId, m15.WeaponId), Is.EqualTo((42, 13200001)));

        var wUnk = new ByteWriter();
        wUnk.WriteByte(99);
        wUnk.WriteInt(12345);
        var mUnk = InteractObjectMessage.Read(R(wUnk));
        Assert.That(mUnk.Mode, Is.EqualTo(99));
    }
}
