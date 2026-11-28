using System;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// The field PvP part of the HUD: PlayerKillNotice (CSceneStateGame::OnPlayerKillNotice @0x141733850, scenestategame.cpp:4344)
/// into the kill log dialog <see cref="PvPKillLogController"/> (0x4ea0) and the streak notice, and the own PC's relation for
/// the other players' hostile name tag colour (<see cref="Net.LiveSession.OwnPcRelation"/>).
/// </summary>
public partial class HudScene
{
    readonly PkZoneNotice pkZone = new();
    static System.Collections.Generic.Dictionary<int, int> pkZoneShadows;
    MovieDialog killLogMovie;
    PvPKillLogController killLog;
    Action unwatchPvp;

    /// <summary>The kill log dialog, for checks.</summary>
    public PvPKillLogController KillLog => killLog;
    /// <summary>The pk zone flag the last fade-in left, for checks.</summary>
    public bool InPkZone => pkZone.InPkZone;

    void WatchPvp()
    {
        unwatchPvp?.Invoke();
        if (Net.LiveSession.Instance is not { } live) return;
        Func<Ms2.Ui.Skills.HitCandidate> own = () => OwnPcRelation(live);
        live.OwnPcRelation = own;
        Action<PlayerKillNotice> on = OnPlayerKillNotice;
        live.PlayerKillNoticeReceived += on;
        unwatchPvp = () =>
        {
            live.PlayerKillNoticeReceived -= on;
            if (live.OwnPcRelation == own) live.OwnPcRelation = null;
        };
    }

    void UnwatchPvp()
    {
        unwatchPvp?.Invoke();
        unwatchPvp = null;
    }

    /// <summary>
    /// OnPlayerKillNotice: the kill log opens when it is closed (any mode); mode 0 adds the kill (AddKill), mode 1 shows the
    /// StringId text as a system notice of type 4 with the log and plays System_KillSlayer (DAT_143ae7a10).
    /// STILL-OPEN: mode 0's NotifyKillPlayer post to the PvP observer (uipvpobserverdialog is not ported).
    /// </summary>
    void OnPlayerKillNotice(PlayerKillNotice n)
    {
        if (killLog == null || killLog.IsClosed) OpenKillLog();
        if (n.Mode == 0) killLog.AddKill(n.Killer, n.Victim);
        else if (n.Mode == 1)
        {
            string text = n.Text == null ? "" : Notices?.Text(n.Text) ?? n.Text.Text ?? "";
            ChatLog.ShowSystemNotice?.Invoke(text, 4, true);
            FieldAudio.Instance?.PlayUi("System_KillSlayer");
        }
    }

    void OpenKillLog()
    {
        if (killLogMovie != null) { killLogMovie.Host.Unload(); killLogMovie.QueueFree(); killLogMovie = null; }
        var t = hudModel.Tables;
        var c = killLog = new PvPKillLogController
        {
            ChatLine = (text, kind) => { if (!string.IsNullOrEmpty(text)) ChatLog.AddSystemLine(text, kind); },
            Sound = key => FieldAudio.Instance?.PlayUi(key),
            ReplaceValueWords = s => t.ReservedWords.ReplaceValueWords(s, hudModel.HasLocalPlayer ? hudModel.Player.Name : null),
            Common = id => t.Common(id),
            MyName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null,
        };
        var m = killLogMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (killLogMovie == m) killLogMovie = null;
        };
        uiRoot.AddChild(m);
    }

    /// <summary>UISceneGamePlay::vfunc_58's pk zone part (<see cref="PkZoneNotice"/>), at the field's fade-in complete: the
    /// notice (FUN_141275260(id, 4, 0)), the chat line of kind 0x14 and System_KillSlayer.</summary>
    public void PkZoneOnFadeIn()
    {
        Ms2.Formats.World.FieldData field = null;
        int shadow = 0;
        try
        {
            if (Net.LiveSession.Instance?.FieldId is int id && (field = Ms2.Formats.World.FieldData.FindById(assets.Packs, id)) != null)
            {
                pkZoneShadows ??= PortalAction.ContinentShadows(assets.Packs);
                pkZoneShadows.TryGetValue(field.ContinentCode, out shadow);
            }
        }
        catch (Exception ex) { Godot.GD.PushWarning($"pk zone notice: {ex.Message}"); }
        if (pkZone.OnFadeInComplete(field != null, shadow, field?.PkMode ?? 0) is not var (notice, chat)) return;
        var t = hudModel.Tables;
        ChatLog.ShowSystemNotice?.Invoke(t.Common(notice), PkZoneNotice.NoticeType, false);
        string line = t.Common(chat);
        if (!string.IsNullOrEmpty(line)) ChatLog.AddSystemLine(line, PkZoneNotice.ChatKind);
        FieldAudio.Instance?.PlayUi(PkZoneNotice.Sound);
    }
}
