using System.Text;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Every NiPSMeshParticleSystem in the model packs (both data sets): the system is a mesh-particle one, the meshes of its
/// master particles are marked Master and drawn only as clones (NiPSMeshParticleSystem::InitializeParticle,
/// NiPSMeshParticleSystem.cpp:229-300), never as a field model's or an effect's static meshes, and every system is one the
/// clone draw handles (MeshAlign: fixed normal and up, no rotation keys, rotations only with rotation axes). The masters
/// hang under no node in the files, so the old loaders drew them where they stand in the file.
/// </summary>
public class MeshParticleDataTests
{
    static readonly string[] Archives = { "Resource/Model/Effect", "Resource/Model/Map", "Resource/Model/Npc", "Resource/Model/Item", "Resource/Model/Character" };
    static readonly byte[] Tag = Encoding.ASCII.GetBytes("NiPSMeshParticleSystem");

    /// <summary>The master subtrees as the file gives them (each master and its Children), walked here apart from the loaders.</summary>
    static HashSet<int> Masters(NifFile n)
    {
        var B = n.Blocks; var set = new HashSet<int>();
        void Walk(long b)
        {
            if (b < 0 || !set.Add((int)b)) return;
            if (B[(int)b].TryGetValue("Children", out var c) && c is long[] kids) foreach (var k in kids) Walk(k);
        }
        foreach (var s in B.Where(x => x.Type == "NiPSMeshParticleSystem"))
            if (s.TryGetValue("Master Particles", out var m) && m is long[] ms) foreach (var k in ms) Walk(k);
        return set;
    }

    [Test]
    public void MasterMeshesAreDrawnOnlyAsParticles()
    {
        var (packs, spec) = NifEnv.Open();
        int files = 0, systems = 0, masterMeshes = 0;
        var bad = new List<string>();
        foreach (var archive in Archives)
        {
            List<string> entries;
            try { entries = packs.List(archive).Where(e => e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal).ToList(); }
            catch (Exception) { continue; }
            foreach (var entry in entries)
            {
                var bytes = packs.Read(archive, entry);
                if (bytes.AsSpan().IndexOf(Tag) < 0) continue;
                var n = NifFile.Load(spec, bytes);
                var B = n.Blocks;
                int blocks = B.Count(b => b.Type == "NiPSMeshParticleSystem");
                if (blocks == 0) continue;
                files++;
                string where = $"{archive}/{entry}";
                var masters = Masters(n);
                var masterMeshBlocks = masters.Where(i => B[i].Type == "NiMesh").ToHashSet();
                masterMeshes += masterMeshBlocks.Count;

                foreach (var m in ModelData.Load(n).Meshes)
                    if (masterMeshBlocks.Contains(m.Block)) bad.Add($"{where}: ModelData draws master mesh {m.Name}");

                var sc = EffectScene.Load(n);
                var mps = sc.Systems.Where(s => s.MeshParticles).ToList();
                systems += mps.Count;
                if (mps.Count != blocks) bad.Add($"{where}: {mps.Count} mesh-particle systems of {blocks}");
                foreach (var em in sc.Meshes)
                    if (em.Master != masterMeshBlocks.Contains(em.Mesh.Block)) bad.Add($"{where}: {em.Mesh.Name} Master {em.Master}");
                foreach (var d in mps)
                {
                    // each master's meshes are the drawable (not APP_CULLED) NiMesh blocks of its subtree in the file; a master
                    // that is an empty node (two enchantmaster1 casts) clones nothing
                    int blk = Enumerable.Range(0, B.Count).FirstOrDefault(i => B[i].Type == "NiPSMeshParticleSystem" && n.Name(i) == d.Name, -1);
                    var ms = blk >= 0 && B[blk].TryGetValue("Master Particles", out var mv) && mv is long[] ml ? ml : Array.Empty<long>();
                    int FileMeshes(long k)
                    {
                        if (k < 0) return 0;
                        var kids = B[(int)k].TryGetValue("Children", out var c) && c is long[] ks ? ks : Array.Empty<long>();
                        int own = B[(int)k].Type == "NiMesh" && (B[(int)k].IntOr("Flags", 0) & 1) == 0 ? 1 : 0;
                        return own + kids.Sum(FileMeshes);
                    }
                    if (d.MasterMeshes.Length != ms.Length) bad.Add($"{where} {d.Name}: {d.MasterMeshes.Length} masters of {ms.Length}");
                    else
                        for (int g = 0; g < ms.Length; g++)
                            if (d.MasterMeshes[g].Length != FileMeshes(ms[g]))
                                bad.Add($"{where} {d.Name}: master {g} has {d.MasterMeshes[g].Length} meshes, the file {FileMeshes(ms[g])}");
                    foreach (var g in d.MasterMeshes)
                        foreach (var i in g)
                            if (i < 0 || !sc.Meshes[i].Master) bad.Add($"{where} {d.Name}: master mesh index {i}");
                    if (d.NormalMethod != AlignedQuad.LocalFixed || d.UpMethod != AlignedQuad.LocalFixed) bad.Add($"{where} {d.Name}: methods {d.NormalMethod} {d.UpMethod}");
                    if (d.MeshAlignKeys > 0) bad.Add($"{where} {d.Name}: {d.MeshAlignKeys} rotation keys");
                    if (d.HasRotations && !d.HasRotationAxes) bad.Add($"{where} {d.Name}: rotations without axes");
                }
            }
        }
        TestContext.Out.WriteLine($"{files} files, {systems} mesh-particle systems, {masterMeshes} master meshes");
        Assert.That(systems, Is.GreaterThan(0), "no mesh-particle system found");
        Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(40)));
    }
}
