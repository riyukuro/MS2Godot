using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>SendOp::Skill 0x20 mode 0 (StateSkill::FUN_1417ebc00, WriteSkillUse @0x1401fd510) and GenerateUniqueId @0x14028d0e0.</summary>
public class SkillRequestTests
{
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Expect(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    static SkillCastRequest Cast(bool feature, int attach = 0, string node = "") =>
        new(0x1122334455667788, 5000, 10000001, 3, 2, new Vector3(1, 2, 3), new Vector3(0, 1, 0), new Vector3(0, 0, 90),
            12.5f, 7777, true, 99, 0x123456789, feature, attach, node);

    static void Body(ByteWriter w)
    {
        w.WriteByte(0);                                             // the sub-mode
        w.WriteLong(0x1122334455667788); w.WriteInt(5000); w.WriteInt(10000001);
        w.WriteShort(3); w.WriteByte(2);
        foreach (var f in new float[] { 1, 2, 3, 0, 1, 0, 0, 0, 90 }) w.WriteFloat(f);
        w.WriteFloat(12.5f); w.WriteInt(7777);
        w.WriteBool(true); w.WriteLong(99); w.WriteLong(0x123456789);
    }

    [Test]
    public void TheCastWritesModeZeroThenTheSkillUsePayload()
    {
        Assert.That(Cast(false).Op, Is.EqualTo(Outbound.Skill));
        Assert.That((ushort)Outbound.Skill, Is.EqualTo(0x20));
        Assert.That(Bytes(Cast(false)), Is.EqualTo(Expect(Body)), "feature 174 off: nothing after the cooldown key");
        Assert.That(Bytes(Cast(false)).Length, Is.EqualTo(1 + 8 + 4 + 4 + 2 + 1 + 36 + 4 + 4 + 1 + 8 + 8));
    }

    [Test]
    public void TheHoldAttackFeatureAddsTheAttachedTarget()
    {
        Assert.That(Bytes(Cast(true)), Is.EqualTo(Expect(w => { Body(w); w.WriteBool(false); })), "not attached: only the bool");
        Assert.That(Bytes(Cast(true, 4321, "Bip01")),
            Is.EqualTo(Expect(w => { Body(w); w.WriteBool(true); w.WriteInt(4321); w.WriteUnicodeString("Bip01"); })));
    }

    [Test]
    public void TheCastIdIsTheServerTimeThenFiveCounterDigits()
    {
        // "%09I64d%05d" of (time - 1e9, seq % 100000)
        Assert.That(UniqueId.Make(1_790_000_000, 7), Is.EqualTo(79000000000007L));
        Assert.That(UniqueId.Make(1_790_000_000, 123456), Is.EqualTo(79000000023456L), "the counter keeps five digits");
        Assert.That(UniqueId.Make(1_000_000_005, 3), Is.EqualTo(500003L), "leading zeros of the time drop out");
        Assert.That(UniqueId.Make(999_999_995, 3), Is.EqualTo(-500003L), "\"-00000005\" + \"00003\"");
        long a = UniqueId.Next(1_790_000_000), b = UniqueId.Next(1_790_000_000);
        Assert.That(b, Is.EqualTo(a + 1), "each id takes the next counter value");
    }
    [Test]
    public void TheDamageSendWritesModeOneZeroThenEachTargetAndItsChain()
    {
        // StateSkill::FUN_1417ecce0 (stateskill.cpp:1370-1398)
        var p = new SkillDamageRequest(0x0102030405060708, 2, new Vector3(10, 20, 30), new Vector3(0, -1, 0), 4,
        [
            new SkillDamageTarget(111, 5001, 0, []),
            new SkillDamageTarget(222, 5002, 1, [new SkillDamageLink(333, 5003, 1, 0), new SkillDamageLink(444, 5004, 2, 0)]),
            new SkillDamageTarget(555, 0, 2, []),
        ]);
        Assert.That(p.Op, Is.EqualTo(Outbound.Skill));
        var want = Expect(w =>
        {
            w.WriteByte(1); w.WriteByte(0);
            w.WriteLong(0x0102030405060708); w.WriteByte(2);
            foreach (var f in new float[] { 10, 20, 30, 0, -1, 0 }) w.WriteFloat(f);
            w.WriteByte(3); w.WriteInt(4);
            w.WriteLong(111); w.WriteInt(5001); w.WriteByte(0); w.WriteBool(false);
            w.WriteLong(222); w.WriteInt(5002); w.WriteByte(1);
            w.WriteBool(true); w.WriteLong(333); w.WriteInt(5003); w.WriteByte(1); w.WriteByte(0);
            w.WriteBool(true); w.WriteLong(444); w.WriteInt(5004); w.WriteByte(2); w.WriteByte(0);
            w.WriteBool(false);
            w.WriteLong(555); w.WriteInt(0); w.WriteByte(2); w.WriteBool(false);
        });
        Assert.That(Bytes(p), Is.EqualTo(want));
    }

    [Test]
    public void TheDamageSendWithoutTargetsEndsAtTheSendCount()
    {
        var p = new SkillDamageRequest(7, 0, Vector3.Zero, Vector3.UnitX, 0, []);
        Assert.That(Bytes(p).Length, Is.EqualTo(1 + 1 + 8 + 1 + 12 + 12 + 1 + 4));
    }

    [Test]
    public void TheSkillStateSyncWritesModeTwo()
    {
        // StateSkill::SendChargeState @0x1417ec2f0 (stateskill.cpp:1282-1294)
        var p = new SkillSyncRequest(0x1122334455667788, 10000001, 3, 2, new Vector3(1, 2, 3), new Vector3(0, 1, 0), new Vector3(0, 0, 90),
            new Vector3(4, 5, 6), true, false, 0.25f);
        Assert.That(p.Op, Is.EqualTo(Outbound.Skill));
        Assert.That(Bytes(p), Is.EqualTo(Expect(w =>
        {
            w.WriteByte(2);
            w.WriteLong(0x1122334455667788); w.WriteInt(10000001); w.WriteShort(3); w.WriteByte(2);
            foreach (var f in new float[] { 1, 2, 3, 0, 1, 0, 0, 0, 90, 4, 5, 6 }) w.WriteFloat(f);
            w.WriteBool(true); w.WriteBool(false); w.WriteFloat(0.25f);
        })));
        Assert.That(Bytes(p).Length, Is.EqualTo(1 + 8 + 4 + 2 + 1 + 48 + 1 + 1 + 4));
    }
}
