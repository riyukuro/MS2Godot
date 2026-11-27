using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A mesh under a node flagged APP_CULLED in the file is not drawn (NiAVObject::Cull, NiNode::OnVisible; ModelData.Load).
/// </summary>
public class CulledSubtreeTests
{
    /// <summary>Every model nif (Map, Item, Character, Npc, Path) with a mesh under a culled node, the same on both data
    /// sets, and that mesh's block.</summary>
    static readonly (string Archive, string Entry, int Block)[] Models =
    {
        ("Resource/Model/Map", "common/functobj/ugc_functobj_computer_a01/ugc_in_funct_computer_a01.nif", 51),
        ("Resource/Model/Map", "common/functobj/co_functobj_goldenpig_a01/co_functobj_goldenpig_a01.nif", 110),
        ("Resource/Model/Item", "1/22/12200060_m_clpatrainingbeachpants.nif", 23),
    };

    /// <summary>
    /// The UGC computer's "Editable Poly" (block 51: opaque, emissive and diffuse 0, so it draws black) covers its four
    /// screens; its node (block 49, "Object01", flags 0x113) is culled and its NiVisController is manager-controlled with no
    /// interpolator, so the client shows the screens. The body and the four screens stay.
    /// </summary>
    [Test]
    public void ComputerScreenCoverIsLeftOut()
    {
        var (packs, spec) = NifEnv.Open();
        var nif = NifFile.Load(spec, packs.Read(Models[0].Archive, Models[0].Entry));
        Assert.That(nif.Blocks[49].IntOr("Flags", 0) & 1, Is.EqualTo(1));
        var md = ModelData.Load(nif);
        Assert.That(md.Meshes.Select(m => m.Block), Is.EquivalentTo(new[] { 9, 15, 28, 35, 42 }));
    }

    /// <summary>
    /// The premise of leaving the cover out: no sequence the computer's kfm lists shows Object01. Its Opened sequence
    /// drives Object01's NiVisController with "hidden" keys only, and Idle_A has no visibility track at all.
    /// </summary>
    [Test]
    public void ComputerSequencesKeepTheCoverHidden()
    {
        var (packs, spec) = NifEnv.Open();
        const string dir = "common/functobj/ugc_functobj_computer_a01/";
        int vis = 0;
        foreach (var kf in new[] { "ugc_in_funct_computer_a01_opened.kf", "idle_a.kf" })
        {
            var nif = NifFile.Load(spec, packs.Read(Models[0].Archive, dir + kf));
            foreach (var b in nif.Blocks)
            {
                if (nif.StrField(b, "Controller Type") != "NiVisController") continue;
                Assert.That(nif.StrField(b, "Node Name"), Is.EqualTo("Object01"), kf);
                var keys = nif.Blocks[(int)b.Int("Data")].Struct("Data").Structs("Keys");
                Assert.That(keys, Is.Not.Empty, kf);
                Assert.That(keys.Select(k => Convert.ToInt64(k["Value"])), Is.All.EqualTo(0L), kf);
                vis++;
            }
        }
        Assert.That(vis, Is.EqualTo(1));
    }

    /// <summary>Each listed model loses exactly its mesh under the culled node, and nothing else.</summary>
    [Test]
    public void WholeListLosesOnlyTheCulledMeshes()
    {
        var (packs, spec) = NifEnv.Open();
        foreach (var (archive, entry, block) in Models)
        {
            var nif = NifFile.Load(spec, packs.Read(archive, entry));
            var all = ModelData.Load(nif, keepCulledSubtrees: true).Meshes.Select(m => m.Block).ToList();
            var drawn = ModelData.Load(nif).Meshes.Select(m => m.Block).ToList();
            Assert.That(all, Does.Contain(block), entry);
            Assert.That(drawn, Is.EquivalentTo(all.Where(b => b != block)), entry);
        }
    }

    /// <summary>An effect keeps the meshes under its culled nodes: their NiVisControllers show them over time, and the
    /// effect's nodes carry the culled flag (EffectNode.Culled).</summary>
    [Test]
    public void EffectKeepsCulledNodeMeshes()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read("Resource/Model/Effect", "ch/skill/assassin/eff_assassin_shadowweb_hit_01_a.nif")));
        bool Culled(int n) => n >= 0 && (sc.Nodes[n].Culled || Culled(sc.Nodes[n].Parent));
        Assert.That(sc.Meshes.Count(m => Culled(m.Node)), Is.EqualTo(8));
    }
}
