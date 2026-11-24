using Godot;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIRecallScrollNotifyDialog (0x4edd, movie uirecallscrollnotifydialog): the guild, party and wedding recall notices. The game
/// scene opens it with the HUD (UISceneGame's open list, uiservice.cpp:735) and it stays; the RecallScroll packet
/// (OnRecallScroll, scenestategame.cpp:8038) goes to it, and its once-a-second tick (vfunc_72) runs each frame.
/// </summary>
public partial class HudScene
{
    RecallScrollNotifyController recallNotify;
    MovieDialog recallNotifyMovie;
    Action unwatchRecallScroll;

    void RecallScrollNotify(Control ui)
    {
        if (recallNotifyMovie == null)
        {
            var c = recallNotify = new RecallScrollNotifyController
            {
                NowMs = () => Time.GetTicksMsec(),
                InField = () => true,                                     // the HUD runs in a field only
                ServerNow = () => Ms2Godot.Net.LiveSession.Instance?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                FieldExists = id => Ms2.Formats.World.FieldData.FindById(assets.Packs, id) != null,
                MapName = id => hudModel.Tables.MapName(id) ?? "",
                CommonById = Ms2.Ui.Hud.MessageBoxData.Common,
                CommonByKey = key => hudModel.Tables.Common(key) ?? "",
                Notice = (text, type, log) =>
                {
                    if (SystemText == null) return;                     // ShowSystemNotice: nothing without the system text
                    SystemText.ShowMessage(text ?? "", type, 0);
                    if (log) ChatLog.AddSystemLine(text ?? "", TalkKind.SystemNotice);
                },
                PlaySound = key => FieldAudio.Instance?.PlayUi(key),
            };
            recallNotifyMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        }
        ui.AddChild(recallNotifyMovie);
        WatchRecallScroll();
    }

    void WatchRecallScroll()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null || unwatchRecallScroll != null || recallNotify == null) return;
        Action<RecallScrollPacket> on = recallNotify.OnPacket;
        live.RecallScrollReceived += on;
        unwatchRecallScroll = () => live.RecallScrollReceived -= on;
    }
}
