using System.Collections.Generic;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CubeMapInformationService's plot tables that LoadCubes modes 1-3 fill (CSceneStateGame::OnLoadCubes @0x141728110,
/// scenestategame.cpp:10902-10944):
/// <list type="bullet">
/// <item>fd8, the plots by number. Mode 1 (FUN_1410bbc00 @0x1410bbc00, cubemapinformationservice_3.cpp:1006-1013) adds
/// an empty entry for a plot that has none and drops the state it was sent.</item>
/// <item>f138, the owners by (plot, block), filled by SetCubeOwner @0x1408a10e0 (ugcmapdata_1.cpp:252-266): a new pair
/// gets a new owner, a known pair's owner is overwritten in place. f148 holds the same owners by owner id; AddOwner
/// @0x1410ce3f0 (:1334-1338) keeps the first owner of an id.</item>
/// <item>fe8, the sale states by (plot, block) (SetSaleState @0x1410c7a50, :2255-2265): the state and its value, made on
/// first use.</item>
/// </list>
/// STILL-OPEN: the plot entry's cube set (fd8's value, filled as cubes are made) and the sale sign each mode refreshes
/// (FUN_1410bbcd0, :3160-3181, the sign entity's asset; a visual).
/// </summary>
public sealed class CubePlots
{
    /// <summary>A CubeOwner (ugcmapdata): the plot, the block, the owner's id and name.</summary>
    public sealed class Owner
    {
        public int Plot;
        public int Block;
        public long OwnerId;
        public string Name;
    }

    readonly HashSet<int> plots = new();
    readonly Dictionary<(int Plot, int Block), Owner> owners = new();
    readonly Dictionary<long, Owner> ownersById = new();
    readonly Dictionary<(int Plot, int Block), (byte State, long Value)> sales = new();

    public bool HasPlot(int plot) => plots.Contains(plot);
    public int PlotCount => plots.Count;

    /// <summary>FindCubeOwner @0x1408a0fa0 (ugcmapdata_1.cpp:241-247): the owner of the pair, or null.</summary>
    public Owner FindOwner(int plot, int block) => owners.TryGetValue((plot, block), out var o) ? o : null;

    /// <summary>f148: the owner kept for an owner id, or null.</summary>
    public Owner FindOwnerById(long ownerId) => ownersById.TryGetValue(ownerId, out var o) ? o : null;
    public int OwnerIdCount => ownersById.Count;

    /// <summary>fe8: the pair's sale state and value, or null.</summary>
    public (byte State, long Value)? SaleState(int plot, int block) => sales.TryGetValue((plot, block), out var s) ? s : null;

    /// <summary>OnLoadCubes modes 1-3 (:10902-10944), in the packet's order.</summary>
    public void Apply(LoadCubesPlots m)
    {
        if (m == null) return;
        foreach (var (plot, _) in m.States) plots.Add(plot);
        foreach (var o in m.Owners)
        {
            var owner = FindOwner(o.Plot, o.Block);
            if (owner == null) owners[(o.Plot, o.Block)] = owner = new Owner();
            owner.Plot = o.Plot;
            owner.Block = o.Block;
            owner.OwnerId = o.OwnerId;
            owner.Name = o.Name;
            ownersById.TryAdd(owner.OwnerId, owner);
        }
        foreach (var s in m.Sales) sales[(s.Plot, s.Block)] = (s.State, s.Value);
    }
}
