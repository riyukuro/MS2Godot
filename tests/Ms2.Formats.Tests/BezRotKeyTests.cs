using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Bezier quaternion keys (NiTransformData Rotation Type 2, NiBezRotKey). Before, the channel threw "rotation key type 2"
/// and the whole effect failed to load (an npc die and skill effects).
/// </summary>
public class BezRotKeyTests
{
    static readonly string[] Files =
    {
        "npc/skill/bling/eff_bling_skill_swing_cast.nif",
        "npc/skill/chareuk/eff_chareuk_skill_batsummon_arrow.nif",
        "npc/skill/garrak/eff_garrak_die.nif",
        "npc/skill/leadertwingko/eff_leadertwingko_skill_throwingartifact_cast_a.nif",
    };

    static Quat Q(NifStruct v) => new(v.Float("w"), v.Float("x"), v.Float("y"), v.Float("z"));

    /// <summary>The engine's squad between keys i and i+1 (NiBezRotKey.cpp:71-108, efd Quaternion Intermediate and
    /// Squad), written out here on its own.</summary>
    static Quat Squad(Quat[] q, int i, double u)
    {
        Quat Mid(int k)
        {
            Quat p = q[Math.Max(0, k - 1)], c = q[k], n = q[Math.Min(q.Length - 1, k + 1)], inv = c.Conjugate();
            return c * (((inv * p).Log() + (inv * n).Log()) * -0.25).Exp();
        }
        return Quat.Slerp(Quat.Slerp(q[i], q[i + 1], u), Quat.Slerp(Mid(i), Mid(i + 1), u), 2 * u * (1 - u));
    }

    static double Angle(Quat a, Quat b) => 2 * Math.Acos(Math.Min(1, Math.Abs(a.Normalized().Dot(b.Normalized()))));

    [Test]
    public void TheBezierKeyedEffectsLoad()
    {
        var (packs, spec) = NifEnv.Open();
        int checkedNodes = 0;
        foreach (var f in Files)
        {
            var nif = NifFile.Load(spec, packs.Read("Resource/Model/Effect", f));
            var sc = EffectScene.Load(nif);
            // each node whose transform data has Bezier rotation keys
            foreach (var nd in sc.Nodes)
            {
                var ctl = nif.Blocks.FirstOrDefault(b => b.Type == "NiTransformController" && b.IntOr("Target", -1) == nd.Block);
                if (ctl == null) continue;
                long ii = ctl.IntOr("Interpolator", -1);
                if (ii < 0) continue;
                long di = nif.Blocks[(int)ii].IntOr("Data", -1);
                if (di < 0) continue;
                var data = nif.Blocks[(int)di];
                if (data.IntOr("Num Rotation Keys", 0) < 2 || data.Int("Rotation Type") != 2) continue;
                Assert.That(nd.Anim?.R, Is.Not.Null, $"{f} {nd.Name}");
                var keys = data.Structs("Quaternion Keys");
                var t = keys.Select(k => (double)k.Float("Time")).ToArray();
                var q = keys.Select(k => Q(k.Struct("Value"))).ToArray();
                for (int i = 1; i < q.Length; i++) if (q[i - 1].Dot(q[i]) < 0) q[i] = -q[i];
                for (int i = 0; i < q.Length; i++)
                    Assert.That(Angle(nd.Anim.R(t[i]), q[i]), Is.LessThan(1e-4), $"{f} {nd.Name} key {i}");
                for (int i = 0; i + 1 < q.Length; i++)
                    foreach (double u in new[] { 0.25, 0.5, 0.75 })
                        Assert.That(Angle(nd.Anim.R(t[i] + u * (t[i + 1] - t[i])), Squad(q, i, u)), Is.LessThan(1e-4),
                            $"{f} {nd.Name} keys {i}-{i + 1} at {u}");
                checkedNodes++;
            }
        }
        TestContext.Out.WriteLine($"Bezier-keyed nodes checked: {checkedNodes}");
        Assert.That(checkedNodes, Is.GreaterThan(0));
    }

    [Test]
    public void ThreeEvenKeysEaseFromTheFirst()
    {
        // 0, 90 and 180 degrees about z at even times: the middle key's control point is itself, the first one's is turned
        // back a quarter of the way to the next key (-22.5 degrees), so halfway along the first pair the rotation is
        // slerp(45, slerp(-22.5, 90, 1/2) = 33.75, 1/2) = 39.375 degrees, not the slerp's 45
        Quat Z(double deg) => Quat.FromAxis(2, deg * Math.PI / 180);
        var q = new[] { Z(0), Z(90), Z(180) };
        double deg = Angle(Squad(q, 0, 0.5), Z(0)) * 180 / Math.PI;
        Assert.That(deg, Is.EqualTo(39.375).Within(1e-6));
    }

    /// <summary>Every model and animation in both data sets whose transform data has Bezier rotation keys now loads: a .nif
    /// as an effect scene and through its controllers, a .kf as an animation.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryBezierKeyedModelLoads(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var key = System.Text.Encoding.ASCII.GetBytes("NiTransformData");
        int files = 0;
        var bad = new List<string>();
        foreach (var archive in PackIndex.Archives(dir).Where(a => a.StartsWith("Resource/Model/", StringComparison.Ordinal)))
            foreach (var entry in packs.List(archive).Where(e => (e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) || e.EndsWith(".kf", StringComparison.OrdinalIgnoreCase))).OrderBy(e => e, StringComparer.Ordinal))
            {
                byte[] raw;
                try { raw = packs.Read(archive, entry); } catch (Exception) { continue; }
                if (raw.AsSpan().IndexOf(key) < 0) continue;
                NifFile nif;
                try { nif = NifFile.Load(spec, raw); } catch (Exception) { continue; }
                if (!nif.Blocks.Any(b => b.Type == "NiTransformData" && b.IntOr("Num Rotation Keys", 0) != 0 && b.Int("Rotation Type") == 2)) continue;
                files++;
                try
                {
                    if (entry.EndsWith(".kf", StringComparison.OrdinalIgnoreCase)) KfAnimation.Load(nif);
                    else { EffectScene.Load(nif); KfAnimation.FromControllers(nif); }
                }
                catch (Exception e) { bad.Add($"{archive}:{entry}: {e.Message}"); }
            }
        TestContext.Out.WriteLine($"{env}: models with Bezier rotation keys: {files}");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(files, Is.GreaterThan(0));
    }
}
