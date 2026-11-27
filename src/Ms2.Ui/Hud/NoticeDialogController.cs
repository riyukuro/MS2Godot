using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UINoticeDialog (ms2/client/ms2/uinoticedialog.cpp; UI id 0x4ee9), the C++ side of uinoticedialog: the notice banner
/// tab on the left edge. The client opens it with the field's HUD when the NoticeDialogUrl constant is set
/// (CUIService, uiservice.cpp:742). Its page is a web browser page drawn into m_renderTarget (UIWebBrowserBase,
/// CWebBrowserService), which is not ported: <see cref="BrowserVisible"/> keeps the state the client hands the browser.
/// </summary>
public sealed class NoticeDialogController : UiController
{
    /// <summary>The ctor (@0x1410084b0) and UIWebBrowserBase's (uiupperhuddialog.cpp:2245) have no SetTweenEnabled: m_c4 stays
    /// 0, no open / close tween.</summary>
    protected override bool TweenEnabled => false;

    public override string Movie => "uinoticedialog";

    /// <summary>UINoticeDialog's ctor sets the flag 0 (uinoticedialog.cpp:86): vfunc_56, an always-on one.</summary>
    public override bool AlwaysOn => true;
    protected override string EntityMethod => "alignment";        // GameEntity's; the dialog is the movie's only GameEntity
    public override string TypeName => "NoticeDialog";            // GetTypeName (:96)
    public override bool KeysWhileTextFocused => true;   // FUN_1422dc3b0(1) in the ctor (uinoticedialog.cpp:85)

    object mainWindow, showButton, maviewButton, hitArea;
    uint hideAt;                                                  // m_nHideAt (+0x1f8): tick (ms) to hide at, 0 = none

    /// <summary>UIWebBrowserBase m_bVisible, set by vfunc_84 (uiupperhuddialog.cpp:2271), which shows the browser page.</summary>
    public bool BrowserVisible { get; private set; }

    /// <summary>m_nBrowserId (uinoticedialog.cpp): the browser id the dialog's page has in the browser service.</summary>
    public const int BrowserId = 2;

    /// <summary>The NoticeDialogUrl constant NavigateToNoticeUrl reads (:271); empty means no navigation.</summary>
    public string NoticeUrl = "";

    /// <summary>m_strUrl, the address NavigateToNoticeUrl expands (ExpandUrlPrefix, not ported: kept as given).</summary>
    public string PendingUrl { get; private set; } = "";

    /// <summary>The address vfunc_89 last opened in the browser (OpenBrowser(m_nBrowserId, from, to)); null before the first.</summary>
    public string Url { get; private set; }

    /// <summary>CWebBrowserService::SendNewsNotification(flag, id) from NavigateToNoticeUrl (:278): flag 1, the browser id.</summary>
    public event Action<bool, int> NewsRequested;

    /// <summary>UIWebBrowserBase::vfunc_89 (uiupperhuddialog.cpp:2438): the page is opened at the address, m_strUrl is cleared and
    /// the visibility the dialog has is applied again.</summary>
    public void OpenUrl(string url)
    {
        Url = url;
        PendingUrl = "";
        ShowBrowser(BrowserVisible);
    }

    /// <summary>NavigateToNoticeUrl (@0x141009870): with a NoticeDialogUrl set, it becomes m_strUrl and the news request goes out.</summary>
    void NavigateToNoticeUrl()
    {
        if (string.IsNullOrEmpty(NoticeUrl)) return;
        PendingUrl = NoticeUrl;
        NewsRequested?.Invoke(true, BrowserId);
    }

    /// <summary>OnMaviewButtonClick (@0x1410097a0): CUIService::ToggleDialog(0x4e6a), the in-game web browser.</summary>
    public event Action ToggleWebBrowser;

    /// <summary>vfunc_43 (:146): ASDisplayObject::vfunc_2(3, 0, 0), left edge, vertically centred.</summary>
    public override void Layout(double stageW, double stageH) => Host.Align(Host.Entity, 3, 0, 0);

