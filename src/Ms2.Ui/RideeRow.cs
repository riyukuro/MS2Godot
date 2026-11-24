namespace Ms2.Ui;

/// <summary>
/// What the HUD reads of the own mount's riding row (CRideeData, Xml riding/%08d.xml &lt;basic&gt;): its id, hidePCInfo,
/// useRidingUI, enableRideOffUI, showRideeNameTag, and the default rider's nameTagHide and useRidingNavi.
/// </summary>
public sealed record RideeRow(int Id, int HidePCInfo, bool UseRidingUI, bool EnableRideOffUI, bool ShowRideeNameTag,
    bool NameTagHide, bool UseRidingNavi)
{
    /// <summary>MyPC::vfunc_120 (mypc.cpp:3306-3314): with hidePCInfo above 1 the ride-on closes the inventory (0x4e2d), the
    /// skill book (0x4f2d), the other player's character window (0x4ec8), the own character window (0x2711), 0x4f76 and
    /// the skill preview (0x4ec3), in this order.</summary>
    public static readonly int[] ClosedOnRide = { 0x4e2d, 0x4f2d, 0x4ec8, 0x2711, 0x4f76, 0x4ec3 };

    /// <summary>The windows the ride-on closes: <see cref="ClosedOnRide"/> for hidePCInfo above 1, else none.</summary>
    public IReadOnlyList<int> ClosesOnRide => HidePCInfo > 1 ? ClosedOnRide : Array.Empty<int>();

    /// <summary>The key actions CKeyTable::FUN_141432010 refuses while the mount hides the player info (keytable.cpp:2629-2648):
    /// 400001 (0x61a81, the inventory), 400002 (0x61a82, the character window) and 400005 (0x61a85, the skill book).</summary>
    public static readonly int[] RefusedActions = { 0x61a81, 0x61a82, 0x61a85 };

    /// <summary>The notice the refused key shows (ShowSystemNotice(GetStringCommonByKey(...), 1, 1)).</summary>
    public const string RefusedNoticeKey = "s_hide_pcinfo_dialog_disable_message";
}

/// <summary>
/// The HUD's reads of the own ride (MyPC::IsRiding and GetRidee()->GetData()), each listener as the client has it; the
/// ride sends status event 0xc (NotifyStatusChanged(0xc, 1) at the end of MyPC::vfunc_120, mypc.cpp:3317, and (0xc, 0)
/// after PC::vfunc_121, :3365) to UIMainPlayerInfo, UIExpBar, UIAdventureLevelExpBar, UIGameMenuDialog and
/// UIMinimapDialog (their vfunc_oa0_20, uistatuseventlistener.cpp:335-338).
/// </summary>
public static class RidingUi
{
    /// <summary>The player info bars' visibility (UIExpBar::FUN_141932430, uiexpbar.cpp:168-184;
    /// UIAdventureLevelExpBar::FUN_140ba7130, uiadventurelevelexpbar.cpp:182-196), the minimap's players
    /// (UIMinimapDialog::UpdatePlayerVisibility, uiminimapentity.cpp:2395-2408), the game menu's enable
    /// (UIGameMenuDialog::FUN_141939ec0, uigamemenudialog.cpp:1543-1555) and the main player info's tag
    /// (FUN_1419f16b0's visibleTag, uimainplayerinfo.cpp:1025-1042): hidden while the ridee's hidePCInfo is set.</summary>
    public static bool PcInfoShown(RideeRow ridee) => ridee is not { HidePCInfo: not 0 };

    /// <summary>UIMainPlayerInfo::FUN_1419f16b0 (uimainplayerinfo.cpp:1025-1045): the main player info itself is shown
    /// (SetHideReason(4, bShow)) unless the ridee's hidePCInfo is 2; vfunc_oa0_20 stops there when it is not.</summary>
    public static bool MainInfoShown(RideeRow ridee) => ridee is not { HidePCInfo: 2 };

    /// <summary>UIMainPlayerInfo::vfunc_oa0_20 (uimainplayerinfo.cpp:795-835): slot mode 1 (the riding slots) while
    /// riding a mount whose useRidingUI is set, else 0.</summary>
    public static int SlotMode(bool riding, RideeRow ridee) => riding && ridee is { UseRidingUI: true } ? 1 : 0;

    /// <summary>CKeyTable::FUN_141432010 (keytable.cpp:2629-2648): false for <see cref="RideeRow.RefusedActions"/>
    /// while the mount's hidePCInfo is set.</summary>
    public static bool KeyActionAllowed(RideeRow ridee, int action) =>
        PcInfoShown(ridee) || Array.IndexOf(RideeRow.RefusedActions, action) < 0;

    /// <summary>CQuestNavigator::CanShowBeam's ride part (questnavigator.cpp:474-480): a riding player shows the beam only
    /// when its row's default rider has useRidingNavi (false without a row).</summary>
    public static bool RidingBeam(RideeRow ridee) => ridee is { UseRidingNavi: true };

    /// <summary>The name tag and HP bar of a riding PC are hidden when its ridee's default rider has nameTagHide
    /// (UINameTag::Entity, uinametag.cpp:327-333; UIHpBar::Entity, uihpbar.cpp:204-211).</summary>
    public static bool TagHidden(RideeRow ridee) => ridee is { NameTagHide: true };
}
