using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A skinned NiMesh split into submeshes (skin partitions): each submesh draws its own index region over its own vertex region
/// and skins with its own bone palette region. NiD3DShader::PreRenderSubmesh offsets every vertex stream by the submesh's
/// region start (SetStreamSource, NiD3DShader.cpp:526-566), so an index counts from that region's first vertex, and
/// NiD3DShaderConstantMap (:1627-1636) loads the bone matrices from the BONE_PALETTE region of the submesh. The model is the
/// Safehold NPC Mason (npc 11004316, kfm 11003537_M_Mason): its body mesh has two submeshes of 60 bones each.
/// </summary>
public class SkinSubmeshTests
{
    const string Archive = "Resource/Model/Npc", File = "11/11003537/11003537_m_mason.nif";

    sealed record Region(int Start, int Count);

    /// <summary>The submesh's region of the stream holding <paramref name="semantic"/>, from the file's own tables.</summary>
    static Region RegionOf(NifFile nif, int mesh, string semantic, int submesh)
    {
        foreach (var r in nif.Blocks[mesh].Structs("Datastreams"))
        {
            if (!r.Structs("Component Semantics").Any(c => nif.StrField(c, "Name") == semantic)) continue;
            var reg = nif.Blocks[(int)r.Int("Stream")].Structs("Regions")[(int)r.Ints("Submesh To Region Map")[submesh]];
            return new((int)reg.Int("Start Index"), (int)reg.Int("Num Indices"));
        }
        throw new AssertionException("no " + semantic);
    }

    [Test]
    public void EachSubmeshIndexesItsOwnVerticesAndSkinsWithItsOwnPalette()
    {
        var (packs, spec) = NifEnv.Open();
        if (!packs.Exists(Archive, File)) Assert.Ignore("the data has no Mason model");
        var nif = NifFile.Load(spec, packs.Read(Archive, File));
        var md = ModelData.Load(nif);
        int checkedSubmeshes = 0;
        foreach (var mesh in md.Meshes.Where(m => m.Skinned))
        {
            var b = nif.Blocks[mesh.Block];
            int subs = (int)b.IntOr("Num Submeshes", 1);
            if (subs < 2) continue;
            var streams = ModelData.MeshStreams(nif, mesh.Block);
            var palette = streams["BONE_PALETTE"].v;
            int skin = b.Ints("Modifiers").Select(x => (int)x).First(x => nif.Blocks[x].Type == "NiSkinningMeshModifier");
            var skinBones = nif.Blocks[skin].Ints("Bones");
            int indexAt = 0;
            for (int s = 0; s < subs; s++)
            {
                var vr = RegionOf(nif, mesh.Block, "POSITION_BP", s);
                var ir = RegionOf(nif, mesh.Block, "INDEX", s);
                var pr = RegionOf(nif, mesh.Block, "BONE_PALETTE", s);
                // the triangles of submesh s, in order, on its vertices
                for (int q = 0; q < ir.Count; q++)
                {
                    int v = mesh.Indices[indexAt + q];
                    Assert.That(v, Is.InRange(vr.Start, vr.Start + vr.Count - 1), $"{mesh.Name} submesh {s} index {q}");
                }
                indexAt += ir.Count;
                // every weighted joint of a submesh vertex is a bone of that submesh's palette
                var allowed = new HashSet<string>();
                for (int p = 0; p < pr.Count; p++) allowed.Add(nif.NodeName((int)skinBones[(int)palette[pr.Start + p]]));
                for (int v = vr.Start; v < vr.Start + vr.Count; v++)
                    for (int j = 0; j < 4; j++)
                        if (mesh.Weights[v * 4 + j] > 0)
                            Assert.That(allowed, Does.Contain(md.Bones[mesh.Joints[v * 4 + j]].Name), $"{mesh.Name} submesh {s} vertex {v}");
                checkedSubmeshes++;
            }
            Assert.That(mesh.Indices.Length, Is.EqualTo(indexAt), $"{mesh.Name}: the index list is the submeshes' regions");
        }
        Assert.That(checkedSubmeshes, Is.GreaterThanOrEqualTo(2));
    }
}
