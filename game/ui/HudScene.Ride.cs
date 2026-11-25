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

    /// <summary>Harness: the main player info's mount portrait. Riding a useRidingUI mount, the controller's UIAvatarRidee is the
    /// ridden row and the movie's load built its view (the row, the size, the eye and target, and how many of its pixels the
    /// model covers); otherwise there is neither.</summary>
    public (bool Ok, string Text) RideeAvatarScripted()
    {
        var p = hudModel?.Player;
        bool expected = p?.Riding == true && p.Ridee is { UseRidingUI: true };
        var row = playerInfo?.AvatarRidee;
        int covered = 0, w = 0, h = 0;
        if (rideeAvatar?.Image?.GetImage() is { } img)
        {
            w = img.GetWidth(); h = img.GetHeight();
            if (img.GetFormat() != Godot.Image.Format.Rgba8) img.Convert(Godot.Image.Format.Rgba8);
            var bytes = img.GetData();
            for (int i = 3; i < bytes.Length; i += 4)
                if (bytes[i] > 25) covered++;
        }
        string view = rideeAvatar == null ? "no view"
            : $"view {rideeAvatar.Row.Id} {w}x{h} eye {rideeAvatar.CameraAt.Eye} at {rideeAvatar.CameraAt.Target}, {covered} pixels covered";
        bool ok = expected
            ? row != null && row.Id == p.Ridee.Id && rideeAvatar != null && rideeAvatar.Row.Id == row.Id && w == 100 && h == 100 && covered > 0
            : row == null && rideeAvatar == null;
        return (ok, $"portrait {(expected ? "expected" : "not expected")}: ridee {row?.Id.ToString() ?? "none"}, {view}");
    }

    /// <summary>Harness: the quick slots against the own skill set. Riding a mount whose set has a row, the key table is locked
    /// and the slots 4..7, 0..3, 8.. show the set's skills in order (CKeyTable::ApplySkillSetToQuickSlots); without a set the
    /// table is unlocked. The text has the set, the lock, the mode 7 sends and the first shown slots.</summary>
    public (bool Ok, string Text) SkillSetScripted()
    {
        var set = hudModel?.SkillSets.Current;
        var keys = hudModel?.ServerKeys;
        if (playerInfo == null || keys == null) return (false, "no quick slots");
        int[] order = { 4, 5, 6, 7, 0, 1, 2, 3, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 };
        bool ok = set == null ? !keys.Locked : keys.Locked;
        if (set != null)
            for (int i = 0; i < set.SkillIds.Count && i < order.Length; i++)
                ok &= playerInfo.ShownEntry(order[i]).SkillId == set.SkillIds[i];
        int shown = 0;
        for (int i = 0; i < 25; i++) if (!playerInfo.ShownEntry(i).IsEmpty) shown++;
        string first = string.Join(",", order.Take(4).Select(i => playerInfo.ShownEntry(i).SkillId));
        return (ok, $"skill set {set?.Id.ToString() ?? "none"} ({(set == null ? "-" : string.Join(",", set.SkillIds))}) locked {keys.Locked} "
            + $"mode 7 sent {keyTableUnlocks} slots 4-7 {first} shown {shown}");
    }

    int keyTableUnlocks;      // KeyTable mode 7 sends of the quick slots (the skill set's release)

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
