using Ms2.Avm2.Flash;
using NUnit.Framework;

/// <summary>
/// Every movie of the Ui suite runs on its frame clock (<see cref="Player.TestClock"/>): the tests drive movies by Tick, so
/// getTimer (real time in the game, as in the client) and the double click follow the ticks they run, and a run gives the
/// same frames every time. A test that needs its own clock still sets <see cref="Player.Clock"/>.
/// In the global namespace so it covers every fixture of the assembly.
/// </summary>
[SetUpFixture]
public sealed class FrameClockSetUp
{
    [OneTimeSetUp]
    public void UseTheFrameClock() => Player.TestClock = p => () => p.RT.TimerMs;

    [OneTimeTearDown]
    public void UseTheRealClockAgain() => Player.TestClock = null;
}
