using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>A test that shows a modal and ends without hiding it must not leave the key layers to the next test of a fixture
/// that reads them.</summary>
[TestFixture, NonParallelizable]   // reads the process-wide key layers
public class KeyLayerResetTests
{
    [Test, Order(1)]
    public void ATestThatLeavesALayerHeld()
    {
        KeyInput.MoveAndAttack(false, 0x5151);
        Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld), Is.EqualTo((true, true)));
    }

    [Test, Order(2)]
    public void TheNextTestStartsWithNoLayer()
    {
        Assert.That((KeyInput.Shared.MoveHeld, KeyInput.Shared.AttackHeld, KeyInput.Shared.SkillBlocked), Is.EqualTo((false, false, false)));
        Assert.That(KeyInput.ActionKeyMode, Is.EqualTo(1));
    }

    [Test]
    public void ATestOfAFixtureThatRunsAloneIsReset()
        => Assert.That(KeyLayerResetAttribute.Resets(NUnit.Framework.Internal.TestExecutionContext.CurrentContext.CurrentTest), Is.True);
}

/// <summary>A fixture that runs in parallel (the assembly default) never has the shared layers released around its tests:
/// the sets are unlocked, and a release from one worker would take a layer another worker's dialog holds.</summary>
[TestFixture]
public class KeyLayerNoResetInParallelFixtureTests
{
    [Test]
    public void ATestOfAParallelFixtureIsNotReset()
        => Assert.That(KeyLayerResetAttribute.Resets(NUnit.Framework.Internal.TestExecutionContext.CurrentContext.CurrentTest), Is.False);
}
