// Adapted from the server fork's navmesh mapper (Maple2.File.Ingest NavMeshMapper, AGPL-3.0-or-later).
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;
using DotRecast.Recast.Toolset.Builder;

namespace Ms2.Navigation;

public static class NavMeshBuilder
{
    /// <summary>Single-tile navmesh from the field geometry; null when nothing walkable came out.</summary>
    /// <summary>Keep Recast's intermediate results (memory only; the navmesh is the same either way).</summary>
    public static bool KeepInterResults = false;

    public static DtNavMesh Build(NavGeometry g)
    {
        if (g.Empty) return null;
        var verts = g.Verts.ToArray();
        var tris = g.Tris.ToArray();
        var geom = new SimpleInputGeomProvider(verts, tris);
        var s = NavConfig.BuildSettings();
        var cfg = new RcConfig(
            partitionType: (RcPartition)s.partitioning, cellSize: s.cellSize, cellHeight: s.cellHeight,
            agentMaxSlope: s.agentMaxSlope, agentHeight: s.agentHeight, agentRadius: s.agentRadius, agentMaxClimb: s.agentMaxClimb,
            regionMinSize: s.minRegionSize, regionMergeSize: s.mergedRegionSize, edgeMaxLen: s.edgeMaxLen, edgeMaxError: s.edgeMaxError,
            vertsPerPoly: s.vertsPerPoly, detailSampleDist: s.detailSampleDist, detailSampleMaxError: s.detailSampleMaxError,
            filterLowHangingObstacles: s.filterLowHangingObstacles, filterLedgeSpans: s.filterLedgeSpans,
            filterWalkableLowHeightSpans: s.filterWalkableLowHeightSpans,
            walkableAreaMod: SampleAreaModifications.SAMPLE_AREAMOD_WALKABLE, buildMeshDetail: true);
        var bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax());
        var ctx = new RcContext();

        var solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, bcfg.cfg.Cs, bcfg.cfg.Ch, bcfg.cfg.BorderSize);
        var areas = Flags(g, cfg, verts);
        RcRasterizations.RasterizeTriangles(ctx, verts, tris, areas, tris.Length / 3, solid, bcfg.cfg.WalkableClimb);

        // the intermediate heightfields are not kept: only the poly and detail meshes go into the navmesh
        var res = new RcBuilder().Build(ctx, bcfg.tileX, bcfg.tileZ, geom, bcfg.cfg, solid, keepInterResults: KeepInterResults);
        if (res.Mesh == null) return null;

        var pm = res.Mesh; var dm = res.MeshDetail;
        for (int i = 0; i < pm.npolys; i++) pm.flags[i] = 1;
        var o = new DtNavMeshCreateParams
        {
            verts = pm.verts, vertCount = pm.nverts, polys = pm.polys, polyAreas = pm.areas, polyFlags = pm.flags,
            polyCount = pm.npolys, nvp = pm.nvp,
            walkableHeight = cfg.WalkableHeightWorld, walkableRadius = cfg.WalkableRadiusWorld, walkableClimb = cfg.WalkableClimbWorld,
            bmin = pm.bmin, bmax = pm.bmax, cs = cfg.Cs, ch = cfg.Ch, buildBvTree = true,
            offMeshConCount = 0, offMeshConVerts = new float[0], offMeshConRad = new float[0], offMeshConDir = new int[0],
            offMeshConAreas = new int[0], offMeshConFlags = new int[0], offMeshConUserID = new int[0],
            tileX = res.TileX, tileZ = res.TileZ,
        };
        if (dm != null)
        {
            o.detailMeshes = dm.meshes; o.detailVerts = dm.verts; o.detailVertsCount = dm.nverts;
            o.detailTris = dm.tris; o.detailTriCount = dm.ntris;
        }
        var data = DtNavMeshBuilder.CreateNavMeshData(o);
        if (data == null) return null;
        DemoNavMeshBuilder.UpdateAreaAndFlags(data);
        var nav = new DtNavMesh();
        return nav.Init(data, NavConfig.VertsPerPoly, 0).Failed() ? null : nav;
    }

    // walkable where the triangle is flat enough; water triangles never are
    static int[] Flags(NavGeometry g, RcConfig cfg, float[] verts)
    {
        var tris = g.Tris;
        var a = g.Areas.ToArray();
        float cos = MathF.Cos(cfg.WalkableSlopeAngle / 180f * MathF.PI);
        var n = default(DotRecast.Core.Numerics.RcVec3f);
        for (int i = 0; i < a.Length; i++)
        {
            if ((a[i] & SampleAreaModifications.SAMPLE_POLYAREA_TYPE_WATER) != 0) { a[i] = 0; continue; }
            RcRecast.CalcTriNormal(verts, tris[i * 3], tris[i * 3 + 1], tris[i * 3 + 2], ref n);
            if (n.Y > cos) a[i] = cfg.WalkableAreaMod.Apply(a[i]);
        }
        return a;
    }

    /// <summary>Writes the navmesh through a temporary file of its own, so two writers of one path never share it.</summary>
    public static void Save(DtNavMesh mesh, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
                new DtMeshSetWriter().Write(bw, mesh, RcByteOrder.LITTLE_ENDIAN, true);
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    public static DtNavMesh Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);
        return new DtMeshSetReader().Read(br, NavConfig.VertsPerPoly);
    }
}
