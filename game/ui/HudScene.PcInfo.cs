using System;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The other player's character window: UIPCInfoDialog (UI id 0x4ec8, uipcinfodialog.cpp), the same movie and type name as
/// the own window ("MyInfoDialog", uipcinfodialog.h:27), run by <see cref="MyInfoController"/> with
/// <see cref="MyInfoController.PcInfoDialog"/>. The popup menu's View Character sends the request (SendOp 0x1E); the answer
/// (RecvOp 0x78) goes to the request state, which the client keeps in the dialog object between opens
/// (<see cref="PcInfoRequests"/>), and opens the window or refreshes the loaded one.
/// STILL-OPEN: the avatar. UIPCInfoDialog builds a UIAvatarPC from the record's equips and the two skin colours
/// (FUN_14132c330); no remote avatar is ported, so the window's render target stays empty (no image lookup is attached).
/// STILL-OPEN: MyPC's ride with m_nHidePCInfo > 1 closes 0x4ec8 with the other info windows (mypc.cpp:3311); no ride is
/// ported.
/// </summary>
public partial class HudScene
{
    readonly PcInfoRequests pcInfoRequests = new() { TickMs = () => (uint)Time.GetTicksMsec() };
    MyInfoController pcInfo;
    MovieDialog pcInfoMovie;                    // from open until OnEndClose
    Action unwatchPcInfo;

    bool PcInfoOpen => pcInfo is { IsClosed: false };

    /// <summary>The answer's subscribers: the session's 0x78 to the request state, and its outcomes to the window.</summary>
    void WatchPcInfo()
    {
        Action open = OpenPcInfo;
        Action refresh = () => { if (PcInfoOpen) pcInfo.ShowRecord(); };
        Action<int> box = id => MessageBoxQueue.ShowCommon(id, 0, null, "");    // FUN_140d73bc0(0x26, 0, 0, "")
        pcInfoRequests.Open += open; pcInfoRequests.Refresh += refresh; pcInfoRequests.CommonBox += box;
        var live = Ms2Godot.Net.LiveSession.Instance;
        // vfunc_79: vfunc_55 == 1 (closed) opens; FUN_1422dc430 (m_54, the movie loaded) refreshes
        Action<CharacterInfoAnswer> answered = a => pcInfoRequests.OnAnswer(a, pcInfo == null || pcInfo.IsClosed,
                                                                           PcInfoOpen && pcInfo.Host?.Entity != null);
        if (live != null) live.CharacterInfoReceived += answered;
        unwatchPcInfo = () =>
        {
            pcInfoRequests.Open -= open; pcInfoRequests.Refresh -= refresh; pcInfoRequests.CommonBox -= box;
            if (live != null) live.CharacterInfoReceived -= answered;
        };
    }

    /// <summary>
    /// CUIService::OpenDialog(0x4ec8): the window over the answer's record (m_spInfo). Nothing is sent from it: the window's
    /// buttons are only logged.
    /// </summary>
    void OpenPcInfo()
    {
        if (pcInfoMovie != null) { pcInfoMovie.Host.Unload(); pcInfoMovie.QueueFree(); pcInfoMovie = null; }
        if (pcInfoRequests.Info == null) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        Func<long> now = () => live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var c = new MyInfoController
        {
            PcInfoDialog = true, Requests = pcInfoRequests, Record = () => pcInfoRequests.Info, ServerTime = now,
            ActionKeyDown = ActionKeyDown,
            LocalPlayerDead = () => Ms2Godot.Net.LiveSession.Instance?.Me.Dead ?? false,
        };
        c.SystemNotice += SystemNotice;
        c.ToggleDialog += id => GD.Print($"character info: dialog 0x{id:x} is not in this HUD");
        c.OpenDialog += id => GD.Print($"character info: dialog 0x{id:x} is not in this HUD");
        c.Button += name => GD.Print($"character info: {name} is not sent (display only)");
        // GetProfileImagePath (@0x141336c90): FUN_141028be0(id, picture, 1)
        c.ProfileImagePath = pc => Ms2Godot.Net.ProfileImageService.Images.Path(pc.CharacterId, pc.Picture, true);
        BindBoxAnswers();
        var m = new MovieDialog(assets, c, hudModel, GetWindow().Size);
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (pcInfoMovie == m) pcInfoMovie = null;
        };
        pcInfo = c; pcInfoMovie = m;
        uiRoot.AddChild(m);
    }
}
