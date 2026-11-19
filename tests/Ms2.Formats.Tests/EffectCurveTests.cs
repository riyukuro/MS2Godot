using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The effect curves an EffectInstance evaluates each frame (node transforms, colour, alpha, uv, flip, emitter
/// rate and on, force on) allocate nothing once warmed up.</summary>
public class EffectCurveTests
{
    const string Archive = "Resource/Model/Effect";

    [Test]
    public void FrameCurvesAllocateNothing()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA"); var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nx)) Assert.Ignore("MS2_DATA / MS2_NIFXML not set");
        var spec = new NifSpec(nx); var packs = new PackSet(data);
        var curves = new List<Func<double, double>>();
        int t = 0, r = 0, s = 0;
        static double L(TimeCtl c, double x) => c != null ? c.Local(x) : x;
        foreach (var entry in packs.List(Archive).Where(e => e.StartsWith("bg/common/") && e.EndsWith(".nif")).Order().Take(80))
        {
            var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry)));
            foreach (var n in sc.Nodes)
            {
                if (n.Anim == null) continue;
                var a = n.Anim; var c = n.AnimCtl;
                if (a.T != null) { t++; curves.Add(x => a.T(L(c, x)).X); }
                if (a.R != null) { r++; curves.Add(x => a.R(L(c, x)).W); }
                if (a.S != null) { s++; curves.Add(x => a.S(L(c, x))); }
            }
            foreach (var m in sc.Meshes)
            {
                foreach (var ca in m.Colors) curves.Add(x => ca.Curve(L(ca.Ctl, x))[0]);
                if (m.Alpha != null) curves.Add(m.Opacity);
                foreach (var op in m.Uv) curves.Add(x => op.Curve(L(op.Ctl, x)));
                if (m.Flip?.Curve != null) { var f = m.Flip; curves.Add(x => f.Curve(L(f.Ctl, x))); }
            }
            foreach (var ps in sc.Systems)
            {
                foreach (var e in ps.Emitters)
                {
                    if (e.Rate != null) curves.Add(x => e.Rate(L(e.RateCtl, x)));
                    if (e.On != null) curves.Add(x => e.On(L(e.OnCtl, x)));
                }
                foreach (var f in ps.Forces) if (f.On != null) curves.Add(x => f.On(L(f.OnCtl, x)));
            }
        }
        Assert.That(t, Is.GreaterThan(0)); Assert.That(r, Is.GreaterThan(0)); Assert.That(s, Is.GreaterThan(0));
        double sum = 0;
        void Frame(double x) { foreach (var f in curves) sum += f(x); }
        for (int i = 0; i < 100; i++) Frame(i / 60.0);      // warm up (JIT, lazy curve state)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) Frame(i / 60.0);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Progress.WriteLine($"{curves.Count} curves ({t} T, {r} R, {s} S), {bytes} B over 600 frames ({sum:F1})");
        Assert.That(bytes, Is.EqualTo(0));
    }
}
