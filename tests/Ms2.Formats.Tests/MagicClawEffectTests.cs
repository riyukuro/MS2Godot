using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The Wizard's Magic Claw cast effect (skill 10300001, e0 of its strTagEffects, CH/Skill/Wizard/
/// Eff_Wizard_MagicClaw_Cast_01.xml) plays eff_wizard_magicclaw_cast_01_a.nif, whose Sphere01 scale keys are 1 at 0.333 s and
/// NaN at 0.7 s, in the KMS and the GMS2 data alike. The node's scale curve is therefore 1 up to the first key and NaN after
/// it: the game's effect runtime (EffectInstance.Place) has to hide what that node carries, not hand the NaN to the renderer.</summary>
public class MagicClawEffectTests
{
    const string Nif = "ch/skill/wizard/eff_wizard_magicclaw_cast_01_a.nif";

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheCastSpheresScaleTurnsNaNAfterItsFirstKey(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        using var packs = new PackSet(d);
        var sc = EffectScene.Load(NifFile.Load(new NifSpec(NifEnv.NifXml()), packs.Read("Resource/Model/Effect", Nif)));
        var sphere = sc.Nodes.Single(n => n.Name == "Sphere01");
        Assert.That(sphere.Anim?.S, Is.Not.Null, "the sphere has a scale curve");
        double At(double t) => sphere.Anim.S(sphere.AnimCtl?.Local(t) ?? t);
        Assert.That(At(0.2), Is.EqualTo(1).Within(1e-6), "before the first key");
        Assert.That(double.IsNaN(At(0.5)), "between the keys");
        Assert.That(double.IsNaN(At(0.69)), "up to the NaN key");
        Assert.That(sc.Nodes.Where(n => n.Name != "Sphere01" && n.Anim?.S != null).All(n => double.IsFinite(n.Anim.S(n.AnimCtl?.Local(0.5) ?? 0.5))),
            "only the sphere's scale is NaN");
    }

