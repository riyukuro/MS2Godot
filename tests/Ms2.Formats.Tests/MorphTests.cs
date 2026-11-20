using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The relative POSITION morph an NiMorphMeshModifier and its NiMorphWeightsController put on a mesh (NifModel ReadMorph).</summary>
public class MorphTests
{
    const string Archive = "Resource/Model/Map";

    static (PackSet, NifSpec) Env() => NifEnv.Open();

    /// <summary>The model's NiMesh with an NiMorphMeshModifier (each file here has one) and its streams.</summary>
    static (ModelMesh mesh, Dictionary<string, (double[] v, int n)> s) MorphMesh(NifFile nif, ModelData md)
    {
        int i = Enumerable.Range(0, nif.Blocks.Count).Single(k => nif.Blocks[k].Type == "NiMesh"
            && nif.Blocks[k].Ints("Modifiers").Any(m => m >= 0 && nif.Blocks[(int)m].Type == "NiMorphMeshModifier"));
        return (md.Meshes.Single(m => m.Name == nif.NodeName(i)), ModelData.MeshStreams(nif, i));
    }

    /// <summary>
    /// The underworld flower has 4 relative targets under a looping controller over 0..3 s; targets 1-3 each have 10 Bezier
    /// keys and stored weights 0, 0, 1. The mesh draws target 0 plus the weighted offsets: Positions is target 0 and the
    /// offsets are the MORPH_POSITION1-3 streams.
    /// </summary>
    [Test]
    public void FlowerMorphsOnTheGpu()
    {
        var (packs, spec) = Env();
        var nif = NifFile.Load(spec, packs.Read(Archive, "underworld/field/uw_fi_nature_flower_b01.nif"));
        var md = ModelData.Load(nif);
        var (mesh, s) = MorphMesh(nif, md);
        var a = mesh.Morph;
        Assert.That(a, Is.Not.Null);
        Assert.That((a.Start, a.Stop, a.Frequency, a.Phase, a.Clamp), Is.EqualTo((0f, 3f, 1f, 0f, false)));
        Assert.That(a.Static, Is.EqualTo(new[] { 0f, 0f, 1f }));
        Assert.That(a.Weights.Select(w => (w.KeyType, w.Times.Length)), Is.All.EqualTo((2, 10)));
        Assert.That(a.Weights[2].Values[0], Is.EqualTo(1f));
        Assert.That(mesh.Positions, Is.EqualTo(s["MORPH_POSITION"].v.Select(x => (float)x).ToArray()));
        Assert.That(a.Deltas, Has.Length.EqualTo(3));
        Assert.That(a.Deltas[2], Is.EqualTo(s["MORPH_POSITION3"].v.Select(x => (float)x).ToArray()));
        Assert.That(mesh.AnimatesMaterial, Is.True);
    }

    /// <summary>
    /// The soulbead's controller is MANAGERCONTROLLED (flags 0x6c) with no interpolators, so it never runs: the stored
    /// weights (1, 0) stay, no morph is kept and the mesh draws target 0.
    /// </summary>
    [Test]
    public void ManagerControlledMorphKeepsStoredWeights()
    {
        var (packs, spec) = Env();
        var nif = NifFile.Load(spec, packs.Read(Archive, "common/quest/sw_quest_soulbead_b01/sw_quest_soulbead_b01.nif"));
        var md = ModelData.Load(nif);
        Assert.That(md.Meshes.Select(m => m.Morph), Is.All.Null);
        var (mesh, s) = MorphMesh(nif, md);
        Assert.That(s.ContainsKey("MORPH_POSITION1"), Is.True);
        Assert.That(mesh.Positions, Is.EqualTo(s["MORPH_POSITION"].v.Select(x => (float)x).ToArray()));
    }
}
