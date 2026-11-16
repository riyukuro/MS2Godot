// The navmesh settings and the map-space <-> navmesh-space mapping follow the server fork's navmesh generator
// (Maple2.Tools DotRecastHelper, AGPL-3.0-or-later), so a mesh built here has the shape the server walks on.
// Adapted for MS2Godot; this file stays under AGPL-3.0-or-later like the rest of the repository.
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Toolset;

namespace Ms2.Navigation;

public static class NavConfig
{
    public const int VertsPerPoly = 6;
    public const float CellSize = 0.05f;

    /// <summary>Bumped when the build parameters or the geometry rules change; part of the cache key.</summary>
    public const int Version = 1;

    public static RcNavMeshBuildSettings BuildSettings() => new()
    {
        cellSize = CellSize,
        cellHeight = CellSize,
        agentHeight = 1.4f,
        agentRadius = 0.3f,
        agentMaxClimb = 0.7f,
        agentMaxSlope = 47f,
        agentMaxAcceleration = 8f,
        agentMaxSpeed = 3.5f,
        minRegionSize = 8,
        mergedRegionSize = 20,
        partitioning = (int)RcPartition.WATERSHED,
        filterLowHangingObstacles = true,
        filterLedgeSpans = true,
        filterWalkableLowHeightSpans = true,
        edgeMaxLen = 12f,
        edgeMaxError = 1.3f,
        vertsPerPoly = VertsPerPoly,
        detailSampleDist = 6f,
        detailSampleMaxError = 3f,
        keepInterResults = true,
    };

    /// <summary>Half extents of the box used to find the poly under a point (navmesh units).</summary>
    public static readonly RcVec3f NearestExtents = new(2, 4, 2);

    /// <summary>Map space (Z up, 1 unit = 1 cm-ish file unit) -> navmesh space (Y up, /100): (x, y, z) -> (x, z, -y) / 100.</summary>
    public static RcVec3f ToNav(double x, double y, double z) => new((float)(x / 100), (float)(z / 100), (float)(-y / 100));

    public static (double x, double y, double z) FromNav(RcVec3f p) => (p.X * 100.0, -p.Z * 100.0, p.Y * 100.0);
}
