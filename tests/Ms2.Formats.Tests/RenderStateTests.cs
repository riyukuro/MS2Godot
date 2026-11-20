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
        var (packs, spec) = NifEnv.Open();
        return EffectScene.Load(NifFile.Load(spec, packs.Read(Archive, entry)));
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

    /// <summary>The effect runtime draws the stencil property's draw mode (culling) only, not its stencil test: no effect NIF
    /// sets the stencil enable bit (bit 0 of the flags, NiStencilProperty.h ENABLE_MASK), so there is no test to draw.</summary>
    [Test]
    public void NoEffectEnablesStencil()
    {
        var (packs, spec) = NifEnv.Open();
        int files = 0, props = 0;
        var on = new List<string>();
        foreach (var entry in packs.List(Archive).Where(e => e.EndsWith(".nif")).OrderBy(e => e, StringComparer.Ordinal))
        {
            NifFile nif;
            try { nif = NifFile.Load(spec, packs.Read(Archive, entry)); } catch { continue; }
            files++;
            foreach (var b in nif.Blocks)
            {
                if (b.Type != "NiStencilProperty") continue;
                props++;
                long flags = b.IntOr("Flags", -1);
                Assert.That(flags, Is.GreaterThanOrEqualTo(0), entry + ": the 20.1+ flags word");
                if ((flags & 1) != 0) on.Add(entry);
            }
        }
        TestContext.Progress.WriteLine($"effect NIFs {files}, stencil properties {props}");
        Assert.That(files, Is.GreaterThan(1000));
        Assert.That(props, Is.GreaterThan(0));
        Assert.That(on.Distinct(), Is.Empty);
    }

    /// <summary>The faces culled per draw mode, as NiDX9RenderState maps it with left-handed culling off
    /// (NiDX9RenderState.cpp:380-389): DRAW_CCW_OR_BOTH and DRAW_CCW the back faces, DRAW_CW the front faces, DRAW_BOTH none.</summary>
    [Test]
    public void CullFollowsDrawMode()
    {
        Assert.That(new[] { 0, 1, 2, 3 }.Select(d => new ModelMesh { DrawMode = d }.Cull),
            Is.EqualTo(new[] { FaceCull.Back, FaceCull.Back, FaceCull.Front, FaceCull.None }));
    }

    /// <summary>Field meshes carry the stencil draw mode too: the cubric fence (alpha-tested bars) and the town square's floor
    /// pattern draw both faces; the floor lamp keeps its post one-sided and only its light cones two-sided.</summary>
    [Test]
    public void FieldMeshesDrawBothFaces()
    {
        var (packs, spec) = NifEnv.Open();
        ModelData Map(string file) => ModelData.Load(NifFile.Load(spec, packs.Read("Resource/Model/Map",
            packs.List("Resource/Model/Map").Single(n => n.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase)))));
        var fence = Map("ke_fi_cubric_fence_a02.nif").Meshes.Single();
        Assert.That(fence.Cull, Is.EqualTo(FaceCull.None));
        Assert.That(fence.AlphaTestOn, Is.True);
        Assert.That(Map("tr_floor_pattern_b01.nif").Meshes.Single().Cull, Is.EqualTo(FaceCull.None));
        var lamp = Map("co_fi_prop_light_b01.nif").Meshes;
        Assert.That(lamp.Where(m => m.Cull == FaceCull.None).Select(m => m.FileName).OrderBy(n => n), Is.EqualTo(new[] { "Cone01", "Cone02" }));
        Assert.That(lamp.Count(m => m.Cull == FaceCull.Back), Is.EqualTo(1));
    }
    /// <summary>The taxi booth's glass tests GREATER against threshold 0 (NiAlphaProperty flags 0x12ED): the D3D11 renderer passes
    /// the reference as threshold / 255 (D3D11ShaderConstantMap.cpp:3690-3700) and clips at or below it for TEST_GREATER (:3660-3663),
    /// so a faint pixel stays. A zero threshold used to fall back to a 0.5 reference, which cut the glass to its glare's centre.</summary>
    [Test]
    public void GlassTestsGreaterThanZero()
    {
        var (packs, spec) = NifEnv.Open();
        var booth = ModelData.Load(NifFile.Load(spec, packs.Read("Resource/Model/Map",
            packs.List("Resource/Model/Map").Single(n => n.EndsWith("/co_fi_struc_station_a01.nif", StringComparison.OrdinalIgnoreCase)))));
        var glass = booth.Meshes.Single(m => m.Textures.GetValueOrDefault("base", "").Contains("glass_a01", StringComparison.OrdinalIgnoreCase));
        Assert.That(glass.AlphaFlags, Is.EqualTo(0x12ED));
        Assert.That(glass.AlphaTestFunc, Is.EqualTo(4));
        Assert.That(glass.AlphaTestRef, Is.EqualTo(0f));
        Assert.That(glass.AlphaTestKeeps(0.25f, glass.AlphaTestRef), Is.True);
        Assert.That(glass.AlphaTestKeeps(0f, glass.AlphaTestRef), Is.False);
    }

    /// <summary>Each test function's clip sides, as SCM_DEF_ALPHA_TEST_FUNC fills them (D3D11ShaderConstantMap.cpp:3630-3687);
    /// a mesh without the test flag clips below the reference.</summary>
    [Test]
    public void AlphaTestFunctionsClip()
    {
        bool[] Keeps(int func) => new[] { 0.25f, 0.5f, 0.75f }.Select(a => new ModelMesh { AlphaFlags = 0x200 | func << 10, AlphaThreshold = 0 }.AlphaTestKeeps(a, 0.5f)).ToArray();
        Assert.That(Enumerable.Range(0, 8).Select(Keeps), Is.EqualTo(new[]
        {
            new[] { true, true, true }, new[] { true, false, false }, new[] { false, true, false }, new[] { true, true, false },
            new[] { false, false, true }, new[] { true, false, true }, new[] { false, true, true }, new[] { false, false, false },
        }));
        var untested = new ModelMesh { AlphaFlags = 0x0ED };
        Assert.That((untested.AlphaTestRef, untested.AlphaTestKeeps(0.25f, 0.5f), untested.AlphaTestKeeps(0.5f, 0.5f)), Is.EqualTo((0.5f, false, true)));
    }
}
