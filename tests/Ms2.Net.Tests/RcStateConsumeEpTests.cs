using Ms2.Net;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// StateConsumeEp (KMS 0x64): the client's handler is CSceneStateGame::OnStateConsumeEp (scenestategame.cpp:1606-1607 and
/// :5799-5810): one i32, an absolute current stat 10 (EP). No recording of it exists in the corpus, so the bytes here are the
/// client read, not a sample. Expected reader: Ms2.Net.Packets.StateConsumeEp.Read, one i32.
/// </summary>
public class RcStateConsumeEpTests
{
    [Ignore("StateConsumeEp is not read yet")]
    [Test]
    public void KmsStateConsumeEpReadsTheAbsoluteEp()
    {
        var p = RcLane.Read("StateConsumeEp", RcLane.Bytes("19000000"));
        long ep = p is IConvertible c && p.GetType().IsPrimitive ? Convert.ToInt64(c) : RcLane.Long(p, "Ep", "Value");
        Assert.That(ep, Is.EqualTo(25L));
    }

    [Ignore("StateConsumeEp is not mapped yet")]
    [Test]
    public void KmsStateConsumeEpIsOpcode64()
    {
        Assert.That((ushort)RcLane.EnumMember<Inbound>("StateConsumeEp"), Is.EqualTo(0x64));
    }
}