    /// <summary>Every effect node whose transform keys give a non-finite value at some time (sampled every 1/30 s over the
    /// effect), and what hangs under it in both data sets: meshes (hidden by EffectInstance.Place) and particle systems whose
    /// system, emitter or emitter mesh node is such a node or under one. In both data sets the Magic Claw cast sphere is the
    /// one mesh and no particle system is: a NaN particle cannot come from the keys.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryNonFiniteNodeKey(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore(env + " is not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        int nifs = 0, animNodes = 0, badNodes = 0, badNifs = 0, badMeshes = 0, badSystems = 0, systems = 0;
        var list = new List<string>(); var loadErrors = new List<string>();
        foreach (var archive in new[] { "Resource/Model/Effect", "Resource/Model/Map" })
            foreach (var entry in packs.List(archive).Where(e => e.EndsWith(".nif", StringComparison.Ordinal)).OrderBy(e => e, StringComparer.Ordinal))
            {
                EffectScene sc;
                try { sc = EffectScene.Load(NifFile.Load(spec, packs.Read(archive, entry))); }
                catch (NotSupportedException) { continue; }
                catch (ArgumentOutOfRangeException e) { loadErrors.Add($"{entry}: {e.TargetSite?.Name}"); continue; }
                nifs++;
                var bad = new bool[sc.Nodes.Count];
                double end = Math.Max(sc.Duration, 0.1);
                for (int i = 0; i < sc.Nodes.Count; i++)
                {
                    var n = sc.Nodes[i];
                    if (n.Anim == null) continue;
                    animNodes++;
                    for (double t = 0; t <= end + 1e-9 && !bad[i]; t += 1.0 / 30)
                    {
                        double lt = n.AnimCtl?.Local(t) ?? t;
                        if (n.Anim.S != null && !double.IsFinite(n.Anim.S(lt))) bad[i] = true;
                        if (n.Anim.T != null) { var v = n.Anim.T(lt); if (!double.IsFinite(v.X + v.Y + v.Z)) bad[i] = true; }
                        if (n.Anim.R != null) { var q = n.Anim.R(lt); if (!double.IsFinite(q.X + q.Y + q.Z + q.W)) bad[i] = true; }
                    }
                    if (bad[i]) badNodes++;
                }
                if (!bad.Any(b => b)) { systems += sc.Systems.Count; continue; }
                bool Under(int i) { for (; i >= 0; i = sc.Nodes[i].Parent) if (bad[i]) return true; return false; }
                badNifs++;
                int meshes = sc.Meshes.Count(m => Under(m.Node));
                var sys = sc.Systems.Where(d => Under(d.Node) || d.Emitters.Any(e => Under(e.Node) || Under(e.MeshNode))).Select(d => d.Name).ToList();
                systems += sc.Systems.Count; badMeshes += meshes; badSystems += sys.Count;
                list.Add($"{entry}: nodes [{string.Join(", ", Enumerable.Range(0, bad.Length).Where(i => bad[i]).Select(i => sc.Nodes[i].Name))}], {meshes} meshes, systems [{string.Join(", ", sys)}]");
            }
        TestContext.Out.WriteLine($"{env}: {nifs} nifs, {animNodes} animated nodes, {badNodes} with a non-finite key in {badNifs} nifs; under them {badMeshes} meshes and {badSystems} of {systems} particle systems");
        foreach (var l in list) TestContext.Out.WriteLine("  " + l);
        TestContext.Out.WriteLine($"load errors {loadErrors.Count}: {string.Join("; ", loadErrors.Take(5))}");
        Assert.That(list.Any(l => l.StartsWith(Nif + ":", StringComparison.Ordinal)), "the Magic Claw cast effect is listed");
        Assert.That(badSystems, Is.Zero, "no particle system hangs from a node whose key is not finite");
        Assert.That(badMeshes, Is.EqualTo(1), "the cast sphere is the one mesh that does");
    }

    /// <summary>Every kf animation (characters, npcs, items, tools, maps, cameras, paths, effects) and every nif's own
    /// transform controllers in the model archives other than Effect (covered above), sampled every 1/30 s: the channels
    /// whose keys give a non-finite value. A bone or node driven by one would hand a NaN to the skeleton or the node
    /// transform; the same seven files in both data sets, NaN for the whole clip.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryNonFiniteAnimationKey(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore(env + " is not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        int files = 0, channels = 0, errors = 0;
        var list = new List<string>();
        foreach (var name in new[] { "Character", "Npc", "Item", "Tool", "Map", "Camera", "Path", "Effect" })
        {
            var archive = "Resource/Model/" + name;
            foreach (var entry in packs.List(archive).OrderBy(e => e, StringComparer.Ordinal))
            {
                bool kf = entry.EndsWith(".kf", StringComparison.Ordinal);
                if (!kf && (name == "Effect" || !entry.EndsWith(".nif", StringComparison.Ordinal))) continue;
                KfAnimation a;
                try { var f = NifFile.Load(spec, packs.Read(archive, entry)); a = kf ? KfAnimation.Load(f) : KfAnimation.FromControllers(f); }
                catch (Exception) { errors++; continue; }
                if (a == null) continue;
                files++;
                var bad = new List<string>();
                double end = Math.Max(a.Duration, 0.1);
                foreach (var (node, ch) in a.Channels)
                {
                    channels++;
                    for (double t = 0; t <= end + 1e-9; t += 1.0 / 30)
                    {
                        string which = "";
                        if (ch.S != null && !double.IsFinite(ch.S(t))) which += "S";
                        if (ch.T != null) { var v = ch.T(t); if (!double.IsFinite(v.X + v.Y + v.Z)) which += "T"; }
                        if (ch.R != null) { var q = ch.R(t); if (!double.IsFinite(q.X + q.Y + q.Z + q.W)) which += "R"; }
                        if (which != "")
                        {
                            int finite = 0, n = 0;
                            for (double u = 0; u <= end + 1e-9; u += 1.0 / 30, n++)
                                if ((ch.S == null || double.IsFinite(ch.S(u))) && (ch.T == null || double.IsFinite(ch.T(u).X + ch.T(u).Y + ch.T(u).Z))
                                    && (ch.R == null || double.IsFinite(ch.R(u).X + ch.R(u).Y + ch.R(u).Z + ch.R(u).W))) finite++;
                            bad.Add($"{node} from {t:0.###} s ({which}, finite at {finite} of {n} samples)"); break;
                        }
                    }
                }
                if (bad.Count == 0) continue;
                // What the model nifs beside the kf hang from those nodes: rigid meshes, and skinned vertices weighted to them.
                var what = new List<string>();
                if (kf)
                {
                    string folder = entry[..(entry.LastIndexOf('/') + 1)];
                    foreach (var m in packs.List(archive).Where(e => e.StartsWith(folder, StringComparison.Ordinal) && e.IndexOf('/', folder.Length) < 0 && e.EndsWith(".nif", StringComparison.Ordinal)))
                    {
                        ModelData md;
                        try { md = ModelData.Load(NifFile.Load(spec, packs.Read(archive, m))); } catch (Exception) { continue; }
                        var under = new bool[md.Bones.Count];
                        for (int b = 0; b < md.Bones.Count; b++)
                            for (int k = b; k >= 0 && !under[b]; k = md.Bones[k].Parent)
                                if (bad.Any(x => x.StartsWith(md.Bones[k].Name + " from", StringComparison.Ordinal))) under[b] = true;
                        int rigid = md.Meshes.Count(x => !x.Skinned && x.ParentBone >= 0 && under[x.ParentBone]), verts = 0;
                        foreach (var x in md.Meshes.Where(x => x.Skinned && x.Joints != null))
                            for (int v = 0; v < x.Joints.Length; v++) if (x.Weights[v] > 0 && under[x.Joints[v]]) verts++;
                        what.Add($"{m[folder.Length..]} {under.Count(u => u)} bones, {rigid} rigid meshes, {verts} skin weights");
                    }
                }
                list.Add($"{archive}/{entry}: {string.Join(", ", bad)}" + (what.Count > 0 ? " [" + string.Join("; ", what) + "]" : ""));
            }
        }
        TestContext.Out.WriteLine($"{env}: {files} animations, {channels} channels, {list.Count} files with a non-finite key, {errors} not read");
        foreach (var l in list) TestContext.Out.WriteLine("  " + l);
        Assert.That(files, Is.GreaterThan(1000));
        // The ones the game's KfNanCheck (res://tests/kf_nan.tscn) builds and plays: a new one belongs there too.
        Assert.That(list.Select(l => l[..l.IndexOf(':')]), Is.EquivalentTo(new[]
        {
            "Resource/Model/Npc/02/01/02010040/talk_a.kf", "Resource/Model/Npc/02/01/02010040/chatup_a.kf",
            "Resource/Model/Npc/21/21803600/idle_a.kf", "Resource/Model/Npc/21/21803600/idle_b.kf", "Resource/Model/Npc/21/21803600/attack_idle_a.kf",
            "Resource/Model/Map/common/functobj/co_functobj_seesaw_a01/idle_a.kf", "Resource/Model/Map/common/functobj/co_functobj_seesaw_a01/co_functobj_seesaw_a01_off.kf",
        }));
    }
}
