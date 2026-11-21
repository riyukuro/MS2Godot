using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The Revival send (SendOp 0x1F) of the revive dialog's buttons.</summary>
public class RevivalPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    static MyCharacter Own() {
        var me = new MyCharacter();
        me.Apply(new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } });
        return me;
    }
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void TheModesWriteTheirByteAndTheCouponForPaidOnes() {
        Assert.That(new RevivalRequest(0).Op, Is.EqualTo(Outbound.Revival));
        Assert.That((ushort)Outbound.Revival, Is.EqualTo(0x1F));
        Assert.That(Bytes(new RevivalRequest(0)), Is.EqualTo(new byte[] { 0 }));
        Assert.That(Bytes(new RevivalRequest(1, false)), Is.EqualTo(new byte[] { 1, 0 }));
        Assert.That(Bytes(new RevivalRequest(1, true)), Is.EqualTo(new byte[] { 1, 1 }));
        Assert.That(Bytes(new RevivalRequest(2, true)), Is.EqualTo(new byte[] { 2, 1 }));
    }

    [Test]
    public void LeavingTheDungeonIsRequestMoveFieldMode1() {
        Assert.That(new RequestMoveFieldLeave().Op, Is.EqualTo(Outbound.RequestMoveField));
        Assert.That(Bytes(new RequestMoveFieldLeave()), Is.EqualTo(new byte[] { 1 }));
    }

    [Test]
    public void TheInboundOpsAreTheClientsRecvOps() {
        Assert.That(((ushort)Inbound.RevivalConfirm, (ushort)Inbound.RevivalCount, (ushort)Inbound.AutoRevive, (ushort)Inbound.DungeonReviveCount),
            Is.EqualTo(((ushort)0x33, (ushort)0x35, (ushort)0xA2, (ushort)0xFB)));
    }

    [Test]
    public void RevivalConfirmSetsTheEndTickAndCountOfTheOwnPlayer() {
        var c = RevivalConfirm.Read(R(w => { w.WriteInt(9); w.WriteInt(5000); w.WriteInt(2); w.WriteByte(7); }));
        Assert.That(c, Is.EqualTo(new RevivalConfirm(9, 5000, 2)), "trailing bytes ignored");
        var me = Own();
        Assert.That(me.RevivalBlocked, Is.False);
        Assert.That(me.ApplyRevivalConfirm(new RevivalConfirm(8, 5000, 2)), Is.EqualTo((false, false)), "another object");
        Assert.That(me.RevivalConfirmCount, Is.EqualTo(0));
        Assert.That(me.ApplyRevivalConfirm(c), Is.EqualTo((false, true)), "a count above 0: the guide trigger");
        Assert.That((me.RevivalEndTick, me.RevivalConfirmCount, me.RevivalBlocked), Is.EqualTo((5000u, 2, true)), "IsRevivalBlocked: count > 0");
        Assert.That(me.ExpireRevival(4999), Is.False, "before the end tick");
        Assert.That(me.ExpireRevival(5000), Is.True, "the end tick passed: both cleared");
        Assert.That((me.RevivalEndTick, me.RevivalConfirmCount, me.RevivalBlocked), Is.EqualTo((0u, 0, false)));
        me.ApplyRevivalConfirm(new RevivalConfirm(9, 9000, 3));
        Assert.That(me.ApplyRevivalConfirm(new RevivalConfirm(9, 0, 0)), Is.EqualTo((true, false)), "the count falls to 0: the effect, no trigger");
    }

    [Test]
    public void TheRevivalFlagBlocksUntilTheNextRevival() {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[1].Health = 400; u.Stats[2].Health = 480;
        me.Apply(u);
        me.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 0 } }));
        Assert.That(me.Die(9, flag: true), Is.True);
        Assert.That(me.RevivalBlocked, Is.True, "DeadUser's flag: PC::FUN_141661ba0(1)");
        Assert.That(me.Revive(9, 0), Is.True);
        Assert.That(me.RevivalBlocked, Is.False, "Revival's flag byte 0");
        me.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 0 } }));
        me.Die(9);
        me.Revive(9, 1);
        Assert.That(me.RevivalBlocked, Is.True, "Revival's flag byte 1");
    }

    [Test]
    public void RevivalCountAndDungeonReviveCountAreStored() {
        var me = Own();
        me.Apply(RevivalCount.Read(R(w => w.WriteInt(3))));
        me.Apply(DungeonReviveCount.Read(R(w => w.WriteInt(5))));
        Assert.That((me.MesoRevivalsUsed, me.DungeonRevivalsUsed), Is.EqualTo((3, 5)));
    }

    [Test]
    public void AutoReviveReadsTheDuration() {
        Assert.That(AutoRevive.Read(R(w => w.WriteInt(10000))), Is.EqualTo(new AutoRevive(10000)));
    }
}
