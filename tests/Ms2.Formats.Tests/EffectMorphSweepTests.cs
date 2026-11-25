using System.Text;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Every NiMorphMeshModifier of every effect nif (Resource/Model/Effect), in both data sets: what the data asks of the
/// effect runtime and that the runtime takes every morph that moves. A mesh's morph moves when its NiMorphWeightsController
/// runs (ACTIVE, not MANAGERCONTROLLED, a LOOP or CLAMP cycle) and a target's weight has keys; then the effect scene gives
/// the drawn mesh an <see cref="EffectMorph"/>, whose weights follow the controller's time as the field shader's keys do
/// (field_morph.gdshaderinc: NiTimeController::ComputeScaledTime, then NiFloatKey linear or Bezier), and EffectInstance
/// draws its offset targets (at most 4, the custom arrays). A helper mesh (APP_CULLED) is not drawn, by the client either.
/// </summary>
public class EffectMorphSweepTests
{
    const string Archive = "Resource/Model/Effect";

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryMovingMorphOfEveryEffectNifIsPlayed(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var marker = Encoding.ASCII.GetBytes("NiMorphMeshModifier");
        int nifs = 0, withMorph = 0, meshes = 0, drawn = 0, culled = 0, fixedWeights = 0, stopped = 0, samples = 0;
        var bad = new List<string>();
        var overTargets = new SortedSet<string>();
        var noScene = new SortedSet<string>();
        var drawnMorphs = new List<EffectMorph>();
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
        {
            nifs++;
            var bytes = packs.Read(Archive, entry);
            if (bytes.AsSpan().IndexOf(marker) < 0) continue;
            withMorph++;
            var nif = NifFile.Load(spec, bytes);
            EffectScene sc;
            // a scene that does not load plays through CombatLayer's fallback, morph or not (SceneFallbacks)
            try { sc = EffectScene.Load(nif); }
            catch (NotSupportedException ex) { noScene.Add($"{entry}: {ex.Message}"); continue; }
            if (sc.Unsupported.Contains("NiMorphWeightsController")) bad.Add($"{entry}: the morph controller is listed unsupported");
            var B = nif.Blocks;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (b.Type != "NiMesh") continue;
                var mods = b.Ints("Modifiers").Where(m => m >= 0).Select(m => B[(int)m]).ToList();
                var mod = mods.FirstOrDefault(m => m.Type == "NiMorphMeshModifier");
                if (mod == null) continue;
                meshes++;
                string at = $"{entry} {nif.Name(i)}";
                // the shapes ReadMorph takes: relative targets, no normal update, one POSITION element, not skinned
                long flags = mod.IntOr("Flags", 0);
                var el = mod.Structs("Elements");
                if ((flags & 1) == 0 || (flags & 2) != 0 || el.Length != 1 || nif.StrField(el[0].Struct("Semantic"), "Name") != "POSITION"
                    || mods.Any(m => m.Type == "NiSkinningMeshModifier"))
                    bad.Add($"{at}: a morph shape the reader does not take (flags {flags}, {el.Length} elements)");
                NifStruct ctl = null;
                for (long c = b.IntOr("Controller", -1), g = 0; c >= 0 && g < 16 && ctl == null; g++)
                {
                    var cb = B[(int)c];
                    if (cb.Type == "NiMorphWeightsController") ctl = cb;
                    c = cb.IntOr("Next Controller", -1);
                }
                long cf = ctl?.IntOr("Flags", 0) ?? 0;
                bool runs = ctl != null && (cf & 8) != 0 && (cf & 0x20) == 0 && ((cf >> 1) & 3) is 0 or 2;
                bool keyed = runs && ctl.Ints("Interpolators").Skip(1).Any(ip => ip >= 0 && B[(int)ip].Type == "NiFloatInterpolator"
                    && B[(int)ip].IntOr("Data", -1) is var di && di >= 0 && B[(int)di].Struct("Data").Structs("Keys").Length > 0);
                if (!runs) { stopped++; continue; }
                if (!keyed) { fixedWeights++; continue; }
                if ((b.IntOr("Flags", 0) & 1) != 0) { culled++; continue; }
                var em = sc.Meshes.FirstOrDefault(m => m.Mesh.Block == i);
                if (em?.Morph == null) { bad.Add($"{at}: a keyed running morph the effect scene does not play"); continue; }
                drawn++;
                drawnMorphs.Add(em.Morph);
                if (em.Morph.Anim.Deltas.Length > 4) { overTargets.Add($"{entry} ({em.Morph.Anim.Deltas.Length} offset targets)"); continue; }
                samples += CheckWeights(em.Morph, at, bad);
            }
        }
        TestContext.Out.WriteLine($"{env}: {nifs} effect nifs, {withMorph} with a morph modifier, {meshes} morph meshes: {drawn} drawn with their morph "
            + $"({samples} weight samples checked), {culled} helper meshes (APP_CULLED), {fixedWeights} with fixed weights only, {stopped} whose controller does not run");
        foreach (var o in overTargets) TestContext.Out.WriteLine($"  more offset targets than the custom arrays (fixed weights baked): {o}");
        foreach (var o in noScene) TestContext.Out.WriteLine($"  no effect scene: {o}");
        foreach (var x in bad.Take(40)) TestContext.Out.WriteLine("  " + x);
        Assert.That(bad, Is.Empty);
        Assert.That(drawn, Is.GreaterThan(2500), "the morphs drawn");
        // the meshes with five offset targets: the bat step ride and its use effect and one boss attack, in both data sets
        Assert.That(overTargets.Select(o => o.Split(' ')[0]).Distinct(), Is.EquivalentTo(new[]
        {
            "item/ride/eff_batstep_ride.nif", "item/use/eff_batstep_use.nif", "npc/skill/halloweencarriage/eff_halloweencarriage_attack_01_d_a.nif",
        }));

