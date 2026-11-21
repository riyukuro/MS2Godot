using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// CSceneStateGame::OnNewsNotification (scenestategame.cpp:3789), mode 0: a page address for one of the web browser dialogs.
/// With bInGame clear the address goes to the browser service's token path (SendTokenToBrowser, not ported:
/// <see cref="TokenRequested"/>); with it set the dialog of the type is handed the address through UIWebBrowserBase::vfunc_89:
/// type 0 the web browser (0x4e6a), 1 the visitors book (0x4ed9), 2 the notice dialog (0x4ee9), 5 the browser popup
/// (0x4f40); any other type does nothing. The pages themselves are not drawn here: each dialog keeps what the client hands
/// its browser.
/// </summary>
public sealed class NewsNotificationRouter
{
    public const int WebBrowser = 0, VisitorsBook = 1, Notice = 2, Popup = 5;

    readonly Dictionary<int, Action<string>> targets = new();
    readonly Dictionary<int, string> urls = new();

    /// <summary>SendTokenToBrowser(type, url): the in-game flag was clear.</summary>
    public event Action<int, string> TokenRequested;

    /// <summary>The dialog of a type that is open to receive an address; its handler is vfunc_89.</summary>
    public void Register(int type, Action<string> openUrl) => targets[type] = openUrl;

    /// <summary>The last address handed to the dialog of a type, null if none.</summary>
    public string UrlOf(int type) => urls.TryGetValue(type, out var u) ? u : null;

    public static bool IsBrowserType(int type) => type is WebBrowser or VisitorsBook or Notice or Popup;

    public void Handle(NewsNotification m)
    {
        if (m.Mode != 0) return;
        if (!m.InGame) { TokenRequested?.Invoke(m.Type, m.Url); return; }
        if (!IsBrowserType(m.Type)) return;
        urls[m.Type] = m.Url;
        if (targets.TryGetValue(m.Type, out var open)) open(m.Url);
    }
}
