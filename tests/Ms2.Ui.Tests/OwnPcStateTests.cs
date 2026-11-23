using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class OwnPcStateTests
{
    [TestCase(6, 6, TestName = "Jump")]
    [TestCase(5, 5, TestName = "Fall")]
    [TestCase(27, 27, TestName = "Swim")]
    [TestCase(29, 29, TestName = "Climb")]
    [TestCase(1, 1, TestName = "Idle")]
    [TestCase(2, 2, TestName = "Walk")]
    public void AMoveStateIsItsOwnId(int move, int expected) =>
        Assert.That(OwnPcState.CurStateId(1, false, false, move), Is.EqualTo(expected));

    [Test]
    public void TheSkillStateIsSixteen() =>
        Assert.That(OwnPcState.CurStateId(0, false, true, 1), Is.EqualTo(0x10));

    [Test]
    public void RidingIsThirtyThree() =>
        Assert.That(OwnPcState.CurStateId(1, true, true, 6), Is.EqualTo(0x21));

    [Test]
    public void AServerStateIsKept() =>
        Assert.That(OwnPcState.CurStateId(0xc, false, true, 6), Is.EqualTo(0xc));

    /// <summary>The fall the server sends at every field entry held the state at 5 until the body's landing left it
    /// (MyCharacter.LeaveFallOnGround): until then the gates that want the standing state refused, the interact objects
    /// and npcs, the reactors and the ride item (StateAllowsInteraction) and the lift put-down (state 1).</summary>
    [Test]
    public void TheFieldEntryFallHoldsTheIdleGatesUntilTheBodyStands()
    {
        var me = new MyCharacter();
        var u = new FieldAddUser { ObjectId = 9, User = new UserBlock { Name = "a" } };
        u.Stats[0].Health = 500; u.Stats[2].Health = 500;
        me.Apply(u);
        me.ApplyUserState(9, 5);
        int falling = OwnPcState.CurStateId(me.State, false, false, 1);
        Assert.That((falling, LiveInteractObjects.StateAllowsInteraction(falling)), Is.EqualTo((5, false)), "the server's fall: refused");
        me.LeaveFallOnGround(true);
        int standing = OwnPcState.CurStateId(me.State, false, false, 1);
        Assert.That((standing, LiveInteractObjects.StateAllowsInteraction(standing)), Is.EqualTo((1, true)), "on the ground: the body's idle");
    }
}
