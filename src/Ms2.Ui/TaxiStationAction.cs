using System;
using System.Collections.Generic;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>
/// TaxiStationAction (action key 8, actionkeyobject.cpp:762-816; registered at :520): the poll that registers a station the
/// player stands by, and the key that opens the world map in taxi mode.
/// <para>vfunc_1 (@0x14095bca0, :769-808) applies only when all of these hold:</para>
/// <list type="bullet">
/// <item>the player is there and alive;</item>
/// <item>it can move (Unk_PCMove::FUN_141683ef0);</item>
/// <item>its state is not StateTaxiCall (0x25);</item>
/// <item>a station (0x85359406) is within TaxiStationFindDistance (WorldService::FUN_141bcfd30, the nearest, 3D distance);</item>
/// <item>for a station with IsHomePrivilege (0xe725ea17), the player's home privilege FUN_14050da30 holds;</item>
/// <item>otherwise the found-taxi list (DAT_143ab3078) holds the current field (DAT_143ab2870).</item>
/// </list>
/// When the found-taxi list doesn't hold the field and the requested list (DAT_143ab3098) doesn't either, the field joins the
/// requested list and RequestTaxi mode 0 goes out once. The action doesn't apply then.
/// The requested list is emptied at the server enter (OnServerEnter, scenestategame.cpp:2115).
/// <para>vfunc_2 (@0x14095bef0, :811-821): the host opens the world map (0x4e42) when closed, calls SetTaxiMode(1) and adds
/// guide trigger 0x11.</para>
/// STILL-OPEN: FUN_141683ef0 (unit_141682f80.cpp:495) is not ported. Its owner kind, input key handler flag, additional
/// effect, PC and camera checks are left to the host's <c>canMove</c>.
/// STILL-OPEN: the home privilege (FUN_14050da30) has no port. An unknown privilege is taken as not held, as for
/// <c>TaxiStations.IsOpen</c>.
/// STILL-OPEN: CAutoFindTaxiStation's adds to the requested list (autofindtaxistation.cpp:298, :378). The auto-find has no port.
/// </summary>
public sealed class TaxiStationAction
{
    /// <summary>The action key (vfunc_5, :766).</summary>
    public const int Key = 8;
    /// <summary>StateTaxiCall's state id (GetCurStateId() == 0x25, :778).</summary>
    public const int StateTaxiCall = 0x25;
    /// <summary>Client::Constants TaxiStationFindDistance's default (clientconstants +0x2c30).</summary>
    public const float DefaultFindDistance = 250.0f;
    /// <summary>The station model (0x85359406).</summary>
    public const string StationModel = "MS2TaxiStation";

    static readonly RequestTaxiRegister Register = new();

    readonly List<int> requested = new();     // DAT_143ab3098
    readonly List<Vec3> stations = new();
    readonly List<bool> house = new();         // the station's IsHomePrivilege (0xe725ea17)

    /// <summary>The station search radius (TaxiStationFindDistance).</summary>
    public float FindDistance = DefaultFindDistance;
    /// <summary>The packet sink (FUN_140710590).</summary>
    public Action<IOutPacket> Send;

    /// <summary>The fields RequestTaxi mode 0 was sent for since the server enter (DAT_143ab3098).</summary>
    public IReadOnlyList<int> Requested => requested;
    public int StationCount => stations.Count;

    /// <summary>OnServerEnter (scenestategame.cpp:2115): DAT_143ab3098.RemoveAll().</summary>
    public void ServerEnter() => requested.Clear();

    /// <summary>The field's stations: the entities of model MS2TaxiStation, with their Position and IsHomePrivilege.</summary>
    public void Attach(IReadOnlyList<FieldEntity> entities)
    {
        stations.Clear();
        house.Clear();
        if (entities == null) return;
        foreach (var e in entities)
        {
            if (!e.HasMixin(StationModel)) continue;
            AddStation(e.Position, string.Equals(e.Get("IsHomePrivilege"), "True", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal void AddStation(Vec3 at, bool homePrivilege) { stations.Add(at); house.Add(homePrivilege); }

    /// <summary>FUN_141bcfd30(0x85359406, at, FindDistance, nearest 1, flat 0): the nearest station strictly inside the
    /// radius, by 3D distance; -1 when none.</summary>
    public int FindStation(Vec3 at)
    {
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < stations.Count; i++)
        {
            var s = stations[i];
            float dx = (float)(at.X - s.X), dy = (float)(at.Y - s.Y), dz = (float)(at.Z - s.Z);
            float d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < FindDistance && d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// vfunc_1: true when the key goes to the taxi. <paramref name="homePrivilege"/>: FUN_14050da30, null when unknown.
    /// <paramref name="found"/>: DAT_143ab3078. <paramref name="field"/>: DAT_143ab2870. Allocates only when the requested
    /// list grows.
    /// </summary>
    public bool Poll(bool hasPlayer, bool dead, bool canMove, int stateId, Vec3 at, bool? homePrivilege, int field,
                     ICollection<int> found)
    {
        if (!hasPlayer || dead || !canMove || stateId == StateTaxiCall) return false;
        int station = FindStation(at);
        if (station < 0) return false;
        if (house[station]) return homePrivilege == true;
        if (found != null && found.Contains(field)) return true;
        if (requested.Contains(field)) return false;
        requested.Add(field);
        Send?.Invoke(Register);
        return false;
    }
}