        // the frame update of every drawn morph allocates nothing
        var w = new float[4];
        long allocated = Alloc.Least(() =>
        {
            foreach (var m in drawnMorphs) m.WeightsAt(0.37, w.AsSpan(0, Math.Min(4, m.Anim.Deltas.Length)));
        });
        Assert.That(allocated, Is.EqualTo(0), "bytes over one frame of every drawn morph");
    }

    /// <summary>The weights at 40 times over two cycles against the field shader's reading of the same keys
    /// (field_morph.gdshaderinc morph_weight on ComputeScaledTime).</summary>
    static int CheckWeights(EffectMorph m, string at, List<string> bad)
    {
        var a = m.Anim;
        var w = new float[a.Deltas.Length];
        int n = 0;
        double span = Math.Max(a.Stop - a.Start, 1e-3);
        for (int s = 0; s < 40; s++)
        {
            double t = a.Start + span * 2 * s / 39.0;
            m.WeightsAt(t, w);
            double lt = a.Clamp ? Math.Clamp(a.Frequency * t + a.Phase, a.Start, a.Stop) : Loop(a, t);
            for (int k = 0; k < w.Length; k++)
            {
                float want = a.Weights[k] is { } keys ? Key(keys, (float)lt) : a.Static[k];
                if (Math.Abs(want) < 0.001f || !float.IsFinite(want)) want = 0;
                n++;
                if (Math.Abs(w[k] - want) > 1e-3f + 1e-3f * Math.Abs(want)) { bad.Add($"{at}: target {k + 1} at {t:0.###} s weighs {w[k]}, the keys {want}"); return n; }
            }
        }
        return n;
    }

    // NiTimeController::ComputeScaledTime's LOOP: freq * t + phase wrapped into start..stop
    static double Loop(MorphAnim a, double t)
    {
        double x = a.Frequency * t + a.Phase, span = a.Stop - a.Start;
        if (span <= 1e-9) return a.Start;
        double r = (x - a.Start) % span;
        return a.Start + (r < 0 ? r + span : r);
    }

    // NiFloatKey: the end values outside the keys; linear, or NiInterpScalar::Bezier with the segment's out and in tangents
    static float Key(FloatAnim k, float t)
    {
        if (t <= k.Times[0]) return k.Values[0];
        for (int j = 0; j + 1 < k.Times.Length; j++)
        {
            if (t > k.Times[j + 1]) continue;
            float u = (t - k.Times[j]) / Math.Max(k.Times[j + 1] - k.Times[j], 1e-6f), p = k.Values[j], q = k.Values[j + 1];
            if (k.KeyType != 2) return p + (q - p) * u;
            float dp = k.OutTans[j], dq = k.InTans[j + 1];
            float A = 3f * (q - p) - (dq + 2f * dp), Bc = dp + dq - 2f * (q - p);
            return p + (dp + (A + Bc * u) * u) * u;
        }
        return k.Values[^1];
    }
}
