using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>UIWebBrowserBase 0x4e6a Navigate(url, true) for this client: the address opens in the system browser (see <see cref="WebPage"/>).</summary>
public static class WebBrowser
{
    public static void Navigate(string url)
    {
        string target = WebPage.Target(url);
        if (target == null) { GD.Print($"web: nothing to open for \"{url}\""); return; }
        GD.Print($"web: {target}");
        OS.ShellOpen(target);
    }
}
