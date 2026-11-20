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
}
