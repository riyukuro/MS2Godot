using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>CharacterAbility 0xDF read and the 0x89 requests (CCharacterAbilityManager::OnCharacterAbility, uicharacterabilitydialog.cpp:206).</summary>
public class CharacterAbilityPacketTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }
    static byte[] Bytes(Ms2.Net.Wire.IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test] public void LoadedReadsTheIdsTheResetTimeAndThePoints() {
        var r = R(w => { w.WriteByte(0); w.WriteInt(2); w.WriteInt(101); w.WriteInt(205); w.WriteLong(1700000000); w.WriteInt(3); });
        var m = (CharacterAbilityMessage.Loaded) CharacterAbilityMessage.Read(r);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(m.Ids, Is.EqualTo(new[] { 101, 205 }));
        Assert.That((m.ResetTime, m.Points), Is.EqualTo((1700000000L, 3)));
    }

    [Test] public void LearnedReadsOneId() {
        var r = R(w => { w.WriteByte(2); w.WriteInt(301); });
        Assert.That(CharacterAbilityMessage.Read(r), Is.EqualTo(new CharacterAbilityMessage.Learned(301)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void ResetReadsNothing() {
        var r = R(w => w.WriteByte(1));
        Assert.That(CharacterAbilityMessage.Read(r), Is.InstanceOf<CharacterAbilityMessage.Reset>());
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(CharacterAbilityMessage.Read(R(w => w.WriteByte(3))), Is.Null);
    }

    [Test] public void RequestsWriteTheirModes() {
        Assert.That(new CharacterAbilityLearnRequest(301).Op, Is.EqualTo(Outbound.CharacterAbility));
        Assert.That(Bytes(new CharacterAbilityLearnRequest(301)), Is.EqualTo(new byte[] { 2, 0x2d, 1, 0, 0 }));
        Assert.That(Bytes(new CharacterAbilityResetRequest()), Is.EqualTo(new byte[] { 1 }));
        Assert.That((ushort) Outbound.CharacterAbility, Is.EqualTo(0x89));
        Assert.That((ushort) Inbound.CharacterAbility, Is.EqualTo(0xDF));
    }
}
