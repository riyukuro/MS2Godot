using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A NiMesh of Primitive Type 1 (PRIMITIVE_TRISTRIPS): the DX9 renderer draws each submesh's INDEX region as one
/// D3DPT_TRIANGLESTRIP (NiDX9Renderer.cpp:4857) of count - 2 triangles (NiMesh::GetPrimitiveCountFromElementCount), the
/// engine walking triangle t as (s[t], s[t+1], s[t+2]) with the first two swapped on odd t (NiMeshAlgorithms.inl:533-575).
/// The models are the walrus NPC (21000825_m_walrusnormal, one strip) and the gargoyle NPC (21000988_m_gargoylenormal, a
/// skinned strip in two submeshes), and the gargoyle iron mace attack effect whose mesh emitters take a strip.
/// </summary>
public class TriStripMeshTests
{
    const string Npc = "Resource/Model/Npc";
    const string Walrus = "21/21000825/21000825_m_walrusnormal.nif", Gargoyle = "21/21000988/21000988_m_gargoylenormal.nif";
    const string Mason = "11/11003537/11003537_m_mason.nif";
    const string Effect = "Resource/Model/Effect", MaceAttack = "npc/skill/gargoylemadriasoldier/eff_gargoyleironmace_attack_02_c_02.nif";

    /// <summary>The share of a mesh's triangles whose face normal points the same way as its vertices' normals: the
    /// winding's sign, read against the file's own normals.</summary>
    static double FacingShare(ModelMesh m)
    {
        int agree = 0, n = 0;
        for (int t = 0; t + 2 < m.Indices.Length; t += 3)
        {
            int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
            double ux = m.Positions[b * 3] - m.Positions[a * 3], uy = m.Positions[b * 3 + 1] - m.Positions[a * 3 + 1], uz = m.Positions[b * 3 + 2] - m.Positions[a * 3 + 2];
            double vx = m.Positions[c * 3] - m.Positions[a * 3], vy = m.Positions[c * 3 + 1] - m.Positions[a * 3 + 1], vz = m.Positions[c * 3 + 2] - m.Positions[a * 3 + 2];
            double fx = uy * vz - uz * vy, fy = uz * vx - ux * vz, fz = ux * vy - uy * vx;
            double nx = 0, ny = 0, nz = 0;
            foreach (int v in new[] { a, b, c }) { nx += m.Normals[v * 3]; ny += m.Normals[v * 3 + 1]; nz += m.Normals[v * 3 + 2]; }
            double d = fx * nx + fy * ny + fz * nz;
            if (d == 0) continue;
            n++;
            if (d > 0) agree++;
        }
        return n == 0 ? 0.5 : (double)agree / n;
    }

    /// <summary>The submesh's (start, count) in the stream holding <paramref name="semantic"/>, from the file's own tables.</summary>
    static (int Start, int Count) RegionOf(NifFile nif, int mesh, string semantic, int submesh)
    {
        foreach (var r in nif.Blocks[mesh].Structs("Datastreams"))
        {
            if (!r.Structs("Component Semantics").Any(c => nif.StrField(c, "Name") == semantic)) continue;
            var reg = nif.Blocks[(int)r.Int("Stream")].Structs("Regions")[(int)r.Ints("Submesh To Region Map")[submesh]];
            return ((int)reg.Int("Start Index"), (int)reg.Int("Num Indices"));
        }
        throw new AssertionException("no " + semantic);
    }

