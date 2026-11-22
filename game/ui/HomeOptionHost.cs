using System;
using Ms2.Formats.Model;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The home option dialog's outside (<see cref="IHomeOptionHost"/>): the cube map service's rows (<see cref="Net.LiveSession.HomeRows"/>,
/// ResponseCube 0x2a / 0x2b), CanChange (CubeMapInformationService::FUN_1410d5640, cubemapinformationservice_3.cpp:1186) and the home's
/// open type (<see cref="Net.LiveSession.Home"/>). The service exists in the game scene (RegisterServices), taken here as a field being loaded.
/// STILL-OPEN: the HomeOptionDialog movie that would use it is not ported, so nothing creates this host yet; the service owner (f1c0) and f330
/// are <see cref="Ms2.Net.CubeMapInfo"/>'s Owner and PlotMode, set from LoadUgcMap's nOwner and nF (scenestategame.cpp:2252, 2258).
/// </summary>
sealed class HomeOptionHost(Net.LiveSession live, GameOptionHost options, Func<int, bool> fieldIsHome) : IHomeOptionHost
{
    public bool HasService => live.FieldId != null;
    /// <summary>FUN_1410d2f10 (CFieldData::FUN_140374410 of the field id: map type 3), FUN_1410c35c0, FUN_1410c3180 == DAT_143ab2820.</summary>
    public bool CanChange => live.FieldId is int id
        && Ms2.Ui.HomeOptionRows.CanChange(fieldIsHome(id), live.CubeMap.PlotMode == 1, live.CubeMap.Owner, live.OwnAccountId);
    public int? OpenType => live.Home.OpenType;
    public bool RowOn(int row) => live.HomeRows.RowOn(row);
    public int RowLevel(int row) => live.HomeRows.RowLevel(row);
    public bool SendHouseSetting(int kind, string value, bool flag)
    {
        if (live.SelfObjectId < 0) return false;
        options.SendHouseSetting(kind, value, flag);
        return true;
    }
    public void SendRow(int row, bool on, int level) => live.SendHomeOptionRow(row, on, level);

    /// <summary>CFieldData::FUN_140374410: the field's map type is 3 (home).</summary>
    public static bool FieldIsHome(Ms2.Formats.Pack.PackSet packs, int id) => Ms2.Formats.World.FieldData.FindById(packs, id)?.MapType == 3;
}
