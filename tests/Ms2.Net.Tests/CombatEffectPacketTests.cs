using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// OnInvincibleEffect 0x83, OnOneTimeEffect 0xF1 (scenestategame.cpp:8453), OnTimeScale 0xF3 (:8465), SendOp::StateSkill 0x21
/// (StateSkill::UseStateSkill, stateskill.cpp:2045-2070) and SendOp::RequestAdditionalEffect 0x5C
/// (uiadditionalcontroldialog.cpp:139-160).
/// </summary>
public class CombatEffectPacketTests
{
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
    static byte[] Expect(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void TheOpcodeNumbers()
    {
        var k = ProtocolProfile.Kms2525; var g = ProtocolProfile.Gms2;
        Assert.That(new[] { k.Number(Inbound.InvincibleEffect), k.Number(Inbound.OneTimeEffect), k.Number(Inbound.TimeScale) },
            Is.EqualTo(new ushort[] { 0x83, 0xF1, 0xF3 }));
        Assert.That(new[] { k.Number(Outbound.StateSkill), k.Number(Outbound.RequestAdditionalEffect) }, Is.EqualTo(new ushort[] { 0x21, 0x5C }));
        Assert.That(new[] { g.Number(Inbound.InvincibleEffect), g.Number(Inbound.OneTimeEffect), g.Number(Inbound.TimeScale) },
            Is.EqualTo(new ushort[] { 0x85, 0xF3, 0xF5 }));
        Assert.That(new[] { g.Number(Outbound.StateSkill), g.Number(Outbound.RequestAdditionalEffect) }, Is.EqualTo(new ushort[] { 0x21, 0x5D }));
    }

    [Test]
    public void OneTimeEffectReadsTheNameOnlyOnAdd()
    {
        var add = OneTimeEffect.Read(R(w => { w.WriteInt(7); w.WriteBool(true); w.WriteUnicodeString("Eff/Fx.xml"); }));
        Assert.That(add, Is.EqualTo(new OneTimeEffect(7, true, "Eff/Fx.xml")));
        var remove = OneTimeEffect.Read(R(w => { w.WriteInt(7); w.WriteBool(false); }));
        Assert.That(remove, Is.EqualTo(new OneTimeEffect(7, false, "")));
        Assert.That(() => OneTimeEffect.Read(R(w => { w.WriteInt(7); w.WriteBool(false); w.WriteByte(0); })), Throws.Exception);
    }

    [Test]
    public void TimeScaleReadsFourValuesAndTheCurve()
    {
        var p = TimeScale.Read(R(w => { w.WriteBool(true); w.WriteFloat(0.2f); w.WriteFloat(1f); w.WriteFloat(1500f); w.WriteByte(2); }));
        Assert.That(p, Is.EqualTo(new TimeScale(true, 0.2f, 1f, 1500f, 2)));
        Assert.That(() => TimeScale.Read(R(w => { w.WriteBool(false); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); })), Throws.Exception);
    }

    [Test]
    public void StateSkillWritesModeZeroAndTheStateLayout()
    {
        var p = new StateSkillRequest(0x1122334455667788, 5000, 10200001, 1, 0x1e, 123456, 0x2233445566);
        Assert.That(p.Op, Is.EqualTo(Outbound.StateSkill));
        Assert.That(Bytes(p), Is.EqualTo(Expect(w => {
            w.WriteByte(0); w.WriteLong(0x1122334455667788); w.WriteInt(5000); w.WriteInt(10200001); w.WriteShort(1);
            w.WriteInt(0x1e); w.WriteInt(123456); w.WriteLong(0x2233445566);
        })));
        Assert.That(Bytes(p).Length, Is.EqualTo(1 + 8 + 4 + 4 + 2 + 4 + 4 + 8));
    }

    [Test]
    public void AdditionalEffectAddAndReset()
    {
        var add = AdditionalEffectRequest.Add(70000001, 0x10003, 2.5f);
        Assert.That(add.Op, Is.EqualTo(Outbound.RequestAdditionalEffect));
        Assert.That(Bytes(add), Is.EqualTo(Expect(w => { w.WriteByte(0); w.WriteInt(70000001); w.WriteInt(3); w.WriteFloat(2.5f); })),
            "the level is kept as u16");
        Assert.That(Bytes(AdditionalEffectRequest.ResetAll()), Is.EqualTo(new byte[] { 1 }));
    }
}
