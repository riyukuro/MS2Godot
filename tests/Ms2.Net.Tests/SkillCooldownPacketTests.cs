using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The skill failure and cooldown packets as the client reads them: OnSkillUseFailed @0x1417187b0 (scenestategame.cpp:3425) and
/// OnSkillCooldown @0x14172f030 (scenestategame.cpp:7856).
/// </summary>
public class SkillCooldownPacketTests {
    static ByteReader Bytes(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    [Test] public void UseFailedReadsTheStateOnlyInModeSix() {
        var five = SkillUseFailed.Read(Bytes(w => { w.WriteByte(5); w.WriteLong(0x1122334455L); w.WriteInt(10200001); w.WriteByte(3);
            w.WriteInt(5000); w.WriteShort(85); w.WriteInt(2); }));
        Assert.That(five, Is.EqualTo(new SkillUseFailed(5, 0x1122334455L, 10200001, 3, 0, 5000, 8.5f, 2)));
        var six = SkillUseFailed.Read(Bytes(w => { w.WriteByte(6); w.WriteLong(1); w.WriteInt(10200001); w.WriteByte(0);
            w.WriteInt(0x10); w.WriteInt(0); w.WriteShort(0); w.WriteInt(0); }));
        Assert.That((six.State, six.StartTick, six.Cooldown, six.Uses), Is.EqualTo((0x10, 0, 0f, 0)), ":3442: the state before the start tick");
    }

    [Test] public void CooldownReadsItsEntries() {
        var c = SkillCooldown.Read(Bytes(w => { w.WriteByte(2); w.WriteLong((7L << 32) | 10200001); w.WriteInt(90000); w.WriteInt(1);
            w.WriteLong(10200002); w.WriteInt(0); w.WriteInt(0); }));
        Assert.That(c.Entries, Is.EqualTo(new[] { new SkillCooldownEntry((7L << 32) | 10200001, 90000, 1), new SkillCooldownEntry(10200002, 0, 0) }));
        Assert.That(SkillCooldown.Read(Bytes(w => w.WriteByte(0))).Entries, Is.Empty);
    }

    [Test] public void TheOpcodesAreTheClients() =>
        Assert.That(((ushort)Inbound.SkillUseFailed, (ushort)Inbound.SkillCooldown, (ushort)Inbound.SkillResetCooldown), Is.EqualTo((0x40, 0x42, 0x43)));
}
