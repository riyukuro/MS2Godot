using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The sphere-mapped environment map a mesh takes from the NiTextureEffect on its node (NifModel ReadEnvMap).</summary>
public class EnvMapTests
{
    const string Archive = "Resource/Model/Map";

    static (PackSet, NifSpec) Env()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA"); var nx = Environment.GetEnvironmentVariable("MS2_NIFXML");
        if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(nx)) Assert.Ignore("MS2_DATA / MS2_NIFXML not set");
        return (new PackSet(data), new NifSpec(nx));
    }

    /// <summary>
    /// The station booth's glass (an MS2GlassMaterial mesh) hangs from a node whose effect list holds a SPHERE_MAP
    /// environment effect, a child of the scene root. The node turns file y into z, so the effect's u axis (its world
    /// column 2, root x) stays x in the mesh's space and its v axis (column 1, root z) becomes the mesh's y. The body mesh
    /// beside it, under the scene root, has no environment map.
    /// </summary>
    [Test]
    public void StationGlassTakesTheSkyEnvironmentMap()
    {
        var (packs, spec) = Env();
        var md = ModelData.Load(NifFile.Load(spec, packs.Read(Archive, "common/field/co_fi_struc_station_a01.nif")));
        var glass = md.Meshes.Single(m => m.Material == "MS2GlassMaterial");
        Assert.That(md.Bones[glass.ParentBone].FileName, Is.EqualTo("ke_fi_prop_glass_A01"));
        Assert.That(glass.EnvMap, Is.EqualTo("ugc_fi_cubric_itower_A02_sky.dds"));
        Assert.That(glass.EnvU, Is.EqualTo(new[] { 1f, 0f, 0f }).Within(1e-5));
        Assert.That(glass.EnvV, Is.EqualTo(new[] { 0f, 1f, 0f }).Within(1e-5));
        Assert.That(glass.Textures["gloss"], Is.EqualTo("ke_fi_prop_glass_A01.dds"));
        var body = md.Meshes.Single(m => m.FileName == "co_fi_struc_station_A01");
        Assert.That(body.EnvMap, Is.Null);
    }
}
