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
}
