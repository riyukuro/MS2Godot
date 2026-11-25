using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Unk_PCMove::FUN_141683ef0, the own pc's move gate (unit_141682f80.cpp:495-518).</summary>
public class MoveGateTests
{
    [Test]
    public void AnIdleLivingPcPasses() =>
        Assert.That(MoveGate.Passes(false, false, 0, false, 0), Is.True);

    [Test]
    public void EachCheckClosesTheGate()
    {
        Assert.That(MoveGate.Passes(true, false, 0, false, 0), Is.False, "dead");
        Assert.That(MoveGate.Passes(false, true, 0, false, 0), Is.False, "input held (text field, loading screen)");
        Assert.That(MoveGate.Passes(false, false, AutoFindTaxiStation.StateFlag, false, 0), Is.False, "a state flag (vfunc_63 0)");
        Assert.That(MoveGate.Passes(false, false, 0, true, 0), Is.False, "stunned");
        Assert.That(MoveGate.Passes(false, false, 0, false, 1), Is.False, "cube mode 1");
        Assert.That(MoveGate.Passes(false, false, 0, false, 0, cameraAllowsMove: false), Is.False, "the camera mode");
    }

    [Test]
    public void OnlyCubeModeOneClosesIt()
    {
        Assert.That(MoveGate.Passes(false, false, 0, false, 2), Is.True);
        Assert.That(MoveGate.CanAct(0), Is.True);
        Assert.That(MoveGate.CanAct(4 | 0x10), Is.False);
    }
}
