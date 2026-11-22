using System;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The home menu's outside (<see cref="IHomeMenuHost"/>): the cube map service's state (<see cref="Ms2.Net.CubeMapInfo"/>: PlotMode is f330,
/// Owner is f1c0), the own player's cube craft (<see cref="Net.LiveSession.CubeCraft"/>) and the home sends. Built by HudScene.OpenHomeMenu.
/// STILL-OPEN: MyPC::FUN_14152feb0(1, 0) (the craft mode on), SendRequestMoveFieldMode3 / 4 / 5, CubeMapInformationService::FUN_1410d5530
/// (the design has changes) and CountCubes, MyPC::FUN_14152f3c0 (the demolish prelude) and the camera of SetFreeCamera (CameraMgr mode 6) are not
/// ported: they do nothing here, and the design has no changes, no cubes, and no free camera. The content shutdown flags (0x65, 0x66) are not
/// ported either: nothing is shut down.
/// </summary>
sealed class HomeMenuHost(Net.LiveSession live, Func<int, bool> fieldIsHome, Func<int, bool> feature, Action<int> toggleDialog,
    Action<string> notice) : IHomeMenuHost
{
    public bool HasPlayer => live.SelfObjectId >= 0;
    public bool InCubeMode => live.CubeCraft.InCubeMode;
    public int PlotMode => live.CubeMap.PlotMode;
    /// <summary>FUN_1410d5640 (see <see cref="HomeOptionHost.CanChange"/>).</summary>
    public bool CanChange => live.FieldId is int id
        && Ms2.Ui.HomeOptionRows.CanChange(fieldIsHome(id), live.CubeMap.PlotMode == 1, live.CubeMap.Owner, live.OwnAccountId);
    /// <summary>FUN_1410d56a0: the same with the f330 == 1 test inverted.</summary>
    public bool CanEditHome => live.FieldId is int id
        && Ms2.Ui.HomeOptionRows.CanChange(fieldIsHome(id), live.CubeMap.PlotMode != 1, live.CubeMap.Owner, live.OwnAccountId);
    public bool OwnsHome => live.OwnAccountId != 0 && live.CubeMap.Owner == live.OwnAccountId;
    public bool InOwnersHouse => live.Housing.InOwnersHouse;
    public bool BlueprintAvailable(bool message) => feature(0xe9);
    public bool BlueprintShopAvailable(bool message) => feature(0xec);
    public void ToggleDialog(int uiId) => toggleDialog(uiId);
    public bool SendCraftModeOff() => live.SendCraftModeOff();
    public void EnterCraftMode() { }
    public void SendRequestMoveFieldMode(int mode) { }
    public bool DesignHasChanges => false;
    public int CubeCount => 0;
    public bool FeatureEnabled(int id) => feature(id);
    public void BeforeDemolish() { }
    public void SendDemolish() => live.SendHomeDemolish();
    public void SetFreeCamera(bool on) { }
    public bool FreeCameraActive => false;
}
