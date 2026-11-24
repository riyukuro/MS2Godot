using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>NpcStates against CNpc's registered states (unit_14108e1e0_npcactorandvibrate.cpp:479-490) and the sync block's change.</summary>
public class NpcStatesTests
{
    [Test]
    public void TheNpcHasTwelveStates()
    {
        var have = new System.Collections.Generic.List<int>();
        for (int s = 0; s < 256; s++) if (NpcStates.Registered((byte)s)) have.Add(s);
        Assert.That(have, Is.EqualTo(new[] { 1, 2, 6, 0xc, 0xd, 0x10, 0x11, 0x12, 0x16, 0x17, 0x23, 0x38 }));
    }

    [Test]
    public void ABlockEntersNoneNorStunNorAStateTheNpcLacks()
    {
        Assert.That(NpcStates.BlockEnters(0), Is.False);
        Assert.That(NpcStates.BlockEnters(0x12), Is.False);
        Assert.That(NpcStates.BlockEnters(0xb), Is.False);    // gosEmotion, a PC state
        Assert.That(NpcStates.BlockEnters(0xf), Is.False);
        Assert.That(NpcStates.BlockEnters(1), Is.True);
        Assert.That(NpcStates.BlockEnters(0x17), Is.True);
    }
}
