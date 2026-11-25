namespace Ms2.Ui.Hud;

/// <summary>
/// UITopLayerNotify (ms2/client/ms2/uitoplayernotify.cpp, "TopLayerNotify", UI id 0x4ef5, a UIHeadUpDisplay), the C++ side
/// of uitoplayernotify: the top layer announce, shown by a notice of kind 4 with flags 0x200 (CSceneStateGame::OnNotice,
/// scenestategame.cpp:9240) through ShowTopLayerNotify (@0x141275400, unit_141274d10_uihelpers.cpp:305). The movie is loaded
/// only while a message is up: ShowMessage without it keeps the text and type and loads it (vfunc_2), OnLoadComplete shows
/// the kept message, and the movie's onHideMessage unloads it again (vfunc_3).
/// </summary>
public sealed class TopLayerNotifyController : UiController
{
    public override string Movie => "uitoplayernotify";     // GetSwfFileName: "UI" + GetTypeName
    protected override string EntityMethod => "showMessage";
    protected override bool IsDialog => false;              // a UIHeadUpDisplay
    public override string TypeName => "TopLayerNotify";    // GetTypeName (@0x140fa8710)
    /// <summary>vfunc_48 (@0x140fa8b90).</summary>
    public const int UiId = 0x4ef5;

    /// <summary>UIHeadUpDisplay::vfunc_2: the host loads the movie.</summary>
    public Action LoadMovie;
    /// <summary>UIHeadUpDisplay::vfunc_3: the host unloads the movie.</summary>
    public Action UnloadMovie;

    string pendingText = "";    // m_70
    int pendingType;            // m_78

    /// <summary>The kept message (m_70, m_78), for the tests.</summary>
    public (string Text, int Type) Pending => (pendingText, pendingType);

    bool Loaded => Host?.Entity != null;     // m_30.get()

    /// <summary>
    /// ShowMessage (@0x140fa8970): with the movie loaded, its showMessage(text, type); else the text and type are kept and the
    /// movie loads (vfunc_2), and OnLoadComplete shows them.
    /// </summary>
    public void ShowMessage(string text, int type)
    {
        text ??= "";
        if (Loaded)
        {
            Host.Invoke(Host.Entity, "showMessage", text, type);
            return;
        }
        pendingText = text;
        pendingType = type;
        LoadMovie?.Invoke();
    }

    /// <summary>OnLoadComplete (@0x140fa87c0): UIObject's, then the kept message when it has text.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (pendingText.Length != 0) ShowMessage(pendingText, pendingType);
    }

    /// <summary>vfunc_28 (@0x140fa88f0): onHideMessage runs vfunc_3, which unloads the movie (UIHeadUpDisplay's) and
    /// forgets the kept message.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onHideMessage", StringComparison.OrdinalIgnoreCase)) return false;
        Hide();
        return true;
    }

    /// <summary>vfunc_3 (@0x140fa8780): UIHeadUpDisplay's unload, then the kept message is forgotten.</summary>
    public void Hide()
    {
        UnloadMovie?.Invoke();
        pendingText = "";
        pendingType = 0;
    }

    /// <summary>vfunc_43 (@0x140fa8820): the movie aligned top centre, a tenth of the client height down
    /// (ASGameEntity::vfunc_2(1, 0, height * 0.1)).</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (!Loaded) return;
        Host.Align(Host.Entity, 1, 0, (int)(stageH * 0.1));
    }
}
