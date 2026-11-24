using Godot;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2Godot.Flow;

namespace Ms2Godot.Net;

/// <summary>
/// Reconnect wiring on the live game connection (CReconnectionManagerCli, reconnectionmanagercli.cpp @0x140aace60-0x140aadd70;
/// WindowService uMsg 0x465, windowservice.cpp:482-555; CSceneStateGame::OnLeave, scenestategame.cpp:1312).
/// </summary>
public partial class LiveSession
{
    /// <summary>UIReconnectTopLayerNotify (0x4f78): raised on the main thread with (attempt, maxAttempts) while reconnecting.</summary>
    public event Action<int, int> ReconnectProgress;
    /// <summary>UIReconnectTopLayerNotify (0x4f78): raised on the main thread when the reconnect notice hides.</summary>
    public event Action ReconnectProgressHidden;

    /// <summary>Binds a newly opened game transport's reconnection identity and main-thread UI/scene callbacks.</summary>
    void AttachGameReconnect(TcpTransport g)
    {
        if (g == null) return;
        UpdateReconnectIdentity(g);
        g.ReconnectProgress += (attempt, max) =>
        {
            Callable.From(() =>
            {
                if (exitCts.IsCancellationRequested || game != g) return;
                if (LiveCheck) GD.Print($"live: reconnect progress {attempt}/{max}");
                ReconnectProgress?.Invoke(attempt, max);
            }).CallDeferred();
        };
        g.ReconnectProgressHidden += () =>
        {
            Callable.From(() =>
            {
                if (exitCts.IsCancellationRequested) return;
                ReconnectProgressHidden?.Invoke();
            }).CallDeferred();
        };
        g.SessionEnded += () =>
        {
            Callable.From(() =>
            {
                if (exitCts.IsCancellationRequested || game != g) return;
                OnReconnectSessionEnded();
            }).CallDeferred();
        };
        g.ReconnectGaveUp += code =>
        {
            Callable.From(() =>
            {
                if (exitCts.IsCancellationRequested) return;
                OnReconnectGaveUp(code);
            }).CallDeferred();
        };
    }

    /// <summary>Sets accountId (DAT_143ab2820), characterId (DAT_143ab2828, OnServerEnter) and dashed machine id on the reconnection manager.</summary>
    void UpdateReconnectIdentity(TcpTransport g = null)
    {
        var target = g ?? game;
        if (target == null) return;
        target.Reconnection.SetIdentity(accountId, entry?.Enter?.CharacterId ?? 0, MachineIdText.Format(machine));
    }

    /// <summary>CSceneStateGame::OnLeave (@0x14170bfd0, scenestategame.cpp:1312): resets CReconnectionManagerCli and hides any progress notice.</summary>
    void ResetReconnect()
    {
        game?.Reconnection.Reset();
        ReconnectProgressHidden?.Invoke();
    }

    /// <summary>UIReconnectTopLayerNotify "onCancel" (@0x140e7ffa0, uireconnecttoplayernotify.cpp:67-72): DisableReconnect.</summary>
    public void CancelReconnect()
    {
        if (LiveCheck) GD.Print("live: reconnect cancelled");
        game?.Reconnection.Disable();
        ReconnectProgressHidden?.Invoke();
    }

    /// <summary>CReconnectionManagerCli::OnReconnectPacket kind 3 in game (reconnectionmanagercli.cpp:317-325): SendRequestQuitAndDisconnect, scene 0x6200009, box 0x20f(0).</summary>
    void OnReconnectSessionEnded()
    {
        GD.PushWarning("live: reconnect session ended (kind 3)");
        ReconnectProgressHidden?.Invoke();
        _ = SendRequestQuitAndDisconnect();
        GameFlow.ReturnToLogin();
        string text = Ms2.Ui.DisconnectNotice.FormatSessionEnded(Ms2.Ui.Hud.MessageBoxData.Common);
        if (!string.IsNullOrEmpty(text)) Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");
    }

    /// <summary>WindowService uMsg 0x465 when reconnect gave up (windowservice.cpp:482-555): scene 0x6200009 and the 0x20f / 0xc69 disconnect box.</summary>
    void OnReconnectGaveUp(int code)
    {
        GD.PushWarning($"live: reconnect gave up, code {code}");
        ReconnectProgressHidden?.Invoke();
        GameFlow.ReturnToLogin();
        string text = Ms2.Ui.DisconnectNotice.FormatDisconnect(code, Ms2.Ui.Hud.MessageBoxData.Common);
        if (!string.IsNullOrEmpty(text)) Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");
    }

    /// <summary>CNetworkService::OnGvg (@0x14153a8f0, networkservice.cpp:811-832): ClientSession already applied kind 43 to the socket; the rest is
    /// decoded for the live check log only.</summary>
    void OnGvg(Ms2.Net.Wire.RawPacket p)
    {
        if (!LiveCheck) return;
        var entries = GvgOptions.ReadAll(p.Reader());
        GD.Print($"live: Gvg ({entries.Count} group(s): {string.Join(", ", entries.Select(e => e.Kind))})");
    }
}
