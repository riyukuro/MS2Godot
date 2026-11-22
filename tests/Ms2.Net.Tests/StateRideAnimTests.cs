using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class StateRideAnimTests
{
    static readonly RiderSequences S = new("pc", "idle", "run", "jump", "spIdle", "spRun", "spJump");

    [Test]
    public void WalkAndSwimRunWithTheSpRunForItsSubs()
    {
        foreach (int st in new[] { RemotePcStates.StateWalk, RemotePcStates.StateSwim })
        {
            Assert.That(StateRideAnim.For(st, 0, S), Is.EqualTo("run"));
            Assert.That(StateRideAnim.For(st, 0x17, S), Is.EqualTo("spRun"));
            Assert.That(StateRideAnim.For(st, 0x85, S), Is.EqualTo("spRun"));
            Assert.That(StateRideAnim.For(st, 0x27, S), Is.EqualTo("run"));
        }
    }

    [Test]
    public void JumpLandAndIdle()
    {
        Assert.That(StateRideAnim.For(RemotePcStates.StateJump, 0, S), Is.EqualTo("jump"));
        Assert.That(StateRideAnim.For(RemotePcStates.StateJump, 0x27, S), Is.EqualTo("spJump"));
        // land stands whatever its sub
        Assert.That(StateRideAnim.For(RemotePcStates.StateLand, 0x27, S), Is.EqualTo("idle"));
        Assert.That(StateRideAnim.For(RemotePcStates.StateIdle, 0, S), Is.EqualTo("idle"));
        Assert.That(StateRideAnim.For(RemotePcStates.StateIdle, 0xc, S), Is.EqualTo("spIdle"));
        Assert.That(StateRideAnim.For(RemotePcStates.StateIdle, 0x87, S), Is.EqualTo("spIdle"));
    }

    [Test]
    public void OtherStatesPlayRideAniPc()
    {
        // the fall (5) is not in StateRide::vfunc_9's switch
        Assert.That(StateRideAnim.For(RemotePcStates.StateFall, 0, S), Is.EqualTo("pc"));
        Assert.That(StateRideAnim.For(RemotePcStates.StateSkill, 0x17, S), Is.EqualTo("pc"));
    }

    [Test]
    public void PassengerStandsOnTheLandingWithTheSpIdleAndFallsBackByM_unkB80()
    {
        // StateHold::vfunc_9 (stateholddata.cpp:309-370): land (4) shares idle's branch and its SP subs
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateLand, 0xc, false, S), Is.EqualTo("spIdle"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateLand, 0, false, S), Is.EqualTo("idle"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateIdle, 0x87, false, S), Is.EqualTo("spIdle"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateWalk, 0x85, false, S), Is.EqualTo("spRun"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateSwim, 0, false, S), Is.EqualTo("run"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateJump, 0x27, false, S), Is.EqualTo("spJump"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateJump, 0, false, S), Is.EqualTo("jump"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateFall, 0, false, S), Is.EqualTo("pc"));
        Assert.That(StateRideAnim.ForPassenger(RemotePcStates.StateFall, 0, true, S), Is.EqualTo("spIdle"));
    }
}
