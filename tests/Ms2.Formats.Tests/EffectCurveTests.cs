using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The effect curves an EffectInstance evaluates each frame (node transforms, colour, alpha, uv, flip, emitter
/// rate and on, force on, and the material colour terms of EffectMesh.Shade) allocate nothing once warmed up.</summary>
public class EffectCurveTests
{
    const string Archive = "Resource/Model/Effect";

    [Test]
    public void FrameCurvesAllocateNothing()
    {
        var (packs, spec) = NifEnv.Open();
        var curves = new List<Func<double, double>>();
        int t = 0, r = 0, s = 0, col = 0, alpha = 0, uv = 0, flip = 0, rate = 0, on = 0, force = 0, shade = 0;
        double[] lit = new double[3], amb = new double[3];
        static double L(TimeCtl c, double x) => c != null ? c.Local(x) : x;
        // the first 80 bg/common effects, then further effects (in name order) until every curve kind below is in the set
        int files = 0;
        bool All() => t > 0 && r > 0 && s > 0 && col > 0 && alpha > 0 && uv > 0 && flip > 0 && rate > 0 && on > 0 && force > 0;
        var entries = packs.List(Archive).Where(e => e.EndsWith(".nif")).OrderBy(e => e.StartsWith("bg/common/") ? 0 : 1).ThenBy(e => e, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (files >= 80 && All() || files >= 2000) break;
            files++;
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
                foreach (var ca in m.Colors) { col++; curves.Add(x => ca.Curve(L(ca.Ctl, x))[0]); }
                if (m.Colors.Count > 0) { shade++; var em = m; curves.Add(x => { em.Shade(x, lit, amb); return lit[0] + amb[0]; }); }
                if (m.Alpha != null) { alpha++; curves.Add(m.Opacity); }
                foreach (var op in m.Uv) { uv++; curves.Add(x => op.Curve(L(op.Ctl, x))); }
                if (m.Flip?.Curve != null) { flip++; var f = m.Flip; curves.Add(x => f.Curve(L(f.Ctl, x))); }
            }
            foreach (var ps in sc.Systems)
            {
                foreach (var e in ps.Emitters)
                {
                    if (e.Rate != null) { rate++; curves.Add(x => e.Rate(L(e.RateCtl, x))); }
                    if (e.On != null) { on++; curves.Add(x => e.On(L(e.OnCtl, x))); }
                }
                foreach (var f in ps.Forces) if (f.On != null) { force++; curves.Add(x => f.On(L(f.OnCtl, x))); }
            }
        }
        // every curve kind the frame evaluates is in the set
        foreach (var (kind, k) in new[] { ("T", t), ("R", r), ("S", s), ("colour", col), ("shade", shade), ("alpha", alpha), ("uv", uv), ("flip", flip),
                     ("emit rate", rate), ("emitter on", on), ("force on", force) })
            Assert.That(k, Is.GreaterThan(0), kind);
        double sum = 0;
        void Frame(double x) { foreach (var f in curves) sum += f(x); }
        for (int i = 0; i < 100; i++) Frame(i / 60.0);      // warm up (JIT, lazy curve state)
        // the least of up to 5 windows of 600 frames (Alloc.Least)
        long bytes = Alloc.Least(() => { for (int i = 0; i < 600; i++) Frame(i / 60.0); });
        TestContext.Progress.WriteLine($"{files} files, {curves.Count} curves ({t} T, {r} R, {s} S, {col} colour, {alpha} alpha, {uv} uv, {flip} flip, {rate} rate, {on} on, {force} force), {bytes} B in the least 600-frame window ({sum:F1})");
        Assert.That(bytes, Is.EqualTo(0));
    }
}
