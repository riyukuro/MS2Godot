using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// An actor's cast from the field's skill packets: OnSkillSync @0x141718150 (scenestategame.cpp:3361), OnStateSkill
/// @0x141718b80 (:3476), and the cast test OnSkillUse / OnSkillSync / OnSkillCancel share (<see cref="ActorCast"/>).
/// </summary>
public class SkillSyncTests {
    static byte[] Body(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static void Vec(ByteWriter w, float x, float y, float z) { w.WriteFloat(x); w.WriteFloat(y); w.WriteFloat(z); }

    /// <summary>OnSkillSync's reads (:3363-3374) in order.</summary>
    static byte[] SyncBody(long uid, int actor, byte motion, bool a, bool b) => Body(w => {
        w.WriteLong(uid); w.WriteInt(actor); w.WriteInt(10200001); w.WriteShort(3); w.WriteByte(motion);
        Vec(w, 1, 2, 3); Vec(w, 0, -1, 0); Vec(w, 0, 0, 90); Vec(w, 4, 5, 6);
        w.WriteBool(a); w.WriteBool(b); w.WriteFloat(1.5f);
    });

    [Test] public void SyncReadsTheClientsFields() {
        var s = SkillSync.Read(new ByteReader(SyncBody(0x1122334455L, 77, 2, true, false)));
        Assert.That((s.CastUid, s.ActorId, s.SkillId, s.Level, s.Motion), Is.EqualTo((0x1122334455L, 77, 10200001, (short)3, (byte)2)));
        Assert.That((s.Position, s.Direction, s.Rotation, s.Move), Is.EqualTo((new Vector3(1, 2, 3), new Vector3(0, -1, 0), new Vector3(0, 0, 90), new Vector3(4, 5, 6))));
        Assert.That((s.A, s.B, s.Speed), Is.EqualTo((true, false, 1.5f)));
    }

    [Test] public void StateSkillReadsTypeSixOnly() {
        // :3483-3486: i32 id, i64 uid, i32 skill id, i32 level, only for type 6
        var six = StateSkill.Read(new ByteReader(Body(w => { w.WriteByte(6); w.WriteInt(77); w.WriteLong(9); w.WriteInt(10200001); w.WriteInt(4); })));
        Assert.That((six.ActorId, six.CastUid, six.SkillId, six.Level, six.Rest.Length), Is.EqualTo((77, 9L, 10200001, 4, 0)));
        var other = StateSkill.Read(new ByteReader(Body(w => { w.WriteByte(2); w.WriteInt(77); })));
        Assert.That((other.ActorId, other.Rest.Length), Is.EqualTo((0, 4)), "the switch has no other case");
    }

    [Test] public void TheTrackerQueuesASync() {
        var t = new FieldTracker();
        t.Inbound(new RawPacket((ushort) Inbound.SkillSync, SyncBody(5, 77, 1, false, false)));
        Assert.That(t.Events.TryDequeue(out var ev) && ev is SkillSyncedEvent { Sync: { CastUid: 5, ActorId: 77, Motion: 1 } });
    }

    static SkillSync Sync(long uid, byte motion, bool a = false, bool b = false) => SkillSync.Read(new ByteReader(SyncBody(uid, 77, motion, a, b)));

    [Test] public void ASyncStartsACastUnlessItIsTheRunningOne() {
        var c = new ActorCast();
        Assert.That(c.Sync(Sync(5, 1)), Is.EqualTo(CastSyncResult.NewCast), "no record");
        Assert.That((c.Uid, c.Motion, c.InSkillState), Is.EqualTo((5L, 1, true)));
        Assert.That(c.Sync(Sync(5, 1, true, true)), Is.EqualTo(CastSyncResult.Update));
        Assert.That((c.Charging, c.ChargeStart), Is.EqualTo((true, true)), "FUN_1417ec630(A, B)");
        Assert.That(c.Sync(Sync(5, 2)), Is.EqualTo(CastSyncResult.NewCast), "another motion");
        Assert.That(c.Sync(Sync(6, 2)), Is.EqualTo(CastSyncResult.NewCast), "another uid");
        c.End();
        Assert.That(c.Sync(Sync(6, 2)), Is.EqualTo(CastSyncResult.NewCast), "out of the skill state");
    }

    [Test] public void ACancelNeedsTheUidInTheSkillState() {
        var c = new ActorCast();
        Assert.That(c.Cancel(5), Is.False, "no record");
        c.Use(5, 0);
        Assert.That(c.Cancel(6), Is.False);
        Assert.That(c.Cancel(5), Is.True);
        Assert.That(c.Cancel(5), Is.False, "already out of the state");
    }
}
