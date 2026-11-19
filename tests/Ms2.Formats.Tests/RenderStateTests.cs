using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The render state a mesh inherits from the scene graph: NiZBufferProperty flags, the NiStencilProperty draw mode
/// (the nearest property up the graph wins) and an NiSortAdjustNode's SORTING_OFF.</summary>
public class RenderStateTests
{
    const string Archive = "Resource/Model/Effect";

    static EffectScene Load(string entry)
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA"); var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nx)) Assert.Ignore("MS2_DATA / MS2_NIFXML not set");
        return EffectScene.Load(NifFile.Load(new NifSpec(nx), new PackSet(data).Read(Archive, entry)));
    }

    /// <summary>A mesh without properties of its own takes the defaults: z test and write with TEST_LESSEQUAL (15), DRAW_CCW_OR_BOTH.</summary>
    [Test]
    public void Defaults()
    {
        var m = new ModelMesh();
        Assert.That(m.ZFlags, Is.EqualTo(15));
        Assert.That(m.ZTest && m.ZWrite, Is.True);
        Assert.That((m.ZFlags >> 2) & 15, Is.EqualTo(3));
        Assert.That(m.DrawMode, Is.EqualTo(0));
        Assert.That(m.NoSort, Is.False);
    }

    /// <summary>The entrance arrow: the ground circle has its own z-buffer property 13 (test, no write); the chevrons under
    /// the SORTING_OFF node Dummy01 are unsorted.</summary>
    [Test]
    public void EntranceArrow()
    {
        var sc = Load("bg/common/eff_com_entrance_mark.nif");
        var plane = sc.Meshes.Single(m => m.Mesh.Name == "Plane").Mesh;
        Assert.That(plane.ZFlags, Is.EqualTo(13));
        Assert.That(plane.ZTest, Is.True); Assert.That(plane.ZWrite, Is.False);
        Assert.That(plane.NoSort, Is.False);
        var chevron = sc.Meshes.Single(m => m.Mesh.Name == "ChamferBox03").Mesh;
        Assert.That(chevron.NoSort, Is.True);
        Assert.That(sc.Meshes.All(m => m.Mesh.ZTest), "every mesh tests depth");
    }

    /// <summary>The member portal: the scene root's z-buffer property is 15, but every mesh sits under a node whose own 13
    /// (test, no write) replaces it; the meshes with the stencil property 19840 draw both faces (DRAW_BOTH), the rest keep the default.</summary>
    [Test]
    public void MemberPortal()
    {
        var sc = Load("bg/common/eff_com_portal_b.nif");
        Assert.That(sc.Meshes.Select(m => m.Mesh.ZFlags).Distinct(), Is.EqualTo(new[] { 13 }));
        Assert.That(sc.Meshes.Any(m => m.Mesh.DrawMode == 3), "a two-sided mesh");
        Assert.That(sc.Meshes.Any(m => m.Mesh.DrawMode == 0), "a one-sided mesh");
        Assert.That(sc.Meshes.All(m => m.Mesh.DrawMode == 0 || m.Mesh.DrawMode == 3));
    }
}
