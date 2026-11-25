using System.Numerics;
using System.Text;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The NiPSAlignedQuadGenerator quads (AlignedQuad). The healing spot's flames (bg/common/eff_com_recovery.nif,
/// PArray01, region skill 70000018) are a world-space system with Normal Method and Up Method ALIGN_LOCAL_FIXED, normal
/// direction zero and up direction +Z: the client draws them as fixed upright planes facing the file's X axis. The effect
/// runtime drew every aligned system as a camera-facing quad stretched along the velocity, so the flames' clamped texture
/// showed its square edges from every side.</summary>
public class AlignedQuadTests
{
    const float Eps = 1e-5f;

    static void Near(Vector3 a, Vector3 b, string what) =>
        Assert.That(Vector3.Distance(a, b), Is.LessThan(1e-4f), $"{what}: {a} vs {b}");

    [Test]
    public void RecoveryFlamesAreFixedUprightPlanes()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "bg/common/eff_com_recovery.nif")));
        var d = sc.Systems.Single(s => s.Name == "PArray01");
        Assert.That(d.Aligned && d.WorldSpace, Is.True);
        Assert.That((d.NormalMethod, d.UpMethod), Is.EqualTo((AlignedQuad.LocalFixed, AlignedQuad.LocalFixed)));
        Assert.That(d.NormalDirection, Is.EqualTo(new Vec3(0, 0, 0)));
        Assert.That(d.UpDirection, Is.EqualTo(new Vec3(0, 0, 1)));
        Assert.That(d.HasRotations, Is.False);
        // SubmitAlignMethods: the zero normal direction unitizes to UNIT_X, the up stays +Z
        var n = AlignedQuad.Unitize(Vector3.Zero, Vector3.UnitX);
        var u = AlignedQuad.Unitize(new Vector3(0, 0, 1), Vector3.UnitZ);
        AlignedQuad.Quad(n, u, 10f, 1f, 1f, (float)d.CenterU, (float)d.CenterV, 0f, out var x, out var y, out _);
        Near(x, new Vector3(0, 10, 0), "the quad's X axis runs along file Y");
        Near(y, new Vector3(0, 0, 10), "the quad's Y axis is file up");
        // and the other system (block 63, the sparks) follows the camera
        var cam = sc.Systems.Single(s => s.Aligned && s.Name != "PArray01");
        Assert.That((cam.NormalMethod, cam.UpMethod), Is.EqualTo((AlignedQuad.Camera, AlignedQuad.Camera)));
    }

    [Test]
    public void CornersAreTheKernels()
    {
        // NiPSAlignedQuadGeneratorKernelFF.cpp:82-110, the rotated corners, written out as the kernel does
        var normal = Vector3.Normalize(new Vector3(0.3f, -0.8f, 0.5f));
        var upIn = Vector3.Normalize(new Vector3(0.1f, 0.4f, 0.9f));
        float size = 7f, sU = 1.6f, sV = 0.7f, cU = 0.25f, cV = -0.4f, a = 0.9f;
        var right = AlignedQuad.UnitCross(upIn, normal);
        var up = Vector3.Cross(normal, right);
        float sin = MathF.Sin(a), cos = MathF.Cos(a), c1 = size * (cos + sin), c2 = size * (cos - sin);
        var v0 = c1 * sU * right + c2 * sV * up;
        var v1 = -c2 * sU * right + c1 * sV * up;
        float fcu = size * sU * cU, fcv = size * sV * cV;
        var rc = (fcv * sin + fcu * cos) * right + (fcv * cos - fcu * sin) * up;
        var p = new Vector3(5, 6, 7);
        AlignedQuad.Quad(normal, upIn, size, sU, sV, cU, cV, a, out var x, out var y, out var off);
        var o = p - off;
        Near(o - x + y, p + v1 - rc, "vertex 0 (top left)");
        Near(o - x - y, p - v0 - rc, "vertex 1");
        Near(o + x - y, p - v1 - rc, "vertex 2");
        Near(o + x + y, p + v0 - rc, "vertex 3 (top right)");
        // no rotation: :145-169
        AlignedQuad.Quad(normal, upIn, size, sU, sV, cU, cV, 0f, out x, out y, out off);
        var center = size * (up * sV * cV + right * sU * cU);
        Near(p - off - x + y, p + size * (up * sV - right * sU) - center, "unrotated top left");
        Near(p - off + x + y, p + size * (up * sV + right * sU) - center, "unrotated top right");
    }

    [Test]
    public void SpeedScaleIsTheKernels()
    {
        // amount 0.002 normalized over limit 3 - rest 1; (a v l + r) / (a v + 1) runs from rest at rest to the limit
        float a = AlignedQuad.NormalizedAmount(0.002, 3, 1);
        Assert.That(a, Is.EqualTo(0.001f).Within(Eps));
        Assert.That(AlignedQuad.SpeedScale(a, 3, 1, 0), Is.EqualTo(1f).Within(Eps));
        Assert.That(AlignedQuad.SpeedScale(a, 3, 1, 1000), Is.EqualTo(2f).Within(Eps));
        Assert.That(AlignedQuad.SpeedScale(a, 3, 1, 1e9f), Is.EqualTo(3f).Within(1e-3f));
        Assert.That(AlignedQuad.NormalizedAmount(5, 1, 1), Is.EqualTo(0f), "limit = rest: no speed scale");
        Assert.That(AlignedQuad.ScaleValid(new ParticleSystemDef { ScaleAmountV = 1, ScaleLimitV = 0.5, ScaleRestV = 1, ScaleLimitU = 1, ScaleRestU = 1 }), Is.False);
    }

    [Test]
    public void PerParticleInputs()
    {
        var fixedN = Vector3.UnitX; var fixedU = Vector3.UnitZ;
        var rel = new Vector3(0, 3, 0); var vel = new Vector3(0, 0, -2);
        // VF with the position: the normal is the unitized position, the up stays fixed
        AlignedQuad.Inputs(AlignedQuad.LocalPosition, AlignedQuad.LocalFixed, false, fixedN, fixedU, rel, vel, Vector3.UnitX, Vector3.UnitZ, out var n, out var u);
        Near(n, Vector3.UnitY, "VF normal"); Near(u, fixedU, "VF up");
        // FV with the velocity: the up is the velocity as is
        AlignedQuad.Inputs(AlignedQuad.Camera, AlignedQuad.LocalVelocity, true, fixedN, fixedU, rel, vel, Vector3.UnitX, Vector3.UnitZ, out n, out u);
        Near(n, fixedN, "FV normal"); Near(u, vel, "FV up");
        // VV with rotations: zero inputs take UNIT_X / UNIT_Z; without, the normal goes to zero
        AlignedQuad.Inputs(AlignedQuad.LocalVelocity, AlignedQuad.LocalVelocity, true, fixedN, fixedU, rel, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, out n, out u);
        Near(n, Vector3.UnitX, "VV rotated normal default"); Near(u, Vector3.UnitZ, "VV rotated up default");
        AlignedQuad.Inputs(AlignedQuad.LocalVelocity, AlignedQuad.LocalVelocity, false, fixedN, fixedU, rel, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, out n, out u);
        Near(n, Vector3.Zero, "VV normal"); Near(u, Vector3.Zero, "VV up");
    }

    /// <summary>Every aligned system of every effect and map nif, both data sets: the alignment methods are ones the kernels
    /// take (ALIGN_LOCAL_FIXED, ALIGN_LOCAL_POSITION, ALIGN_LOCAL_VELOCITY, ALIGN_CAMERA) and the counts per shape.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryAlignedSystemHasAKernel(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var marker = Encoding.ASCII.GetBytes("NiPSAlignedQuadGenerator");
        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        int nifs = 0, systems = 0, refused = 0, centered = 0, speedScaled = 0;
        int[] ok = { AlignedQuad.LocalFixed, AlignedQuad.LocalPosition, AlignedQuad.LocalVelocity, AlignedQuad.Camera };
        foreach (var archive in new[] { "Resource/Model/Effect", "Resource/Model/Map" })
            foreach (var entry in packs.List(archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
            {
                var bytes = packs.Read(archive, entry);
                if (bytes.AsSpan().IndexOf(marker) < 0) continue;
                nifs++;
                EffectScene sc;
                try { sc = EffectScene.Load(NifFile.Load(spec, bytes)); }
                catch (NotSupportedException) { continue; }
                foreach (var d in sc.Systems.Where(s => s.Aligned))
                {
                    systems++;
                    if (!ok.Contains(d.NormalMethod) || !ok.Contains(d.UpMethod)) bad.Add($"{entry} {d.Name}: methods {d.NormalMethod}/{d.UpMethod}");
                    if (!AlignedQuad.ScaleValid(d)) refused++;
                    if (d.CenterU != 0 || d.CenterV != 0) centered++;
                    if (d.ScaleAmountU != 0 || d.ScaleAmountV != 0) speedScaled++;
                    static string Dir(Vec3 v) => v == default ? "(0)" : $"({v.X:0.##},{v.Y:0.##},{v.Z:0.##})";
                    string key = $"N{d.NormalMethod}{(d.NormalMethod == AlignedQuad.LocalFixed ? Dir(d.NormalDirection) : "")} "
                        + $"U{d.UpMethod}{(d.UpMethod == AlignedQuad.LocalFixed ? Dir(d.UpDirection) : "")} "
                        + $"{(d.WorldSpace ? "world" : "local")} {(d.HasRotations ? "rot" : "norot")}";
                    shapes[key] = shapes.GetValueOrDefault(key) + 1;
                }
            }
        TestContext.Out.WriteLine($"{env}: {nifs} nifs with aligned quads, {systems} aligned systems; {speedScaled} speed scaled, "
            + $"{centered} with a pivot, {refused} refused by the generator");
        foreach (var (k, c) in shapes.OrderByDescending(p => p.Value)) TestContext.Out.WriteLine($"  {c,5} {k}");
        foreach (var x in bad.Take(40)) TestContext.Out.WriteLine("  " + x);
        Assert.That(bad, Is.Empty);
        Assert.That(systems, Is.GreaterThan(100));
    }
}