    [TestCase(Walrus, 1)]
    [TestCase(Gargoyle, 2)]
    public void AStripMeshLoadsAsItsStripsTrianglesWoundLikeAListMesh(string file, int submeshes)
    {
        var (packs, spec) = NifEnv.Open();
        if (!packs.Exists(Npc, file) || !packs.Exists(Npc, Mason)) Assert.Ignore("the data has no " + file + " or Mason model");
        var nif = NifFile.Load(spec, packs.Read(Npc, file));
        var md = ModelData.Load(nif);   // threw "only triangle lists" before
        Assert.That(md.Warnings, Is.Empty);
        var strips = md.Meshes.Where(m => nif.Blocks[m.Block].Int("Primitive Type") == 1).ToList();
        Assert.That(strips, Is.Not.Empty, "the model has a strip mesh");
        Assert.That(strips.Max(m => nif.Blocks[m.Block].IntOr("Num Submeshes", 1)), Is.EqualTo(submeshes));
        foreach (var m in strips)
        {
            int subs = (int)nif.Blocks[m.Block].IntOr("Num Submeshes", 1);
            string positions = m.Skinned ? "POSITION_BP" : "POSITION";
            var index = ModelData.MeshStreams(nif, m.Block)["INDEX"].v;
            // each submesh's region is one strip over its own vertices; its triangles with three different vertices, in order
            var expected = new List<int>();
            for (int s = 0; s < subs; s++)
            {
                var (start, count) = RegionOf(nif, m.Block, "INDEX", s);
                int vs = RegionOf(nif, m.Block, positions, s).Start;
                for (int t = 0; t < count - 2; t++)
                {
                    int x = (int)index[start + t] + vs, y = (int)index[start + t + 1] + vs, z = (int)index[start + t + 2] + vs;
                    if (x == y || y == z || x == z) continue;
                    if (t % 2 == 0) expected.AddRange(new[] { x, y, z }); else expected.AddRange(new[] { y, x, z });
                }
            }
            Assert.That(m.Indices, Is.EqualTo(expected.ToArray()), $"{m.Name}: the strip's triangles");
            Assert.That(m.Indices, Is.All.InRange(0, m.Positions.Length / 3 - 1));
            if (m.Normals != null)
                Assert.That(Math.Abs(FacingShare(m) - 0.5), Is.GreaterThan(0.4), $"{m.Name}: the strip's triangles all face one way");
        }
        // the winding: the strip's triangles face the way a triangle-list mesh's do against the file's normals
        var mason = ModelData.Load(NifFile.Load(spec, packs.Read(Npc, Mason)));
        double listShare = FacingShare(mason.Meshes.Where(m => m.Normals != null).OrderByDescending(m => m.Indices.Length).First());
        double stripShare = FacingShare(strips.Where(m => m.Normals != null).OrderByDescending(m => m.Indices.Length).First());
        Assert.That(Math.Abs(listShare - 0.5), Is.GreaterThan(0.4), "a list mesh's triangles all face one way");
        Assert.That(stripShare > 0.5, Is.EqualTo(listShare > 0.5), $"strip facing {stripShare:F3}, list facing {listShare:F3}");
    }

    /// <summary>A mesh emitter on a strip picks among the strip's count - 2 triangles, read as NiPSMeshEmitter::ComputeIndices
    /// reads them through the strip index iterator, which reverses odd windings (NiDataStreamPrimitiveLock.inl:35-39,
    /// NiTPrimitiveRandomAccessIterator.h:79-93): triangle t is (s[t], s[t+1], s[t+2]) on even t and (s[t+1], s[t], s[t+2]) on
    /// odd t, degenerate ones kept for its pick to step past.</summary>
    [Test]
    public void AMeshEmitterOnAStripTakesTheStripsTriangles()
    {
        var (packs, spec) = NifEnv.Open();
        if (!packs.Exists(Effect, MaceAttack)) Assert.Ignore("the data has no gargoyle iron mace attack effect");
        var nif = NifFile.Load(spec, packs.Read(Effect, MaceAttack));
        var sc = EffectScene.Load(nif);
        var meshBlocks = nif.Blocks.Where(b => b.Type == "NiPSMeshEmitter").SelectMany(b => b.Ints("Mesh Emitters")).Select(x => (int)x).Distinct().ToList();
        Assert.That(meshBlocks, Has.Count.EqualTo(1), "both emitters take one mesh");
        int meshBlock = meshBlocks[0];
        Assert.That(nif.Blocks[meshBlock].Int("Primitive Type"), Is.EqualTo(1));
        Assert.That(nif.Blocks[meshBlock].IntOr("Num Submeshes", 1), Is.EqualTo(1));
        var index = ModelData.MeshStreams(nif, meshBlock)["INDEX"].v;
        var (start, count) = RegionOf(nif, meshBlock, "INDEX", 0);
        var expected = new int[(count - 2) * 3];
        for (int t = 0; t < count - 2; t++)
        {
            int odd = t & 1;
            expected[t * 3] = (int)index[start + t + odd];
            expected[t * 3 + 1] = (int)index[start + t + 1 - odd];
            expected[t * 3 + 2] = (int)index[start + t + 2];
        }
        var emitters = sc.Systems.SelectMany(s => s.Emitters).Where(e => e.Kind == EmitterKind.Mesh).ToList();
        Assert.That(emitters, Has.Count.EqualTo(2), "the effect's two mesh emitters");
        foreach (var e in emitters)
            Assert.That(e.MeshTriangles, Is.EqualTo(expected), "the strip's triangles, as the emitter reads them");
    }

