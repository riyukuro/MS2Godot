using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// NiVisController (EffectNode.Vis, BoolTrack) and NiPSResetOnLoopCtlr (ParticleSystemDef.ResetCtl). Both were left in
/// Unsupported before: a vis-keyed node was drawn all the time and a reset-on-loop system kept its particles over the wrap.
/// </summary>
public class VisResetTests
{
    [Test]
    public void TheBoolKeysStep()
    {
        // an assassin web piece: hidden at 0.1, shown from 0.133333 on (NiStepBoolKey: the earlier key until the later one's time)
        var web = new BoolTrack { Times = new[] { 0.1, 0.133333 }, Values = new[] { false, true }, KeyType = 5 };
        Assert.That(web.At(0), Is.False);
        Assert.That(web.At(0.1), Is.False);
        Assert.That(web.At(0.12), Is.False);
        Assert.That(web.At(0.133333), Is.True);
        Assert.That(web.At(5), Is.True);
        // a chest: shown, hidden, shown again
        var chest = new BoolTrack { Times = new[] { 0.0, 0.5, 1.0 }, Values = new[] { true, false, true }, KeyType = 5 };
        Assert.That(new[] { chest.At(-1), chest.At(0.25), chest.At(0.5), chest.At(0.75), chest.At(1), chest.At(2) },
            Is.EqualTo(new[] { true, true, false, false, true, true }));
        // one key, or no keys and the pose
        Assert.That(new BoolTrack { Times = new[] { 3.0 }, Values = new[] { false } }.At(0), Is.False);
        Assert.That(new BoolTrack { Pose = true }.At(1), Is.True);
        Assert.That(new BoolTrack { Pose = false }.At(1), Is.False);
    }

