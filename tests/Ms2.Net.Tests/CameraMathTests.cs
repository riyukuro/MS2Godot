using NUnit.Framework;
namespace Ms2.Net.Tests;

public class CameraMathTests {
    static void Is3(float x, float y, float z, (float, float, float) e) {
        Assert.That(x, Is.EqualTo(e.Item1).Within(0.01)); Assert.That(y, Is.EqualTo(e.Item2).Within(0.01)); Assert.That(z, Is.EqualTo(e.Item3).Within(0.01));
    }

    [Test] public void DefaultFieldCameraGivesSniffedView() {
        // map 2000001 has no <camera> in fielddata, so hangle 45 / vangle 30 apply: the placed eye relative to the look point (MS2 axes, z up) is (-777.8, -777.8, 952.6)
        // (live run); the sniffs carry (90, 45, -40) on that map and on ~40 others, the int16 field truncating -40.89
        var (x, y, z) = CameraMath.Euler(-777.8, -777.8, 952.6, 0, 0, 0);
        Is3(x, y, z, (90f, 45f, -40.89f));
        Assert.That((short) z, Is.EqualTo((short) -40));
    }

    [Test] public void HangleZeroCameraGivesSniffedView() {
        // maps with <camera hangle="0" vangle="50"> carry (57, 90, 0) in the sniffs (2000290); the Field places the eye at (d sin yaw, d cos yaw, d cos pitch), yaw 180, pitch 50
        var (x, y, z) = CameraMath.Euler(0, -1, Math.Cos(50 * Math.PI / 180), 0, 0, 0);
        Is3(x, y, z, (57.27f, 90f, 0f));
        // vangle 90 (map 61000004): (90, 90, 0)
        (x, y, z) = CameraMath.Euler(0, -1, 0, 0, 0, 0);
        Is3(x, y, z, (90f, 90f, 0f));
    }
}
