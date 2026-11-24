using Godot;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIGlobalPortalDialog (0x4ed7, movie uiglobalportaldialog): the massive event entrance notice. The controller lives for the
/// session, as the client's dialog does (CUIService makes it once); the GlobalPortal packet goes to it whether or not the movie is
/// loaded, its per-frame update runs even while it is closed (the ctor's SetFlag(3, 1), uiservice.cpp:1927), and its open
/// loads the movie. The HUD is only made in the game scene, so the scene check always passes here.
/// </summary>
public partial class HudScene
{
    GlobalPortalController globalPortal;
    MovieDialog globalPortalMovie;
    Action unwatchGlobalPortal;

    GlobalPortalController GlobalPortal()
    {
        if (globalPortal != null) return globalPortal;
        var constants = Ms2.Ui.FeatureElements.Constants(assets.Packs);
        globalPortal = new GlobalPortalController
        {
            Tables = hudModel.Tables,
            NowMs = () => (uint)Time.GetTicksMsec(),
            PlayerLevel = () => hudModel.HasLocalPlayer ? hudModel.Player.Level : null,
            MinLevel = () => constants.Int("GlobalPortalMinLevel", 0),          // Value<unsigned int>, default 0
            IsOpen = () => globalPortalMovie != null && !globalPortal.IsClosed,
            OpenRequested = OpenGlobalPortal,
            Notice = (text, type, log) =>
            {
                if (string.IsNullOrEmpty(text) || SystemText == null) return;
                SystemText.ShowMessage(text, type, 0);
                if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
            },
            PlaySound = key => FieldAudio.Instance?.PlayUi(key),
            Send = p => Ms2Godot.Net.LiveSession.Instance?.SendTalk(p),
        };
        globalPortal.Unloaded += () =>
        {
            var m = globalPortalMovie;
            globalPortalMovie = null;
            if (m == null) return;
            m.Host.Unload();
            m.QueueFree();
        };
        return globalPortal;
    }

    /// <summary>FUN_141924680: the dialog's open, the movie loaded under the HUD.</summary>
    void OpenGlobalPortal()
    {
        if (globalPortalMovie != null) return;
        var m = new MovieDialog(assets, globalPortal, hudModel, ScreenMode.PixelSize(GetWindow()));
        globalPortalMovie = m;
        GD.Print($"global portal: load={m.Loaded}");
        uiRoot.AddChild(m);
    }

    /// <summary>OnGlobalPortal (scenestategame.cpp:4369) to UIGlobalPortalDialog::OnGlobalPortalPacket.</summary>
    void WatchGlobalPortal()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null || unwatchGlobalPortal != null) return;
        var c = GlobalPortal();
        Action<GlobalPortalPacket> on = c.OnPacket;
        live.GlobalPortalReceived += on;
        unwatchGlobalPortal = () => live.GlobalPortalReceived -= on;
    }
}