    /// <summary>
    /// OnLoadComplete (:107): UIDialog's, then gotoAndStop("hideComplete") (ASDisplayObject vfunc_15), the children bound
    /// with the show and maview buttons' click handlers (ASButton::AddClickHandler, "buttonClick") and the hit area made
    /// focusable with focusIn / focusOut listeners, unfocused, and the browser shown by its focus; a pending hide time
    /// shows the notice.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Host.Invoke(Host.Entity, "gotoAndStop", "hideComplete");
        mainWindow = Host.Child("m_mainWindow");
        if (mainWindow != null)
        {
            showButton = Host.Child("m_showButton", mainWindow);
            if (showButton != null) Host.Invoke(showButton, "addEventListener", "buttonClick", Handler(ToggleShowHide), false, 0, false);
            maviewButton = Host.Child("m_maviewButton", mainWindow);
            if (maviewButton != null) Host.Invoke(maviewButton, "addEventListener", "buttonClick", Handler(() => ToggleWebBrowser?.Invoke()), false, 0, false);
            hitArea = Host.Child("m_hitArea", mainWindow);
        }
        if (hitArea != null)
        {
            Host.Set(hitArea, "focusable", true);                                                      // ASUIComponent::SetFocusable
            Host.Invoke(hitArea, "addEventListener", "focusIn", Handler(() => ShowBrowser(true)), false, 0, false);
            Host.Invoke(hitArea, "addEventListener", "focusOut", Handler(() => ShowBrowser(false)), false, 0, false);
            Host.Set(hitArea, "focused", 0);                                                           // ASUIComponent::SetFocused(0)
            ShowBrowser(Rt.ToNumber(Host.Get(hitArea, "focused")) != 0);
        }
        if (hideAt != 0) Show();
    }

    static Func<object[], object> Handler(Action a) => _ => { a(); return null; };

    /// <summary>UIWebBrowserBase::vfunc_84: m_bVisible, then CWebBrowserService::ShowBrowser (not ported).</summary>
    void ShowBrowser(bool visible) => BrowserVisible = visible;

    /// <summary>
    /// OnNoticeDialog (:224), the NoticeDialog packet: flag 0 on an open dialog sets the hide time
    /// (NoticeDialogOpenSeconds, added to the millisecond tick) and shows the notice.
    /// </summary>
    public void OnNoticeDialog(int flag, uint nowMs, uint openTicks)
    {
        if (flag != 0 || IsClosed) return;
        hideAt = nowMs + openTicks;          // SetHideAt @0x141009950
        Show();
    }

    /// <summary>vfunc_72 (:154), each frame: past the hide time the notice hides.</summary>
    public void Update(uint nowMs)
    {
        if (Host?.Entity == null) return;
        if (hideAt != 0 && nowMs > hideAt) Hide();
    }

    /// <summary>ToggleShowHide (@0x1410097d0): "show" as the current label hides, anything else shows.</summary>
    public void ToggleShowHide()
    {
        if (Host.Entity == null) return;
        if (Rt.ToString(Host.Get(Host.Entity, "currentLabel")) == "show") Hide();
        else Show();
    }

    /// <summary>
    /// Show (@0x141009980): NavigateToNoticeUrl, gotoAndPlay("show"), the show button selected,
    /// the browser shown; SetTopMostLayer has one movie per window here.
    /// </summary>
    void Show()
    {
        if (Host.Entity == null) return;
        NavigateToNoticeUrl();
        Host.Invoke(Host.Entity, "gotoAndPlay", "show");
        Host.Set(showButton, "selected", true);                    // ASButton::SetSelected
        ShowBrowser(true);
    }

    /// <summary>Hide (@0x141009a60): the browser hidden, gotoAndPlay("hide"), the show button unselected, the browser closed
    /// (not ported), no hide time.</summary>
    void Hide()
    {
        if (Host.Entity == null) return;
        ShowBrowser(false);
        Host.Invoke(Host.Entity, "gotoAndPlay", "hide");
        Host.Set(showButton, "selected", false);
        hideAt = 0;
    }
}
