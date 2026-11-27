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

    /// <summary>The fog range the shaders get under the field camera (10 / 100000), as SetFogRange, SetMaxFogValue and
    /// SCM_DEF_FOG_NEARFAR rebuild it: a range with far above near comes back as it was (02000110_bf's 3190 / 10000); near
    /// equal to far fogs from near to the camera's far (52000104_qd's 3590); near above far has its far end pushed far past
    /// the camera's (63000041_cs's 11000 / 10000: 1e-5 max fog value, factor 99999); a near past the camera's far puts
    /// both ends there.</summary>
    [TestCase(3190f, 10000f, 3190f, 10000f)]
    [TestCase(1000f, 10000f, 1000f, 10000f)]
    [TestCase(3590f, 3590f, 3590f, 100000f)]
    [TestCase(11000f, 10000f, 11000f, 8.9e9f)]
    [TestCase(150000f, 200000f, 100000f, 100000f)]
    public void TheShaderFogRange(float near, float far, float shaderNear, float shaderFar)
    {
        var (n, f) = CameraFrustum.GameplayDefault.ShaderFogRange(near, far);
        Assert.That(n, Is.EqualTo(shaderNear).Within(shaderNear * 1e-5f), "near");
        Assert.That(f, Is.EqualTo(shaderFar).Within(shaderFar * 1e-3f), "far");
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
