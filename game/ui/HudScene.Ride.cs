using System;

namespace Ms2Godot.Ui;

/// <summary>
/// The HUD's side of the own ride: MyPC::vfunc_120 closes the windows a mount with hidePCInfo above 1 hides
/// (mypc.cpp:3306-3314) and both MyPC::vfunc_120 and vfunc_121 send the riding status event 0xc (:3317, :3365), which the
/// main player info, the exp bars, the game menu and the minimap take (<see cref="Ms2.Ui.RidingUi"/>). CKeyTable's gate
/// refuses the inventory, character and skill book keys while the mount hides the player info (keytable.cpp:2629-2648).
/// </summary>
public partial class HudScene
{
    /// <summary>The own ride on (the row) or off (null) from the live session.</summary>
    void OnOwnRide(Ms2.Ui.RideeRow row)
    {
        if (row != null)
            foreach (int id in row.ClosesOnRide) CloseOnRide(id);
        SetOwnRide(hudModel, row);
        // PCBase::vfunc_120 / vfunc_121 refresh the own name tag (vfunc_82()->FUN_140f48d80, pcbase.cpp:3335-3339)
        playerNameTag?.SetName(OwnTagName());
        hudModel.Notify(Ms2.Ui.StatusEvent.Riding);
    }

    /// <summary>PC::vfunc_77 (pc.cpp:1528-1538), the own tag's name: the mount's name (string table 0x31) while riding a
    /// row with showRideeNameTag, else the player's.</summary>
    string OwnTagName() => hudModel.Player.Riding && hudModel.Player.Ridee is { ShowRideeNameTag: true } r
        ? hudModel.Tables.StringTableText(0x31, r.Id) : hudModel.Player.Name;

    static void SetOwnRide(Ms2.Ui.UiModel model, Ms2.Ui.RideeRow row)
    {
        model.Player.Riding = row != null;
        model.Player.Ridee = row;
    }

    /// <summary>CUIService::CloseDialog of the ids <see cref="Ms2.Ui.RideeRow.ClosedOnRide"/> names, for the windows the HUD
    /// has: the inventory, the skill book, the other player's and the own character window. The closet (0x4f76) and the
    /// skill preview (0x4ec3) are not ported windows.</summary>
    void CloseOnRide(int id)
    {
        switch (id)
        {
            case 0x4e2d: if (inventory?.IsClosed == false) inventory.Close(); break;
            case 0x4f2d: if (skillBook?.IsClosed == false) skillBook.Close(); break;
            case 0x4ec8: if (PcInfoOpen) pcInfo.Close(); break;
            case 0x2711: if (myInfo?.IsClosed == false) myInfo.Close(); break;
        }
    }

    /// <summary>CKeyTable::FUN_141432010 (keytable.cpp:2629-2648) before ExecuteActionKey's toggle: a refused action shows
    /// s_hide_pcinfo_dialog_disable_message (ShowSystemNotice(text, 1, 1)) and runs nothing.</summary>
    bool RideRefusesKey(int action)
    {
        if (Ms2.Ui.RidingUi.KeyActionAllowed(hudModel.Player.Riding ? hudModel.Player.Ridee : null, action)) return false;
        SystemNotice(Ms2.Ui.RideeRow.RefusedNoticeKey);
        return true;
    }

    /// <summary>CQuestNavigator::CanShowBeam (questnavigator.cpp:468-481) past its sub scene test: a local player; a riding
    /// one only when its mount's default rider has useRidingNavi (<see cref="Ms2.Ui.RidingUi.RidingBeam"/>).</summary>
    bool QuestBeamAllowed() =>
        player != null && (player.Mount == null || Ms2.Ui.RidingUi.RidingBeam(player.Mount.Data?.UiRow));

    /// <summary>The riding HUD as a live run checks it: the model's ride, and whether the main player info, the exp bar
    /// and the game menu show, and what the own tag says.</summary>
    public string RideHudScripted()
    {
        static bool Shown(Ms2.Ui.UiHost h) => h?.Entity is { } e && Ms2.Avm2.Rt.ToBoolean(h.Get(e, "visible"));
        var p = hudModel?.Player;
        return $"riding {p?.Riding} ridee {p?.Ridee?.Id.ToString() ?? "none"} hidePCInfo {p?.Ridee?.HidePCInfo ?? 0} useRidingUI {p?.Ridee?.UseRidingUI ?? false}"
            + $" main info {Shown(playerInfo?.Host)} exp bar {Shown(expBarMovie?.Host)} game menu {Shown(gameMenu?.Host)} tag '{playerNameTag?.Shown}'";
    }

    /// <summary>Harness: closes the level-up guide window, which opens over the field after a warp and hides the shots.</summary>
    public bool ClearShotScripted()
    {
        if (levelUpGuide is not { IsClosed: false }) return false;
        levelUpGuide.Close();
        return true;
    }

    /// <summary>The mount an item summons (ItemData's ridee id, item type 9), 0 for another item.</summary>
    public int ItemRideeScripted(int itemId) => hudModel?.Tables?.Items?.Get(itemId)?.RideeId ?? 0;
}
