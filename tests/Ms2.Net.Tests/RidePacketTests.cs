using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>CSceneStateGame::OnResponseRide (@0x14172a8f0, scenestategame.cpp:12026), ReadRideOffAction (@0x14077e5b0,
/// rideonaction.cpp:159) and the RequestRide writers.</summary>
public class RidePacketTests
{
    static ResponseRide Read(ByteReader r, int self) => ResponseRide.Read(r, self, ProtocolProfile.Kms2525);

    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    static byte[] Bytes(IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestRide));
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:129 ResponseRide = 0x0079; upstream SendOp ResponseRide = 0x007B
        Assert.That((ushort)Inbound.ResponseRide, Is.EqualTo(0x0079));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.ResponseRide), Is.EqualTo((ushort)0x7B));
        // commonfiles/opcodes.h:370 SendOp RideSync = 0x0041; upstream RecvOp RideSync = 0x0042
        Assert.That((ushort)Outbound.RideSync, Is.EqualTo(0x0041));
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.RideSync), Is.EqualTo((ushort)0x42));
    }

    [Test]
    public void RideSyncIsTheUserSyncLayoutUnderItsOwnOpcode()
    {
        // StateRideeController::vfunc_0 (state.cpp:537-562) writes what StateLocalController::vfunc_0 (state.cpp:483-505) does
        UserSyncOut Make(bool ride)
        {
            var p = new UserSyncOut { SyncNumber = 3, ServerTick = 1000, ClientTick = 2000, Ride = ride };
            p.Records.Add((new StateSync { State = 2, SubState = 0x18, Position = new Vector3S(1, 2, 3), Animation = 9 }, 1000));
            return p;
        }
        Assert.That(Make(true).Op, Is.EqualTo(Outbound.RideSync));
        Assert.That(Make(false).Op, Is.EqualTo(Outbound.UserSync));
        static byte[] Body(IOutPacket p)
        {
            var w = new ByteWriter();
            p.Write(w);
            return w.Buffer.AsSpan(0, w.Length).ToArray();
        }
        Assert.That(Body(Make(true)), Is.EqualTo(Body(Make(false))));
        var back = UserSyncOut.Read(new ByteReader(Body(Make(true))));
        Assert.That(back.Records, Has.Count.EqualTo(1));
        Assert.That(back.Records[0].Record.Position.Z, Is.EqualTo((short)3));
    }

    [Test]
    public void ModeZeroReadsTheRideOnAction()
    {
        var p = Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(77);
            w.WriteByte(0); w.WriteInt(60000001); w.WriteInt(500); w.WriteBool(true);
            w.WriteByte(0xEE);
        }), 1);
        Assert.That((p.Mode, p.ObjectId), Is.EqualTo(((byte)0, 77)));
        Assert.That((p.RideOn.Type, p.RideOn.RideId, p.RideOn.ObjectId, p.RideOn.Flag), Is.EqualTo(((byte)0, 60000001, 500, true)));
    }

    [Test]
    public void Gms2ModeZeroHasNoM28Byte()
    {
        var r = Reader(w =>
        {
            w.WriteByte(0); w.WriteInt(77);
            w.WriteByte(0); w.WriteInt(60000001); w.WriteInt(500);
            w.WriteByte(0xEE);
        });
        var p = ResponseRide.Read(r, 1, ProtocolProfile.Gms2);
        Assert.That((p.RideOn.Type, p.RideOn.RideId, p.RideOn.ObjectId, p.RideOn.Flag), Is.EqualTo(((byte)0, 60000001, 500, false)));
        Assert.That(r.Available, Is.EqualTo(1), "GMS2 ReadBody skips m_b28, as FieldAddUser does");
    }

    [Test]
    public void ARideOnTypeAboveThreeIsNullAndStopsAfterTheType()
    {
        var r = Reader(w => { w.WriteByte(0); w.WriteInt(5); w.WriteByte(4); w.WriteInt(123); });
        var p = Read(r, 1);
        Assert.That(p.RideOn, Is.Null);
        Assert.That(r.Available, Is.EqualTo(4), "ReadRideOnAction reads nothing after a type without a case");
    }

    [Test]
    public void ModeOneReadsTheRideOffActionWithItsFields()
    {
        var p = Read(Reader(w =>
        {
            w.WriteByte(1); w.WriteInt(9);
            w.WriteByte(2); w.WriteByte(1); w.WriteInt(42); w.WriteUnicodeString("obj");
        }), 1);
        Assert.That(p.RideOff, Is.EqualTo(new RideOffInteract(1, 42, "obj")));
        Assert.That(p.RideOff.Type, Is.EqualTo((byte)2));
    }

    [Test]
    public void TypeTwentyHasNoCaseSoTheActionIsNull()
    {
        // rideonaction.cpp:159-243: no case 20 (RideOffActionReact), so OnResponseRide mode 1 runs vfunc_121(null)
        var r = Reader(w => { w.WriteByte(1); w.WriteInt(9); w.WriteByte(20); w.WriteByte(0); w.WriteInt(5); w.WriteUnicodeString("r"); });
        var p = Read(r, 1);
        Assert.That(p.RideOff, Is.Null);
        Assert.That(RideOffAction.Known(20), Is.False);
        Assert.That(r.Available, Is.EqualTo(1 + 4 + 2 + 2));
    }

    [Test]
    public void EveryRideOffTypeReadsItsLayout()
    {
        void Check(byte type, Action<ByteWriter> body, RideOffAction expect)
        {
            var r = Reader(w => { w.WriteByte(type); w.WriteByte(1); body(w); });
            Assert.That(RideOffAction.Read(r), Is.EqualTo(expect), $"type {type}");
            Assert.That(r.Available, Is.EqualTo(0), $"type {type} reads its whole body");
        }
        Check(0, _ => { }, new RideOffAction(0, 1));
        Check(1, w => { w.WriteInt(3); w.WriteBool(true); w.WriteInt(4); }, new RideOffUseSkill(1, 3, true, 4));
        Check(25, w => { w.WriteLong(3); w.WriteBool(false); w.WriteInt(4); }, new RideOffUseGlideItem(1, 3, false, 4));
        Check(3, w => w.WriteInt(7), new RideOffTaxi(1, 7));
        Check(4, w => { w.WriteInt(3); w.WriteByte(2); w.WriteLong(9); }, new RideOffCashCall(1, 3, 2, 9));
        Check(5, _ => { }, new RideOffAction(5, 1));
        Check(6, w => { w.WriteByte(2); w.WriteInt(9); }, new RideOffTakeLR(1, 2, 9));
        Check(7, _ => { }, new RideOffAction(7, 1));
        Check(8, w => w.WriteLong(5), new RideOffRecall(1, 5));
        Check(9, w => { w.WriteLong(5); w.WriteByte(6); }, new RideOffSummonPetOn(1, 5, 6));
        Check(10, w => w.WriteLong(5), new RideOffSummonPetTransfer(1, 5));
        Check(11, w => w.WriteByte(3), new RideOffHomeConvenient(1, 3));
        foreach (byte t in new byte[] { 12, 13, 14, 15, 18, 23, 24, 26 }) Check(t, _ => { }, new RideOffAction(t, 1));
        Check(16, w => { w.WriteUnicodeString("h"); w.WriteInt(2); w.WriteShort(unchecked((short)0xFFFE)); w.WriteInt(3); },
            new RideOffHomemade(1, "h", 2, 0xFFFE, 3));
        Check(22, w => { w.WriteUnicodeString("n"); w.WriteInt(2); }, new RideOffNurturing(1, "n", 2));
        Check(17, w => { w.WriteInt(2); w.WriteShort(5); w.WriteInt(3); }, new RideOffAutoInteraction(1, 2, 5, 3));
        Check(19, w => { w.WriteInt(2); w.WriteLong(3); }, new RideOffCoupleEmotion(1, 2, 3));
        Check(21, w => { w.WriteLong(2); w.WriteUnicodeString("f"); }, new RideOffUseFunctionItem(1, 2, "f"));
        Assert.That(RideOffAction.Read(Reader(w => w.WriteByte(27))), Is.Null);
    }

    [Test]
    public void ModeTwoCarriesTheSummonOnlyForTheOwnPc()
    {
        var own = Read(Reader(w => { w.WriteByte(2); w.WriteInt(5); w.WriteInt(60000002); w.WriteLong(0x1122334455L); }), 5);
        Assert.That((own.RideeId, own.ItemUid), Is.EqualTo((60000002, 0x1122334455L)));
        var r = Reader(w => { w.WriteByte(2); w.WriteInt(6); });
        var other = Read(r, 5);
        Assert.That((other.ObjectId, other.RideeId, other.ItemUid, r.Available), Is.EqualTo((6, 0, 0L, 0)));
    }

    [Test]
    public void PassengerAndFlagModes()
    {
        var m3 = Read(Reader(w => { w.WriteByte(3); w.WriteInt(1); w.WriteInt(2); w.WriteByte(1); }), 0);
        Assert.That((m3.ObjectId, m3.RiderId, m3.Seat), Is.EqualTo((1, 2, (byte)1)));
        var m4 = Read(Reader(w => { w.WriteByte(4); w.WriteInt(1); w.WriteInt(2); }), 0);
        Assert.That((m4.ObjectId, m4.RiderId), Is.EqualTo((1, 2)));
        var m5 = Read(Reader(w => { w.WriteByte(5); w.WriteInt(1); }), 0);
        Assert.That(m5.ObjectId, Is.EqualTo(1));
        var m6 = Read(Reader(w => { w.WriteByte(6); w.WriteInt(1); w.WriteByte(1); }), 0);
        Assert.That((m6.ObjectId, m6.Flag), Is.EqualTo((1, true)));
    }

    [Test]
    public void RequestWritersMatchTheClient()
    {
        // statesummon.cpp:353-355: u8 0, RideOnActionUseItem::vfunc_1, a cleared ugc look
        var item = Bytes(new RideOnItemRequest(0x01020304, 0x0A0B0C0D0E0F1011L, true));
        var look = new ByteWriter();
        ItemReader.WriteUgcLook(look, new UgcLook());
        var head = new byte[] { 0, 1, 4, 3, 2, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0x11, 0x10, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A };
        Assert.That(item, Is.EqualTo(head.Concat(look.Buffer.AsSpan(0, look.Length).ToArray()).ToArray()));
        Assert.That(look.Length, Is.EqualTo(8 + 2 + 2 + 1 + 4 + 8 + 8 + 2 + 8 + 2 + 1), "the cleared look: empty strings");

        Assert.That(Bytes(new RideOffRequest()), Is.EqualTo(new byte[] { 1, 0, 0 }));
        Assert.That(Bytes(new RideAlongRequest(2, 3)), Is.EqualTo(new byte[] { 2, 2, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new RideBoardRequest(0x10)), Is.EqualTo(new byte[] { 3, 0x10, 0, 0, 0 }));
        Assert.That(Bytes(new RideLeaveRequest()), Is.EqualTo(new byte[] { 4 }));
        Assert.That(Bytes(new RideOptionRequest(1)), Is.EqualTo(new byte[] { 6, 1 }));
    }
}
