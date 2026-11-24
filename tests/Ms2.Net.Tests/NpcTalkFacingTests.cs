using System.Numerics;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The talk branch of CNpc::ApplySyncBlock and PCBase::LookAtPoint's yaw (NpcTalkFacing).</summary>
public class NpcTalkFacingTests
{
    const byte Talk = NpcTalkFacing.TalkState;

    [Test]
    public void ATalkBlockOnATalkingNpcLooksAtThePlayerOnlyWithTheFlag()
    {
        Assert.That(NpcTalkFacing.OnBlock(Talk, Talk, NpcTalkFacing.Talking, true, true), Is.EqualTo(NpcTalkFacing.Step.LookAtPlayer));
        Assert.That(NpcTalkFacing.OnBlock(Talk, Talk, NpcTalkFacing.Talking, true, false), Is.EqualTo(NpcTalkFacing.Step.Keep));
        // phase 0x49 without a talk returns; with a talk the state is entered again (its phase becomes 0x48)
        Assert.That(NpcTalkFacing.OnBlock(Talk, Talk, NpcTalkFacing.Idle, false, true), Is.EqualTo(NpcTalkFacing.Step.Keep));
        Assert.That(NpcTalkFacing.OnBlock(Talk, Talk, NpcTalkFacing.Idle, true, true), Is.EqualTo(NpcTalkFacing.Step.Enter));
        Assert.That(NpcTalkFacing.OnBlock(Talk, Talk, NpcTalkFacing.Talking, false, true), Is.EqualTo(NpcTalkFacing.Step.Enter));
        // an npc not yet in the talk state enters it; other states are not the talk branch
        Assert.That(NpcTalkFacing.OnBlock(Talk, 0x01, 0, true, true), Is.EqualTo(NpcTalkFacing.Step.Enter));
        Assert.That(NpcTalkFacing.OnBlock(0x01, Talk, NpcTalkFacing.Talking, true, true), Is.EqualTo(NpcTalkFacing.Step.Enter));
        Assert.That(NpcTalkFacing.EnterPhase(true), Is.EqualTo(NpcTalkFacing.Talking));
        Assert.That(NpcTalkFacing.EnterPhase(false), Is.EqualTo(NpcTalkFacing.Idle));
    }

    [Test]
    public void TheLookYawFacesTheTarget()
    {
        // the client's yaw r faces (sin r, -cos r): -y is 0, +x is 90, +y is 180, -x is 270
        var at = new Vector3(100, 200, 50);
        Assert.That(NpcTalkFacing.LookYaw(at, at + new Vector3(0, -30, 9)), Is.EqualTo(0f).Within(1e-3));
        Assert.That(NpcTalkFacing.LookYaw(at, at + new Vector3(30, 0, 0)), Is.EqualTo(90f).Within(1e-3));
        Assert.That(NpcTalkFacing.LookYaw(at, at + new Vector3(0, 30, 0)), Is.EqualTo(180f).Within(1e-3));
        Assert.That(NpcTalkFacing.LookYaw(at, at + new Vector3(-30, 0, 0)), Is.EqualTo(270f).Within(1e-3));
        Assert.That(NpcTalkFacing.LookYaw(at, at + new Vector3(30, -30, 0)), Is.EqualTo(45f).Within(1e-3));
        Assert.That(NpcTalkFacing.LookYaw(at, at with { Z = 500 }), Is.EqualTo(0f));
    }
}
