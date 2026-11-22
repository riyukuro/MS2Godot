using Ms2.Formats.Model;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>FUN_141422f30 (@0x141422f30, itemugcmesh.cpp:455): a non-skin-material mesh with a shader map 0 source texture.</summary>
public class ModelShaderTextureTests
{
    static ModelMesh Mesh(string material, params string[] textures)
    {
        var m = new ModelMesh { Material = material };
        foreach (var t in textures) m.Textures[t] = "x.dds";
        return m;
    }

    static ModelData Of(params ModelMesh[] meshes)
    {
        var d = new ModelData();
        d.Meshes.AddRange(meshes);
        return d;
    }

    [Test]
    public void AShaderMapZeroTextureOnANonSkinMeshCounts()
    {
        Assert.That(Of(Mesh(null, "base", "shader0")).HasShaderTexture(), Is.True);
        Assert.That(Of(Mesh("Other", "shader0")).HasShaderTexture(), Is.True);
    }

    [Test]
    public void TheSkinMaterialTheOtherShaderMapsAndNoMeshDoNotCount()
    {
        Assert.That(Of(Mesh("MS2CharacterSkinMaterial", "shader0")).HasShaderTexture(), Is.False);
        Assert.That(Of(Mesh(null, "base", "shader1")).HasShaderTexture(), Is.False);
        Assert.That(Of().HasShaderTexture(), Is.False);
        Assert.That(Of(Mesh("MS2CharacterSkinMaterial", "shader0"), Mesh(null, "shader0")).HasShaderTexture(), Is.True, "any mesh");
    }
}
