using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>FUN_141422f30 (@0x141422f30, itemugcmesh.cpp:455): a scene graph object whose materials include no
/// MS2CharacterSkinMaterial and whose own NiTexturingProperty has a source texture in shader map 0.</summary>
public class ModelShaderTextureTests
{
    const string Skin = "MS2CharacterSkinMaterial";

    static NifStruct Texturing(bool shader0, bool shader1Only = false)
    {
        var s = new NifStruct { Type = "NiTexturingProperty" };
        var maps = new List<object>();
        if (shader1Only) maps.Add(new NifStruct { ["Has Map"] = 0L });
        if (shader0 || shader1Only)
            maps.Add(new NifStruct { ["Has Map"] = 1L, ["Map"] = new NifStruct { ["Source"] = 9L } });
        s["Shader Textures"] = maps.ToArray();
        return s;
    }

    static NifStruct Obj(string type, long[] children, long[] props, params string[] materials)
    {
        var s = new NifStruct { Type = type, ["Children"] = children, ["Properties"] = props };
        if (materials.Length > 0)
            s["Material Data"] = new NifStruct { ["Num Materials"] = (long)materials.Length, ["Material Name"] = materials.Select(m => (object)new NifStruct { ["Index"] = (long)Array.IndexOf(Names, m) }).ToArray() };
        return s;
    }

    static readonly string[] Names = { "", Skin, "Other" };

    /// <summary>Block 0 is the root node, blocks 1.. are given; the texturing property is block 9.</summary>
    static bool Scan(NifStruct texturing, params NifStruct[] blocks)
    {
        var nif = new NifFile();
        nif.Strings.AddRange(Names);
        nif.Blocks.Add(Obj("NiNode", Enumerable.Range(1, blocks.Length).Select(i => (long)i).ToArray(), new long[0]));
        nif.Blocks.AddRange(blocks);
        while (nif.Blocks.Count < 9) nif.Blocks.Add(new NifStruct { Type = "NiNode" });
        nif.Blocks.Add(texturing);
        nif.Roots.Add(0);
        return ModelData.ScanShaderTexture(nif, 0);
    }

    [Test]
    public void AShaderMapZeroTextureOnAnObjectCounts()
    {
        Assert.That(Scan(Texturing(true), Obj("NiMesh", new long[0], new long[] { 9 }, "Other")), Is.True);
        Assert.That(Scan(Texturing(true), Obj("NiMesh", new long[0], new long[] { 9 })), Is.True, "no material");
    }

    [Test]
    public void ANodeOwnTexturingPropertyCountsAsWell()
    {
        Assert.That(Scan(Texturing(true), Obj("NiNode", new long[0], new long[] { 9 })), Is.True);
    }

    [Test]
    public void AnObjectWithoutItsOwnShaderMapZeroDoesNotCount()
    {
        Assert.That(Scan(Texturing(false), Obj("NiMesh", new long[0], new long[] { 9 }, "Other")), Is.False);
        Assert.That(Scan(Texturing(false, shader1Only: true), Obj("NiMesh", new long[0], new long[] { 9 })), Is.False);
        Assert.That(Scan(Texturing(true), Obj("NiMesh", new long[0], new long[0], "Other")), Is.False, "a property the mesh does not own");
    }

    [Test]
    public void ABlockWithoutChildrenOrPropertiesFieldsIsSkipped()
    {
        var bare = new NifStruct { Type = "NiMesh" };
        Assert.That(Scan(Texturing(true), bare), Is.False);
        Assert.That(Scan(Texturing(true), bare, Obj("NiMesh", new long[0], new long[] { 9 })), Is.True);
    }

    [Test]
    public void AnySkinMaterialAmongTheMeshMaterialsDisqualifiesIt()
    {
        Assert.That(Scan(Texturing(true), Obj("NiMesh", new long[0], new long[] { 9 }, Skin)), Is.False);
        Assert.That(Scan(Texturing(true), Obj("NiMesh", new long[0], new long[] { 9 }, "Other", Skin)), Is.False, "skin in the second slot");
        Assert.That(Scan(Texturing(true), Obj("NiMesh", new long[0], new long[] { 9 }, Skin),
            Obj("NiMesh", new long[0], new long[] { 9 }, "Other")), Is.True, "any other object counts");
    }

    /// <summary>A loaded model holds neither the parsed file nor a scan result: nothing of the dialog-only test rides on every character,
    /// npc and field object.</summary>
    [Test]
    public void ALoadedModelRetainsNoNifFile()
    {
        var fields = typeof(ModelData).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.That(fields.Where(f => f.FieldType == typeof(NifFile) || f.FieldType.IsSubclassOf(typeof(Delegate))), Is.Empty);
    }

    [Test]
    public void TheCacheParsesAnAssetOnceAndAnswersFromTheNifOnDemand()
    {
        var (packs, spec) = NifEnv.Open();
        var cache = new ModelTextureCache();
        NifFile Load() => NifFile.Load(spec, packs.Read("Resource/Model/Map", "kritias/field/ks_fi_cubric_generator_a01.nif"));
        bool first = cache.Has("a", Load);
        Assert.That(cache.Parses, Is.EqualTo(1));
        Assert.That(cache.Has("a", Load), Is.EqualTo(first));
        Assert.That(cache.Parses, Is.EqualTo(1), "the second ask is a cache hit");
        Assert.That(cache.Has("b", () => null), Is.False, "a file that is not packed answers false");
        Assert.That(cache.Parses, Is.EqualTo(2));
        Assert.That(first, Is.EqualTo(ModelData.HasShaderTexture(Load())));
    }
}
