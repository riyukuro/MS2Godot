using Ms2.Net;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// StateConsumeEp (KMS 0x64): the client's handler is CSceneStateGame::OnStateConsumeEp (scenestategame.cpp:1606-1607 and
/// :5799-5810): one i32, an absolute current stat 10 (EP), clamped to [0, total] (pcbase.cpp:2375-2386). No recording of it
/// exists in the corpus, so the bytes here are the client read, not a sample. It has no GMS2 number.
/// </summary>
public class RcStateConsumeEpTests
{
    static StateConsumeEp Read(params byte[] body) => StateConsumeEp.Read(new ByteReader(body));

    [Test]
    public void KmsStateConsumeEpReadsTheAbsoluteEp() => Assert.That(Read(0x19, 0, 0, 0).Ep, Is.EqualTo(25));

    [Test]
    public void KmsStateConsumeEpIsOpcode64() => Assert.That((ushort)Inbound.StateConsumeEp, Is.EqualTo(0x64));

    [Test]
    public void TheEpIsSetAbsoluteAndClampedToTheTotal()
    {
        var me = new MyCharacter();
        me.Stats0[PlayerStat.Stamina] = 120; me.Stats2[PlayerStat.Stamina] = 120;
        int changed = 0; me.Changed += () => changed++;
        Assert.That(me.Apply(Read(0x19, 0, 0, 0)), Is.EqualTo(25));
        Assert.That(me.Stats2[PlayerStat.Stamina], Is.EqualTo(25), "absolute, not a delta");
        Assert.That(me.Apply(Read(0xe8, 0x03, 0, 0)), Is.EqualTo(120), "1000 clamps at the total");
        Assert.That(me.Apply(Read(0xff, 0xff, 0xff, 0xff)), Is.EqualTo(0), "a negative value clamps at 0");
        Assert.That(changed, Is.EqualTo(3));
    }
}
