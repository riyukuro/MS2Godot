using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// Hosts UIReconnectTopLayerNotify (0x4f78, CommonDepth.PopupDialog = 0x1a, uireconnecttoplayernotify.cpp) on the HUD
/// while <see cref="Ms2Godot.Net.LiveSession"/> is reconnecting.
/// </summary>
public partial class HudScene
{
    CanvasLayer reconnectNotifyLayer;
    MovieDialog reconnectNotify;
    ReconnectTopLayerNotifyController reconnectNotifySide;
    Action unwatchReconnect;

    void ReconnectNotifySlot(Control _)
    {
        if (reconnectNotifyLayer != null) return;
        reconnectNotifyLayer = new CanvasLayer { Layer = 11, Offset = UiScale.MovieOrigin };
        MovieLayerHost.Bind(reconnectNotifyLayer, ReconnectTopLayerNotifyController.DepthLayer);
        AddChild(reconnectNotifyLayer);
    }

    void WatchReconnect(Ms2Godot.Net.LiveSession live)
    {
        UnwatchReconnect();
        if (live == null) return;
        Action<int, int> onProgress = ShowReconnectNotify;
        Action onHidden = HideReconnectNotify;
        live.ReconnectProgress += onProgress;
        live.ReconnectProgressHidden += onHidden;
        unwatchReconnect = () =>
        {
            live.ReconnectProgress -= onProgress;
            live.ReconnectProgressHidden -= onHidden;
            HideReconnectNotify();
        };
    }

    void UnwatchReconnect()
    {
        unwatchReconnect?.Invoke();
        unwatchReconnect = null;
    }

    void ShowReconnectNotify(int attempt, int max)
    {
        if (reconnectNotifyLayer == null || assets == null || !IsInsideTree()) return;
        if (reconnectNotify == null)
        {
            reconnectNotifySide = new ReconnectTopLayerNotifyController();
            reconnectNotifySide.SetRetryCount(attempt, max);
            reconnectNotifySide.Cancelled += () =>
            {
                HideReconnectNotify();
                Ms2Godot.Net.LiveSession.Instance?.CancelReconnect();
            };
            reconnectNotify = new MovieDialog(assets, reconnectNotifySide, NewModel(), ScreenMode.PixelSize(GetWindow()));
            reconnectNotifyLayer.AddChild(reconnectNotify);
        }
        else
        {
            reconnectNotifySide?.SetRetryCount(attempt, max);
        }
    }

    void HideReconnectNotify()
    {
        if (reconnectNotify != null)
        {
            reconnectNotify.Host?.Unload();
            reconnectNotify.QueueFree();
            reconnectNotify = null;
        }
        reconnectNotifySide = null;
    }
}
