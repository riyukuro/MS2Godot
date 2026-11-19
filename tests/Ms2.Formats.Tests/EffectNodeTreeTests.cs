using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The effect scene keeps each NiNode-derived block as a node, so a mesh under an NiSortAdjustNode takes that node's
/// transform and animation (NiSortAdjustNode : NiNode, NiSortAdjustNode.h:20).</summary>
public class EffectNodeTreeTests
{
    const string Archive = "Resource/Model/Effect";

    /// <summary>The portal entrance mark (Portal_entrance's effect model): its arrow meshes hang under the animated
    /// NiSortAdjustNode Dummy01.</summary>
    [Test]
    public void EntranceMarkArrowHangsUnderItsSortAdjustNode()
    {
        var (packs, spec) = NifEnv.Open();
        var sc = EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, "bg/common/eff_com_entrance_mark.nif")));
        int dummy = sc.Nodes.FindIndex(n => n.Name == "Dummy01");
        Assert.That(dummy, Is.GreaterThanOrEqualTo(0), "Dummy01 is a node");
        Assert.That(sc.Nodes[dummy].Anim, Is.Not.Null, "Dummy01 keeps its transform controller");
        foreach (var name in new[] { "ChamferBox03", "ChamferBox01", "Object02" })
            Assert.That(sc.Nodes.Single(n => n.Name == name).Parent, Is.EqualTo(dummy), name);
    }

    /// <summary>In every bg/common effect, a node block that is some node's child has a parent in the scene.</summary>
    [Test]
    public void NoChildNodeLosesItsParent()
    {
        var (packs, spec) = NifEnv.Open();
        int files = 0, orphans = 0;
        foreach (var entry in packs.List(Archive).Where(e => e.StartsWith("bg/common/") && e.EndsWith(".nif")))
        {
            var nif = NifFile.Load(spec, packs.Read(Archive, entry));
            var sc = EffectScene.Load(nif);
            var child = new HashSet<long>();
            foreach (var b in nif.Blocks)
                if (b.TryGetValue("Children", out var c) && c is long[] kids) foreach (var k in kids) child.Add(k);
            foreach (var n in sc.Nodes)
                if (n.Parent < 0 && child.Contains(n.Block)) { orphans++; TestContext.Out.WriteLine($"{entry}: {n.Name}"); }
            files++;
        }
        Assert.That(files, Is.GreaterThan(0));
        Assert.That(orphans, Is.Zero);
    }
}
