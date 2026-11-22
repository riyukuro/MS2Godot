using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>calc_msiR / calc_rmsiR against the client chunk (luapack64.o run by Lua 5.1: -5, 0 and 7 give 0.07, 8 gives 0.08,
/// 100 gives 1, 123 gives 1.23, 170 and 250 give 1.7, the same for both).</summary>
public class MoveSpeedRateTests {
    [TestCase(-5, 0.07)]
    [TestCase(0, 0.07)]
    [TestCase(7, 0.07)]
    [TestCase(8, 0.08)]
    [TestCase(100, 1.0)]
    [TestCase(123, 1.23)]
    [TestCase(170, 1.7)]
    [TestCase(250, 1.7)]
    public void ClipsTheStatOverAHundred(long stat, double rate) {
        Assert.That(MoveSpeedRate.Pc(stat), Is.EqualTo((float)rate));
        Assert.That(MoveSpeedRate.Ridee(stat), Is.EqualTo((float)rate));
    }
}
