using System;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// Hosts UICommunityPopup (0x4f5c, uicommunitypopup.cpp), the one-line ticker of GmCommand mode 1 and the PopupText user
/// command, as a UIHeadUpDisplay: its movie is loaded only while a line is up, in its own slot among the HUD movies.
/// </summary>
public partial class HudScene
{
    Control communityPopupSlot;             // the ticker's place among the HUD movies
    MovieDialog communityPopup;             // uicommunitypopup, loaded only while a line is up
    Action unwatchCommunityPopup;

    /// <summary>The community ticker (UICommunityPopup 0x4f5c).</summary>
    public Ms2.Ui.Hud.CommunityPopupController CommunityPopup { get; private set; }
    /// <summary>The ticker's movie is loaded (a line is up); for the live run.</summary>
    public bool CommunityPopupUp => communityPopup != null && IsInstanceValid(communityPopup) && CommunityPopup?.Host?.Entity != null;

    /// <summary>The ticker's slot, made with the other HUD movie slots; a line up when the HUD is rebuilt loads again.</summary>
    void CommunityPopupSlot(Control ui)
    {
        ui.AddChild(communityPopupSlot = new Control { Name = "uicommunitypopup", MouseFilter = Control.MouseFilterEnum.Ignore });
        CommunityPopup ??= new Ms2.Ui.Hud.CommunityPopupController
        {
            LoadMovie = LoadCommunityPopup,
            UnloadMovie = UnloadCommunityPopup,
            OpenUrl = WebBrowser.Navigate,      // UIWebBrowserPopup (0x4f40) Navigate(url, 1)
        };
        if (communityPopup != null && !IsInstanceValid(communityPopup)) communityPopup = null;
        if (CommunityPopup.IsOpen && communityPopup == null) LoadCommunityPopup();
    }

    /// <summary>GmCommand mode 1 (FUN_14173c0f0) and the ServerTime the ticker counts its 120 seconds in.</summary>
    void WatchCommunityPopup(Ms2Godot.Net.LiveSession live)
    {
        CommunityPopup.ServerTime = () => live.ServerTime;
        Action<Ms2.Net.Packets.GmCommand> onLine = g => CommunityPopup?.Show(g.Text, g.Url);
        live.GmPopupText += onLine;
        unwatchCommunityPopup = () => live.GmPopupText -= onLine;
    }

    /// <summary>UIHeadUpDisplay::vfunc_2 for the ticker: its movie loads; OnLoadComplete lays out the shown line.</summary>
    void LoadCommunityPopup()
    {
        if (communityPopup != null || communityPopupSlot == null || !IsInstanceValid(communityPopupSlot)) return;
        communityPopup = new MovieDialog(assets, CommunityPopup, NewModel(), ScreenMode.PixelSize(GetWindow()));
        communityPopupSlot.AddChild(communityPopup);
    }

    /// <summary>UIHeadUpDisplay::vfunc_3 for the ticker: the movie unloads.</summary>
    void UnloadCommunityPopup()
    {
        if (communityPopup == null) return;
        var m = communityPopup;
        communityPopup = null;
        if (!IsInstanceValid(m)) return;
        m.Host.Unload();
        m.GetParent()?.RemoveChild(m);
        m.QueueFree();
    }
}
