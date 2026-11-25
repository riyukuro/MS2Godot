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

    [Test]
    public void FireSheetIsRead()
    {
        // bg/common/eff_bg_fire_a.nif PArray02: an aligned system with Has Animated Textures, its generator a 4 x 2 sheet
        // played once over the life (frames 0 to 8)
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "bg/common/eff_bg_fire_a.nif")));
        var d = sc.Systems.Single(s => s.Name == "PArray02");
        Assert.That((d.Aligned, d.AnimatedTextures, d.FramesAcross, d.FramesDown, d.InitialFrame, d.NumFrames), Is.EqualTo((true, true, 4, 2, 0, 8)));
        Assert.That((d.InitialTime, d.FinalTime, d.PingPong, d.UVScrolling), Is.EqualTo((0.0, 1.0, false, false)));
        var t = new AlignedQuad.TexAnim(d);
        Assert.That(t.On, Is.True);
        t.Cell(0.5f, 1f, 0f, out var u, out var v);
        Assert.That((u, v), Is.EqualTo((0f, 0.5f)), "half way: frame 4, the second row's first cell");
    }

    [Test]
    public void TexAnimIsTheKernels()
    {
        // 4 x 2 frames, frames 1 to 1 + 6 over the life's 0.2 to 0.8, variance 0
        var d = new ParticleSystemDef { Aligned = true, AnimatedTextures = true, FramesAcross = 4, FramesDown = 2, InitialFrame = 1, NumFrames = 6,
                                        InitialTime = 0.2, FinalTime = 0.8 };
        var t = new AlignedQuad.TexAnim(d);
        void At(float age, float var, float l, float top, string what)
        {
            t.Cell(age, 10f, var, out var u, out var v);
            Assert.That((u, v), Is.EqualTo((l, top)).Using<(float, float)>((a, b) => MathF.Abs(a.Item1 - b.Item1) < Eps && MathF.Abs(a.Item2 - b.Item2) < Eps), what);
        }
        At(0f, 0f, 0.25f, 0f, "before the initial time: the initial frame 1");
        At(5f, 0f, 0f, 0.5f, "half way: frame 4, the second row");
        At(7.9f, 0f, 0.5f, 0.5f, "frame 6.9: floored to 6");
        At(9f, 0f, 0.75f, 0.5f, "after the final time: frame 7 (1 + 6), the kernel's own end");
        At(0f, -0.5f, 0.25f, 0f, "variance -0.5 with no variation: frame 1");
        // a variation moves both bounds by floor(variance x variation)
        t = new AlignedQuad.TexAnim(new ParticleSystemDef { Aligned = true, AnimatedTextures = true, FramesAcross = 4, FramesDown = 2, InitialFrame = 1,
                                                            InitialFrameVar = 3, NumFrames = 6, InitialTime = 0.2, FinalTime = 0.8 });
        At(0f, 0.7f, 0.75f, 0f, "variance 0.7 x 3: frame 1 + 2 = 3");
        // ping pong: back from the final time
        t = new AlignedQuad.TexAnim(new ParticleSystemDef { Aligned = true, AnimatedTextures = true, FramesAcross = 4, FramesDown = 2, NumFrames = 4,
                                                            InitialTime = 0, FinalTime = 0.5, PingPong = true });
        At(2.5f, 0f, 0.5f, 0f, "first pass, half way: frame 2");
        At(7.5f, 0f, 0.5f, 0f, "second pass (reversed), half way back: frame 2");
        At(6f, 0f, 0.75f, 0f, "second pass at 1 s in: frame 3.2 floored to 3");
        // UV scrolling: no floors
        t = new AlignedQuad.TexAnim(new ParticleSystemDef { Aligned = true, AnimatedTextures = true, FramesAcross = 4, FramesDown = 1, NumFrames = 4,
                                                            InitialTime = 0, FinalTime = 1, UVScrolling = true });
        At(1.25f, 0f, 0.125f, 0f, "scrolling: frame 0.5 is half a cell along");
        Assert.That(new AlignedQuad.TexAnim(new ParticleSystemDef { AnimatedTextures = true, NumFrames = 4 }).On, Is.False, "a facing system has no texture kernel");
        Assert.That(new AlignedQuad.TexAnim(new ParticleSystemDef { Aligned = true, NumFrames = 4 }).On, Is.False, "nor a system without Has Animated Textures");
    }

    /// <summary>Every particle system of every effect and map nif, both data sets: the texture animation fields and what
    /// the kernel does with them.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryAnimatedTextureSystem(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var marker = Encoding.ASCII.GetBytes("NiPSParticleSystem");
        int nifs = 0, systems = 0, animated = 0, animatedFacing = 0, flipping = 0, pingPong = 0, scrolling = 0, varied = 0, zeroSpan = 0, badGrid = 0;
        var examples = new List<string>(); var loadErrors = new List<string>();
        foreach (var archive in new[] { "Resource/Model/Effect", "Resource/Model/Map" })
            foreach (var entry in packs.List(archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
            {
                var bytes = packs.Read(archive, entry);
                if (bytes.AsSpan().IndexOf(marker) < 0) continue;
                EffectScene sc;
                try { sc = EffectScene.Load(NifFile.Load(spec, bytes)); }
                catch (NotSupportedException) { continue; }
                catch (ArgumentOutOfRangeException e) { loadErrors.Add($"{entry}: {e.TargetSite?.Name}"); continue; }
                nifs++;
                foreach (var d in sc.Systems)
                {
                    systems++;
                    if (!d.AnimatedTextures) continue;
                    if (!d.Aligned) { animatedFacing++; continue; }
                    animated++;
                    if (d.FramesAcross < 1 || d.FramesDown < 1) badGrid++;
                    if (d.NumFrames != 0 || d.NumFramesVar != 0) flipping++;
                    if (d.PingPong) pingPong++;
                    if (d.UVScrolling) scrolling++;
                    if (d.InitialFrameVar != 0 || d.NumFramesVar != 0) varied++;
                    if (d.FinalTime == d.InitialTime) { zeroSpan++; if (examples.Count < 20) examples.Add($"zero span: {entry} {d.Name} pingpong {d.PingPong}"); }
                    if (examples.Count < 20 && d.NumFrames > 1 && d.FramesAcross > 1)
                        examples.Add($"{entry} {d.Name}: {d.FramesAcross}x{d.FramesDown} frames {d.InitialFrame}+{d.NumFrames} var {d.InitialFrameVar}/{d.NumFramesVar} "
                                     + $"time {d.InitialTime}-{d.FinalTime}{(d.PingPong ? " pingpong" : "")}{(d.UVScrolling ? " scroll" : "")}");
                }
            }
        TestContext.Out.WriteLine($"{env}: {nifs} nifs with particles, {systems} systems; {animated} aligned with animated textures ({flipping} with frames, "
            + $"{pingPong} ping pong, {scrolling} uv scrolling, {varied} with a variation, {zeroSpan} with initial = final time, {badGrid} with a zero grid); "
            + $"{animatedFacing} facing systems with the flag (no texture kernel)");
        foreach (var x in examples) TestContext.Out.WriteLine("  " + x);
        TestContext.Out.WriteLine($"  {loadErrors.Count} nifs the effect loader throws on: " + string.Join(", ", loadErrors.Take(10)));
        Assert.That(badGrid, Is.Zero);
        Assert.That(animated, Is.GreaterThan(10));
    }
}
