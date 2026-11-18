using NUnit.Framework;
namespace Ms2.Net.Tests;

public class CameraMathTests {
    [Test] public void SniffedDefaultView() {
        // an eye at 45 degrees elevation with the Field's eye convention (d sin yaw, d cos yaw) at yaw 310 (hAngle 130 + 180), MS2 axes, z up gives the sniffs' constant camera field (90, 45, -40)
        double y = 310 * Math.PI / 180;
        var (x, ey, z) = CameraMath.Euler(Math.Sin(y), Math.Cos(y), 1, 0, 0, 0);
        Assert.That((x, ey, z), Is.EqualTo((90f, 45f, -40f)).Using<(float, float, float)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 0.01 && Math.Abs(a.Item2 - b.Item2) < 0.01 && Math.Abs(a.Item3 - b.Item3) < 0.01 ? 0 : 1));
    }

    [Test] public void LevelViewHasNoElevation() {
        var (x, y, _) = CameraMath.Euler(0, 100, 0, 0, 0, 0);
        Assert.That(x, Is.EqualTo(90f).Within(0.01)); Assert.That(y, Is.EqualTo(0f).Within(0.01));
    }
}
