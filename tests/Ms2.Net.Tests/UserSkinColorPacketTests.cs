using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>UserSkinColor as CSceneStateGame::OnUserSkinColor (scenestategame.cpp:3239) reads it.</summary>
public class UserSkinColorPacketTests {
    [Test] public void ThePacketIsAnObjectIdAndTwoColours() {
        var w = new ByteWriter();
        w.WriteInt(77); w.WriteInt(0x112233); w.WriteInt(0x445566);
        var c = UserSkinColor.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(c, Is.EqualTo(new UserSkinColor(77, 0x112233, 0x445566)));
    }

    [Test] public void TheKmsNumberIsOpcode0x30AndGms2HasNone() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.UserSkinColor), Is.EqualTo((ushort) 0x30));
        Assert.That(ProtocolProfile.Kms2525.Is(0x30, Inbound.UserSkinColor));
        Assert.That(ProtocolProfile.Gms2.Is(0x30, Inbound.UserSkinColor), Is.False);   // 0x30 is UserBattle there
        Assert.That(ProtocolProfile.Gms2.Is(0x30, Inbound.UserBattle));
    }
}
