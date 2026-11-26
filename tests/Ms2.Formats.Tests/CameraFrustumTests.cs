using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The field camera's frustum (<see cref="CameraFrustum"/>): NiCamera::SetViewFrustum's near-plane clamp, and every camera
/// flat of both data sets, with MS2GameplayCamera's 10 / 100000 (not the camera modes' depth of field, 250 / 18000).
/// </summary>
public class CameraFrustumTests
{
    [TestCase(10f, 100000f, 0.1f, 10000f, 10f)]     // MS2GameplayCamera: far / ratio is exactly the near plane
    [TestCase(1f, 100000f, 0.1f, 10000f, 10f)]      // raised to far / ratio
    [TestCase(0.01f, 100f, 0.1f, 10000f, 0.1f)]     // raised to the minimum
    [TestCase(1f, 10000f, 0.1f, 65536f, 1f)]        // MS2TriggerCamera's values: kept
    public void TheNearPlaneClamp(float near, float far, float min, float ratio, float expected)
    {
        var f = CameraFrustum.Clamp(near, far, min, ratio);
        Assert.That(f.Near, Is.EqualTo(expected).Within(1e-6f));
        Assert.That(f.Far, Is.EqualTo(far));
    }

    [Test]
    public void NoFlatIsTheDefault()
    {
        Assert.That(CameraFrustum.Of(null), Is.Null);
        Assert.That(CameraFrustum.Of(new FlatModel { Name = "x" }), Is.Null);
        var f = new FlatModel { Name = "x" };
        f.Props["NearPlane"] = "1";
        f.Props["FarPlane"] = "100000";
        Assert.That(CameraFrustum.Of(f), Is.EqualTo(new CameraFrustum(10, 100000)), "NiCamera's own ratio 10000");
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryCameraFlat(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var flats = new FlatLibrary(packs);
        var cameras = new List<string>();
        foreach (var e in packs.List(XBlock.Archive).Where(e => e.StartsWith("flat/", StringComparison.OrdinalIgnoreCase)
                                                             && e.EndsWith(".flat", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var flat = flats.Get(Path.GetFileNameWithoutExtension(e));
            if (flat == null || !(flat.Mixins.Contains("Camera") || flat.Name.Equals("Camera", StringComparison.OrdinalIgnoreCase))) continue;
            var f = CameraFrustum.Of(flat);
            flat.Props.TryGetValue("FOV", out var fov);
            flat.Props.TryGetValue("DOFNear", out var dn);
            flat.Props.TryGetValue("DOFFar", out var df);
            cameras.Add($"{flat.Name}: frustum {(f is { } v ? $"{v.Near} / {v.Far}" : "none")} fov {fov} dof {dn} / {df}");
        }
        TestContext.Progress.WriteLine($"{env}: {cameras.Count} camera flats\n  " + string.Join("\n  ", cameras));
        Assert.That(cameras, Is.Not.Empty);
        Assert.That(CameraFrustum.Of(flats.Get(CameraFrustum.GameplayCamera)), Is.EqualTo(new CameraFrustum(10, 100000)));
        Assert.That(CameraFrustum.Gameplay(flats), Is.EqualTo(CameraFrustum.GameplayDefault));
    }
}
