namespace Ms2.Ui;

/// <summary>
/// UILoadingProgress (ms2/client/ms2/uiloadingprogress.cpp): the loading screen's C++ side. UILoadingProgressLobby, the
/// lobby's loading screen, is the same movie (vfunc_7 "LoadingProgress", uiloadingprogress.cpp:356-366) with showFrame
/// always false (vfunc_52). It is a UISwfEntity, not a UIDialog: OnLoadComplete only makes its background texture.
/// </summary>
public sealed class LoadingProgressController : UiController
{
    public override string Movie => "uiloadingprogress";
    protected override string EntityMethod => "startProgress";
    protected override bool IsDialog => false;

    /// <summary>vfunc_28 "OnFadeInComplete" (uiloadingprogress.cpp:201-209): the scene service goes on (GFxSceneService::FUN_1422ba110).</summary>
    public event Action FadeInComplete;

    /// <summary>The members StartProgress puts on the object it sends (uiloadingprogress.cpp:97-109).</summary>
    public sealed class StartInfo
    {
        /// <summary>GetLoadingImagePath: the target map's loading picture, "" for none (the movie then asks for the screenshot).</summary>
        public string FilePath = "";
        /// <summary>GetMaskImagePath: the current field's m_strBgMask.</summary>
        public string MaskPath = "";
        /// <summary>CLoadingDescriptionTable::GetRandomTip.</summary>
        public string Tip = "";
        /// <summary>GetFadeOutFlag(WorldService) (WorldService+0x2a8); the movie does nothing when it is 0.</summary>
        public int UseFadeOut = 1;
        /// <summary>The local player's level is below the guide limit constant.</summary>
        public int ShowGuide;
        /// <summary>GetInputDeviceName.</summary>
        public string ShowGuideType = "keyboard";
        /// <summary>FUN_1419e0630: a map id was given to StartProgress (a channel move).</summary>
        public int ShowMove;
        /// <summary>vfunc_52: false on scenes 0x6200009 / 0x6200085 and always for the lobby's screen.</summary>
        public bool ShowFrame = true;
    }

    /// <summary>StartProgress (uiloadingprogress.cpp:85-111): startProgress(obj).</summary>
    public void StartProgress(StartInfo s) => Host.Invoke("startProgress", Host.Obj(
        ("filePath", s.FilePath ?? ""), ("maskPath", s.MaskPath ?? ""), ("tip", s.Tip ?? ""), ("useFadeOut", s.UseFadeOut),
        ("showGuide", s.ShowGuide), ("showGuideType", s.ShowGuideType ?? ""), ("showMove", s.ShowMove), ("showFrame", s.ShowFrame)));

    /// <summary>StopProgress (uiloadingprogress.cpp:115-122): stopProgress(GetFadeOutFlag).</summary>
    public void StopProgress(int useFadeOut) => Host.Invoke("stopProgress", useFadeOut);

    /// <summary>SetProgress (uiloadingprogress.cpp:140-146): progress(message, count, total).</summary>
    public void SetProgress(string message, int count, int total) => Host.Invoke(Host.Entity, "progress", message ?? "", count, total);

    /// <summary>SetMoveChannelMessage (uiloadingprogress.cpp:149-155).</summary>
    public void SetMoveChannelMessage(string message) => Host.Invoke(Host.Entity, "setMoveChannelMessage", message ?? "");

    /// <summary>vfunc_2(map, message) (uiloadingprogress.cpp:170-176): loadFail(message).</summary>
    public void LoadFail(string message) => Host.Invoke(Host.Entity, "loadFail", message ?? "");

    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "OnFadeInComplete", StringComparison.OrdinalIgnoreCase)) return false;
        FadeInComplete?.Invoke();
        return true;
    }
}