    [Test]
    public void TheWebPiecesShowOneAfterAnother()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "ch/skill/assassin/eff_assassin_shadowweb_hit_01_a.nif")));
        Assert.That(sc.Unsupported, Does.Not.Contain("NiVisController"));
        var keyed = sc.Nodes.Where(x => x.Vis != null).ToList();
        Assert.That(keyed, Has.Count.EqualTo(8));
        foreach (var nd in keyed)
        {
            Assert.That(nd.Vis.KeyType, Is.EqualTo(5), nd.Name);
            Assert.That(nd.VisCtl.Cycle, Is.EqualTo(2), nd.Name);       // flags 76: clamp
            double on = nd.VisCtl.Stop;
            Assert.That(nd.Vis.At(nd.VisCtl.Local(0)), Is.False, nd.Name);
            Assert.That(nd.Vis.At(nd.VisCtl.Local(on - 0.01)), Is.False, nd.Name);
            Assert.That(nd.Vis.At(nd.VisCtl.Local(on)), Is.True, nd.Name);
            Assert.That(nd.Vis.At(nd.VisCtl.Local(sc.Duration + 1)), Is.True, nd.Name);
        }
        // each shows at its own time
        Assert.That(keyed.Select(x => Math.Round(x.VisCtl.Stop, 3)).Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void AManagerControlledOneGivesNoTrack()
    {
        // flags 108 (manager-controlled), no interpolator: it takes its value from a sequence only
        var (packs, spec) = NifEnv.Open();
        var nif = NifFile.Load(spec, packs.Read("Resource/Model/Map", "common/functobj/ugc_functobj_computer_a01/ugc_in_funct_computer_a01.nif"));
        Assert.That(nif.Blocks.Any(b => b.Type == "NiVisController" && (b.IntOr("Flags", 0) & EffectScene.ManagerControlled) != 0));
        var sc = EffectScene.Load(nif);
        Assert.That(sc.Nodes.Where(x => x.Vis != null), Is.Empty);
        Assert.That(sc.Unsupported, Does.Not.Contain("NiVisController"));
    }

    [Test]
    public void ThePotionSystemsResetOnTheLoop()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "bg/common/randommap/eff_ludi_obj_potionbottle_a_cast.nif")));
        Assert.That(sc.Unsupported, Does.Not.Contain("NiPSResetOnLoopCtlr"));
        var reset = sc.Systems.Where(s => s.ResetCtl != null).ToList();
        Assert.That(reset, Has.Count.EqualTo(6));
        foreach (var s in reset)
        {
            Assert.That(s.ResetCtl.Cycle, Is.EqualTo(0), s.Name);
            Assert.That(s.ResetCtl.Stop, Is.EqualTo(0.666667).Within(1e-5), s.Name);
            // the time goes back at the wrap
            Assert.That(s.ResetCtl.Local(0.7), Is.LessThan(s.ResetCtl.Local(0.6)), s.Name);
        }
    }

    /// <summary>
    /// Every NiVisController and NiPSResetOnLoopCtlr in the data's models (effects, maps, items, npcs): a vis controller is
    /// read onto its node with step keys that cover its time window, or it is manager-controlled with no interpolator; a
    /// reset controller is read onto its system and loops; neither lands in Unsupported. Also counted, as the draw now
    /// follows them: nodes culled under a culled parent, systems on a culled node, vis tracks under a master particle.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryVisAndResetIsRead(string env)
    {
        var dir = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore($"{env} not set");
        using var packs = new PackSet(dir);
        var spec = new NifSpec(NifEnv.NifXml());
        var keys = new[] { "NiVisController", "NiPSResetOnLoopCtlr", "NiPSMeshParticleSystem" }.Select(k => System.Text.Encoding.ASCII.GetBytes(k)).ToArray();
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string k, int n = 1) => count[k] = count.GetValueOrDefault(k) + n;
        var bad = new List<string>();
        foreach (var archive in PackIndex.Archives(dir).Where(a => a.StartsWith("Resource/Model/", StringComparison.Ordinal)))
            foreach (var entry in packs.List(archive).Where(e => e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
            {
                byte[] raw;
                try { raw = packs.Read(archive, entry); } catch (Exception) { continue; }
                if (!keys.Any(k => raw.AsSpan().IndexOf(k) >= 0)) continue;
                NifFile nif; EffectScene sc;
                try { nif = NifFile.Load(spec, raw); sc = EffectScene.Load(nif); }
                catch (Exception) { Add("not loaded"); continue; }
                string where = archive.Substring("Resource/Model/".Length), at = $"{archive}:{entry}";
                foreach (var u in new[] { "NiVisController", "NiPSResetOnLoopCtlr" })
                    if (sc.Unsupported.Contains(u)) bad.Add($"{at}: {u} unsupported");
                var nodeOf = new Dictionary<int, EffectNode>();
                foreach (var nd in sc.Nodes) nodeOf[nd.Block] = nd;
                var master = new HashSet<EffectNode>(sc.Systems.SelectMany(s => s.MasterNodes.SelectMany(g => g)).Select(k => sc.Nodes[k]));
                var sysOf = new Dictionary<int, ParticleSystemDef>();
                foreach (var s in sc.Systems) if (s.Node >= 0) sysOf[sc.Nodes[s.Node].Block] = s;
                for (int i = 0; i < nif.Blocks.Count; i++)
                {
                    var b = nif.Blocks[i];
                    long flags = b.IntOr("Flags", 0), target = b.IntOr("Target", -1), interp = b.IntOr("Interpolator", -1);
                    if (b.Type == "NiVisController")
                    {
                        Add($"{where} vis controllers");
                        if ((flags & EffectScene.ManagerControlled) != 0)
                        {
                            Add($"{where} vis manager-controlled");
                            if (interp >= 0) bad.Add($"{at} block {i}: manager-controlled with an interpolator");
                            continue;
                        }
                        if (!nodeOf.TryGetValue((int)target, out var nd)) { bad.Add($"{at} block {i}: target {target} is no node"); continue; }
                        if (nd.Vis == null) { bad.Add($"{at} block {i}: not read"); continue; }
                        if (nd.Vis.KeyType != 5) bad.Add($"{at} block {i}: key type {nd.Vis.KeyType}");
                        if (nd.Vis.Times.Length == 0 || nd.Vis.Times[0] > nd.VisCtl.Start + 1e-4 || nd.Vis.Times[^1] < nd.VisCtl.Stop - 1e-4)
                            bad.Add($"{at} block {i}: keys {string.Join(",", nd.Vis.Times)} do not cover {nd.VisCtl.Start}-{nd.VisCtl.Stop}");
                        Add($"{where} vis read");
                        if (master.Contains(nd)) Add($"{where} vis under a master");
                    }
                    else if (b.Type == "NiPSResetOnLoopCtlr")
                    {
                        Add($"{where} reset controllers");
                        if (!sysOf.TryGetValue((int)target, out var s)) { bad.Add($"{at} block {i}: target {target} is no system"); continue; }
                        if (s.ResetCtl == null || s.ResetCtl.Cycle != 0) { bad.Add($"{at} block {i}: not read"); continue; }
                        Add($"{where} reset read{(s.MeshParticles ? " (mesh system)" : "")}");
                    }
                }
                bool Up(int k, Func<EffectNode, bool> f) { for (int g = 0; k >= 0 && g < 256; k = sc.Nodes[k].Parent, g++) if (f(sc.Nodes[k])) return true; return false; }
                foreach (var m in sc.Meshes)
                    if (!m.Master && !sc.Nodes[m.Node].Culled && Up(m.Node, x => x.Culled))
                    {
                        Add($"{where} meshes under a culled parent");
                        TestContext.Out.WriteLine($"{env}: under a culled parent: {at} {m.Mesh.Name}");
                    }
                foreach (var s in sc.Systems)
                    if (s.Emitters.Count > 0 && Up(s.Node, x => x.Culled)) Add($"{where} systems culled");
                foreach (var s in sc.Systems.Where(s => s.MeshParticles && s.MasterNodes.Length > 0 && s.MasterNodes[0].Length > 0))
                    if (sc.Nodes[s.MasterNodes[0][0]].Culled) Add($"{where} masters culled");
            }
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"{env}:   {k}: {c}");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(20)));
        Assert.That(count.Keys.Any(k => k.EndsWith("vis read")), Is.True);
        Assert.That(count.Keys.Any(k => k.StartsWith("Effect reset read")), Is.True);
    }
}
