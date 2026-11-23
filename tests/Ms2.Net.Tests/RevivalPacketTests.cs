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
        Assert.That(me.RevivalBlocked, Is.True, "DeadUser's flag: the flag byte of DeadUser");
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

    [Test]
    public void FallDamageIsSentAsAFloatAndTheReplyEntersState1f() {
        Assert.That((ushort)Outbound.StateFallDamage, Is.EqualTo(0x34));
        Assert.That(Bytes(new StateFallDamageRequest(2.5f)), Is.EqualTo(BitConverter.GetBytes(2.5f)));
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[1].Health = 400; u.Stats[2].Health = 480;
        me.Apply(u);
        Assert.That(me.ApplyFallDamage(new StateFallDamage(8, 5)), Is.EqualTo(0), "another object");
        Assert.That(me.ApplyFallDamage(new StateFallDamage(9, 5)), Is.EqualTo(-1), "alive: guide value -1");
        Assert.That(me.State, Is.EqualTo(0x1f));
    }

    [Test]
    public void FallDamageOnAMountPicksTheVoiceOfTheGender() {
        MyCharacter Me(byte gender) {
            var me = new MyCharacter();
            var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a", Gender = gender } };
            u.Stats[0].Health = 500; u.Stats[2].Health = 480;
            me.Apply(u);
            return me;
        }
        var fall = new StateFallDamage(9, 5);
        Assert.That(Me(1).FallDamageVoice(fall, true), Is.EqualTo(MyCharacter.FemaleDropDamgVoiceKey), "gender 1: +0x2d0");
        Assert.That(Me(0).FallDamageVoice(fall, true), Is.EqualTo(MyCharacter.MaleDropDamgVoiceKey), "gender 0: +0x2d8");
        Assert.That(Me(2).FallDamageVoice(fall, true), Is.Null, "another gender: no name");
        Assert.That(Me(1).FallDamageVoice(fall, false), Is.Null, "no mount");
        Assert.That(Me(1).FallDamageVoice(new StateFallDamage(8, 5), true), Is.Null, "another object");
    }

    [Test]
    public void TombstoneIsReadAndSentWithItsObjectAndHitter() {
        Assert.That((ushort)Outbound.Tombstone, Is.EqualTo(0x2A));
        Assert.That(Bytes(new TombstoneRequest(9)), Is.EqualTo(new byte[] { 9, 0, 0, 0, 1, 0, 0, 0 }));
        var t = TombstoneUpdate.Read(R(w => { w.WriteInt(9); w.WriteByte(2); w.WriteByte(3); w.WriteInt(7); w.WriteByte(1); }));
        Assert.That(t, Is.EqualTo(new TombstoneUpdate(9, 2, 3, 7, true)));
        var me = Own();
        Assert.That(me.Apply(new TombstoneUpdate(8, 2, 3, 7, false)), Is.False);
        Assert.That(me.Apply(t), Is.True);
        Assert.That((me.TombstoneKind, me.TombstoneValue, me.TombstoneHitter, me.TombstoneHidden), Is.EqualTo(((byte)2, (byte)3, 7, false)));
        me.Apply(new TombstoneUpdate(9, 0, 0, 0, false));
        Assert.That(me.TombstoneHidden, Is.True);
    }

    [Test]
    public void BreakableIsSentWithTheNameTagAndSkill() {
        var p = new BreakableRequest("ab", 0x0102030405060708, 0x0A0B0C0D, 0x0E0F, 3, 4);
        Assert.That((p.Op, (ushort)Outbound.Breakable), Is.EqualTo((Outbound.Breakable, (ushort)0x27)));
        Assert.That(Bytes(p), Is.EqualTo(new byte[] {
            2, 0, (byte)'a', (byte)'b',
            8, 7, 6, 5, 4, 3, 2, 1,
            0x0D, 0x0C, 0x0B, 0x0A,
            0x0F, 0x0E,
            3, 4 }));
    }

    static MyCharacter Living() {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[1].Health = 400; u.Stats[2].Health = 480;
        me.Apply(u);
        return me;
    }
    static void ZeroHealth(MyCharacter me) =>
        me.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 0 } }));

    /// <summary>MyPC::vfunc_137's dead branch (mypc.cpp:1346-1355) runs on every DeadUser that leaves the player dead, not only on
    /// the flip; a FieldAddUser does not run it.</summary>
    [Test]
    public void DiedIsRaisedForEveryDeadUserWhileDeadAndNotForADeadFieldAddUser() {
        var me = Living();
        int died = 0; me.Died += () => died++;
        ZeroHealth(me);
        Assert.That(me.Die(9), Is.True);
        Assert.That(died, Is.EqualTo(1));
        Assert.That(me.Die(9), Is.False, "already dead: the flag does not change");
        Assert.That(died, Is.EqualTo(2), "the dead branch runs again");
        var again = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };   // no health: dead on arrival
        me.Apply(again);
        Assert.That((me.Dead, died), Is.EqualTo((true, 2)), "a dead FieldAddUser raises nothing");
    }

    /// <summary>A player who entered the field dead (FieldAddUser with no health) and then gets DeadUser runs the dead branch,
    /// so the revive dialog opens and the player can revive.</summary>
    [Test]
    public void APlayerWhoEnteredDeadGetsDiedFromALaterDeadUser() {
        var me = new MyCharacter();
        int died = 0; me.Died += () => died++;
        me.Apply(new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } });   // no health: dead on arrival
        Assert.That((me.Dead, died), Is.EqualTo((true, 0)));
        me.Die(9);
        Assert.That(died, Is.EqualTo(1));
    }

    /// <summary>A FieldAddUser with no health sets the dead flag and no state (scenestategame.cpp:11185); PC::vfunc_124
    /// (pc.cpp:1199) then refuses idle until Revival, and with health 0 and a max health until the health comes back.</summary>
    [Test]
    public void ADeadFieldAddUserHoldsStateZeroUntilRevivalAndHealth() {
        var me = new MyCharacter();
        me.Apply(new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } });
        Assert.That((me.Dead, me.DeathOnlyStates, me.State), Is.EqualTo((true, true, 0)));
        me.ApplyUserState(9, 1);
        Assert.That(me.State, Is.EqualTo(0), "idle is refused while dead");
        var lived = Living();
        ZeroHealth(lived);
        lived.Die(9);
        lived.Revive(9, 0);
        Assert.That((lived.Dead, lived.DeathOnlyStates, lived.State), Is.EqualTo((false, true, 0x18)), "revived with health 0 of 500");
        lived.Apply(new PlayerStat(9, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 100 } }));
        Assert.That(lived.DeathOnlyStates, Is.False);
    }

    [Test]
    public void ACountAboveZeroAtDeathSetsTheFlagAndFieldAddUserCarriesTheRevivalState() {
        var me = Living();
        me.ApplyRevivalConfirm(new RevivalConfirm(9, 5000, 2));
        ZeroHealth(me);
        me.Die(9);
        me.ExpireRevival(6000);
        Assert.That(me.RevivalBlocked, Is.True, "the death sets the flag when the count is above 0 (pc.cpp:4598-4599)");
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" }, RevivalEndTick = 9000, RevivalCount = 3, RevivalFlag = true };
        u.Stats[2].Health = 5;
        me.Apply(u);
        Assert.That((me.RevivalEndTick, me.RevivalConfirmCount, me.RevivalFlag), Is.EqualTo((9000u, 3, true)));
    }

    static ByteReader Hex(string h) => new(Convert.FromHexString(h));

    /// <summary>A recorded GMS2 field session with two deaths (server ticks from its time sync). The first revival's RevivalConfirm
    /// (tick 126354039) sets the penalty end an hour later with count 3; the next DeadUser (tick 126414478) has flag 0 but the count
    /// is still above 0, so the death sets the revival flag (pc.cpp:4598-4599) and IsRevivalBlocked gives the metal tomb
    /// (MS2TombStoneDark, pc.cpp:4043-4047). Each end tick set raises RevivalEndTickSet (PC::FUN_1416619d0, pc.cpp:2018-2031: the
    /// own player's penalty icon), the expiry with 0.</summary>
    [Test]
    public void ARecordedRepeatDeathInThePenaltyIsBlockedAndEachEndTickReachesThePenaltyIcon() {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 481931, User = new UserBlock { Name = "a" }, RevivalEndTick = 129803332, RevivalCount = 2 };
        u.Stats[0].Health = 500; u.Stats[1].Health = 400; u.Stats[2].Health = 480;
        var ends = new List<uint>();
        me.RevivalEndTickSet += ends.Add;
        me.Apply(u);
        Assert.That(ends, Is.EqualTo(new[] { 129803332u }), "FieldAddUser: FUN_141661b20 sets the end tick (pc.cpp:2646)");
        void Death(string dead, string confirm, string revival) {
            me.Apply(new PlayerStat(481931, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 0 } }));
            var d = DeadUser.Read(Hex(dead));
            me.Die(d.ObjectId, d.Flag);
            me.ApplyRevivalConfirm(RevivalConfirm.Read(Hex(confirm)));
            me.Revive(Revival.Read(Hex(revival)).ObjectId, 0);
            me.Apply(new PlayerStat(481931, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 480 } }));
        }
        Death("8b5a070000", "8b5a0700f7f0be0703000000", "8b5a070000");
        Assert.That((me.RevivalEndTick, me.RevivalConfirmCount, me.RevivalBlocked), Is.EqualTo((129954039u, 3, true)));
        Assert.That(me.RevivalEndTick - 126354039u, Is.EqualTo(3600000u), "an hour from the revival");
        Assert.That(me.ExpireRevival(126414478), Is.False);
        me.Apply(new PlayerStat(481931, new Dictionary<byte, long[]> { [PlayerStat.Health] = new long[] { 500, 400, 0 } }));
        var again = DeadUser.Read(Hex("8b5a070000"));
        Assert.That(again.Flag, Is.False);
        me.Die(again.ObjectId, again.Flag);
        Assert.That((me.Dead, me.RevivalFlag, me.RevivalBlocked), Is.EqualTo((true, true, true)), "the repeat death: the metal tomb");
        me.ApplyRevivalConfirm(RevivalConfirm.Read(Hex("8b5a07000be4bf0704000000")));
        Assert.That(ends, Is.EqualTo(new[] { 129803332u, 129954039u, 130016267u }));
        Assert.That(me.ExpireRevival(130016267), Is.True);
        Assert.That(ends[^1], Is.EqualTo(0u), "the expiry removes the icon");
        Assert.That(me.RevivalConfirmCount, Is.EqualTo(0));
    }
}