    /// <summary>Strip index regions without matching vertex regions: each region is still its own strip, counted from vertex 0,
    /// so no triangle bridges the end of one submesh's strip and the start of the next.</summary>
    [Test]
    public void StripRegionsWithoutVertexRegionsStayApart()
    {
        double[] index = { 0, 1, 2, 3, 4, 5, 6 };
        var regions = new[] { (0, 4), (4, 3) };
        int[] expected = { 0, 1, 2, 2, 1, 3, 4, 5, 6 };
        Assert.That(ModelData.SubmeshIndices(index, regions, null, strips: true), Is.EqualTo(expected));
        Assert.That(ModelData.SubmeshIndices(index, regions, new[] { (0, 7) }, strips: true), Is.EqualTo(expected), "a vertex region count that does not match");
        Assert.That(ModelData.SubmeshIndices(index, null, null, strips: true), Is.EqualTo(new[] { 0, 1, 2, 2, 1, 3, 2, 3, 4, 4, 3, 5, 4, 5, 6 }), "no regions: the stream is one strip");
    }

    /// <summary>The load warns where it reads on an assumption: Mason as shipped has none; a second vertex stream with other
    /// submesh regions, or two submeshes over the same vertices with different palettes, each gives its warning.</summary>
    [Test]
    public void TheLoadWarnsWhenAVertexStreamOrAPaletteBreaksTheRegionRead()
    {
        var (packs, spec) = NifEnv.Open();
        if (!packs.Exists(Npc, Mason)) Assert.Ignore("the data has no Mason model");
        Assert.That(ModelData.Load(NifFile.Load(spec, packs.Read(Npc, Mason))).Warnings, Is.Empty);

        NifStruct VertexRef(NifFile nif, out NifStruct body)
        {
            body = nif.Blocks.First(b => b.Type == "NiMesh" && b.IntOr("Num Submeshes", 1) == 2);
            return body.Structs("Datastreams").First(r => r.Structs("Component Semantics").Any(c => nif.StrField(c, "Name") == "POSITION_BP"));
        }
        // a copy of the vertex stream ref, after the real one, with the submeshes' regions swapped
        var swapped = NifFile.Load(spec, packs.Read(Npc, Mason));
        var vref = VertexRef(swapped, out var mesh);
        var copy = new NifStruct(); foreach (var kv in vref) copy[kv.Key] = kv.Value;
        copy["Submesh To Region Map"] = vref.Ints("Submesh To Region Map").Reverse().ToArray();
        mesh["Datastreams"] = ((object[])mesh["Datastreams"]).Append(copy).ToArray();
        Assert.That(ModelData.Load(swapped).Warnings, Has.One.Contains("other submesh regions"));

        // both submeshes on the first vertex region, under their two palettes
        var shared = NifFile.Load(spec, packs.Read(Npc, Mason));
        VertexRef(shared, out _)["Submesh To Region Map"] = new long[] { 0, 0 };
        Assert.That(ModelData.Load(shared).Warnings, Has.One.Contains("different bone palettes"));
    }
}
