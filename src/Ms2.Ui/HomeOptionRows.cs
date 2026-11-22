using System.Collections.Generic;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The home option rows CubeMapInformationService keeps: the on/off map at +0x368 (FUN_1410c3e00 reads, FUN_1410c3e40 writes) and
/// the level map at +0x378 (FUN_1410c3e80 / FUN_1410c3f10), as ResponseCube 0x2a / 0x2b fill them (scenestategame.cpp:10619-10633).
/// A row never sent reads as off and level 0 (the on map's operator[] inserts a 0; the level getter returns 0 when the key is missing,
/// cubemapinformationservice_3.cpp:1076-1085). The service's ctor and its reset put rows 0 to 8 at off and 0 (:196, :5455-5475).
/// STILL-OPEN: OnLoadUgcMap (@0x14170e800, scenestategame.cpp:2168) fills both maps from the field's cube map packet too; that packet
/// is not read, so a home entered fresh shows every row off until a 0x2a / 0x2b arrives.
/// </summary>
public sealed class HomeOptionRows
{
    readonly Dictionary<int, bool> on = new();
    readonly Dictionary<int, int> level = new();

    /// <summary>FUN_1410c3e00(row).</summary>
    public bool RowOn(int row) => on.TryGetValue(row, out var v) && v;

    /// <summary>FUN_1410c3e80(row): 0 when the row was never set.</summary>
    public int RowLevel(int row) => level.TryGetValue(row, out var v) ? v : 0;

    public void Apply(ResponseCubeRowSwitch m) { if (m != null) on[m.Row] = m.On; }
    public void Apply(ResponseCubeRowLevel m) { if (m != null) level[m.Row] = m.Level; }

    /// <summary>The service's reset (:5455-5475): rows 0 to 8 off and level 0. f330 and the owner (f1c0) live in <see cref="Ms2.Net.CubeMapInfo"/> (PlotMode, Owner), which OnLoadUgcMap fills and the field teardown resets.</summary>
    public void Reset()
    {
        on.Clear(); level.Clear();
        for (int i = 0; i < 9; i++) { on[i] = false; level[i] = 0; }
    }

    /// <summary>
    /// CubeMapInformationService::FUN_1410d5640 (@0x1410d5640, :1186), UIHomeOptionDialog's CanChange: the field is a home map
    /// (CFieldData::FUN_140374410, m_nMapType 3), the service's f330 is not 1 (FUN_1410c35c0), and the service's owner
    /// (FUN_1410c3180) is the local account (DAT_143ab2820). Checked in that order.
    /// </summary>
    public static bool CanChange(bool fieldIsHome, bool f330IsOne, long serviceOwner, long ownAccount)
    {
        if (!fieldIsHome) return false;
        if (f330IsOne) return false;
        return serviceOwner == ownAccount;
    }
}
