using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The local player's UserUGCMapInfo (MyPC +0x1618, GetUGCMapInfo / SetUGCMapInfo) as ResponseCube mode 0x14 fills it
/// (scenestategame.cpp:10199-10244). Empty until the first info for the local object arrives.
/// </summary>
public sealed class OwnHome
{
    /// <summary>MyPC::GetUGCMapInfo() != 0.</summary>
    public bool HasInfo => Info != null;
    public ResponseCubeHomeInfo Info { get; private set; }

    /// <summary>MyPC::HasNoHome (mypc.cpp:2075): no info, or its +0x30 value is 0.</summary>
    public bool HasNoHome => Info == null || Info.HomeId == 0;

    /// <summary>The plot id (+0x1c); 0 without info.</summary>
    public int PlotId => Info?.PlotId ?? 0;

    /// <summary>The open type (+0x40); null without info.</summary>
    public int? OpenType => Info?.OpenType;

    /// <summary>The nResult of the last applied info (the taxi refresh reads it).</summary>
    public byte Result { get; private set; }

    /// <summary>
    /// The 0x14 case (:10205-10224): another object's info changes nothing (false). For the local object the info is created when
    /// missing; otherwise it counts as changed when the player had a home and its +0x30 value differs (bChanged). Then CopyFrom.
    /// <paramref name="changed"/> is what the client hands UIUGCEventDispatcher::vfunc_7.
    /// </summary>
    public bool Apply(ResponseCubeHomeInfo m, int localObjectId, out bool changed)
    {
        changed = false;
        if (m == null || m.OwnerObjectId != localObjectId) return false;
        if (Info != null && !HasNoHome && Info.HomeId != m.HomeId) changed = true;
        Info = m;
        Result = m.Result;
        return true;
    }
}
