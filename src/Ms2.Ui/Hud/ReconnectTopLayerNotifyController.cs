namespace Ms2.Ui.Hud;

/// <summary>
/// UIReconnectTopLayerNotify (ms2/client/ms2/uireconnecttoplayernotify.cpp, @0x140e7fd50-0x140e800f0, UI id 0x4f78,
/// CommonDepth 0x1a = PopupDialog), the C++ side of uireconnecttoplayernotify: displays the reconnect attempt counter
/// ("setRetryCount") and handles the cancel button ("onCancel" -> vfunc_3 unload + CReconnectionManagerCli::DisableReconnect).
/// </summary>
public sealed class ReconnectTopLayerNotifyController : UiController
{
    public const int UiId = 0x4f78;
    public const int DepthLayer = CommonDepth.PopupDialog;   // m_2c = 0x1a (uireconnecttoplayernotify.cpp:20)

    public override string Movie => "uireconnecttoplayernotify";
    protected override string EntityMethod => "setRetryCount";
    protected override bool IsDialog => false;               // UIHeadUpDisplay (uireconnecttoplayernotify.h:5)
    public override string TypeName => "ReconnectTopLayerNotify";   // GetTypeName (@0x140e7fe00)

    /// <summary>m_70: retry counter (starts at 1 in the ctor, uireconnecttoplayernotify.cpp:21).</summary>
    public int Retry { get; private set; } = 1;
    /// <summary>m_74: retry limit (starts at 5 in the ctor, uireconnecttoplayernotify.cpp:22).</summary>
    public int MaxRetries { get; private set; } = 5;

    /// <summary>vfunc_28 "onCancel" (@0x140e7ffa0, uireconnecttoplayernotify.cpp:67-73): unloads the HUD notice and disables reconnect.</summary>
    public event Action Cancelled;

    /// <summary>vfunc_43 (@0x140e7fec0, uireconnecttoplayernotify.cpp:47-52): ASGameEntity::vfunc_2(0, 0, -25).</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Host?.Entity != null) Host.Align(Host.Entity, 0, 0, -25);
    }

    /// <summary>OnLoadComplete (@0x140e7fe90, uireconnecttoplayernotify.cpp:40-44): UIObject::OnLoadComplete, then SetRetryCount.</summary>
    public override void OnLoadComplete() => PushRetryCount();

    /// <summary>vfunc_52 (@0x140e7ff60, uireconnecttoplayernotify.cpp:55-60): updates retry and max, then pushes setRetryCount.</summary>
    public void SetRetryCount(int retry, int max)
    {
        Retry = retry;
        MaxRetries = max;
        PushRetryCount();
    }

    /// <summary>SetRetryCount (@0x140e80030, uireconnecttoplayernotify.cpp:78-82).</summary>
    public void PushRetryCount()
    {
        if (Host?.Entity != null) Host.Invoke("setRetryCount", Retry, MaxRetries);
    }

    /// <summary>vfunc_28 (@0x140e7ffa0, uireconnecttoplayernotify.cpp:63-75).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onCancel", StringComparison.OrdinalIgnoreCase)) return false;
        Cancelled?.Invoke();
        return true;
    }
}
