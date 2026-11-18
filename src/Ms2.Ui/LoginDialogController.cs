namespace Ms2.Ui;

/// <summary>
/// UILoginDialog (ms2/client/ms2/uilogindialog.cpp): the login dialog's C++ side over the uilogindialog movie
/// (scaleform.ms2.controls.dialog.LoginDialog). What the client does with the answers (the login request, the connect)
/// is the host's: <see cref="Connect"/>, <see cref="EndPopdown"/>.
/// </summary>
public sealed class LoginDialogController : UiController
{
    public override string Movie => "uilogindialog";
    protected override string EntityMethod => "initData";

    /// <summary>
    /// No startOpen on load: UIDialog::OnLoadComplete invokes it only when m_c4 is set (uidialog.cpp:704-719), m_c4 starts
    /// at 0 (uidialog.cpp:199) and only SetTweenEnabled sets it, which UILoginDialog's constructor does not call
    /// (uilogindialog.cpp:166-174). The movie's own timeline plays its "popup" label.
    /// </summary>
    protected override bool IsDialog => false;

    /// <summary>The id initData gets: DAT_143af37a0 (an id given at start-up), else GameOption's saved id.</summary>
    public string SavedId = "";
    /// <summary>GetRegionCode(GetRegion()), initData's fourth argument.</summary>
    public string RegionCode = "KR";

    /// <summary>GameOption m_bSaveId, as OnLoadComplete and OnSaveID set it.</summary>
    public bool SaveId { get; private set; }

    /// <summary>OnConnect (uilogindialog.cpp:523-538): on the login scene, UISceneLogin::FUN_141acfad0 with this dialog's id.</summary>
    public event Action Connect;
    /// <summary>OnEndPopdown(1) (uilogindialog.cpp:190-201): the connect to the login server.</summary>
    public event Action EndPopdown;

    /// <summary>OnLoadComplete (uilogindialog.cpp:263-279): initData(id, saveId = id given, localedName = true, region).</summary>
    public override void OnLoadComplete()
    {
        SaveId = SavedId.Length > 0;
        Host.Invoke(Host.Entity, "initData", SavedId, SaveId, true, RegionCode);
    }

    /// <summary>vfunc_28 (uilogindialog.cpp:330-393).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Is(name, "OnConnect")) { Connect?.Invoke(); return true; }
        if (Is(name, "OnSaveID"))
        {
            if (args.Length == 0) return false;
            SaveId = args[0] is bool b && b;
            return true;
        }
        if (Is(name, "OnEndPopdown")) { EndPopdown?.Invoke(); return true; }
        if (Is(name, "OnChkLocaleName"))
        {
            // unchecked after 2019-02-06 15:00: a message box "you must use a locale account" (the client's Korean text) whose answer
            // (FUN_1419e2690, uilogindialog.cpp:476-482) selects the box again
            if (args.Length > 0 && !(args[0] is bool c && c)) LocaleNameRequired?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>The locale-name box was unchecked: the host shows the message box, then calls <see cref="SelectLocaleName"/>.</summary>
    public event Action LocaleNameRequired;

    /// <summary>FUN_1419e2690 (uilogindialog.cpp:476-482): m_chkLocaledName selected again.</summary>
    public void SelectLocaleName()
    {
        if (Host.Get(Host.Entity, "m_chkLocaledName") is { } box) Host.Set(box, "selected", true);
    }

    /// <summary>vfunc_13 (uilogindialog.cpp:310-327): Enter -> onEnterKey.</summary>
    public void EnterKey() => Host.Invoke(Host.Entity, "onEnterKey");

    /// <summary>vfunc_13 (uilogindialog.cpp:310-327): Tab -> onTabKey.</summary>
    public void TabKey() => Host.Invoke(Host.Entity, "onTabKey");

    /// <summary>ShowConnectFailure (uilogindialog.cpp:500-507): failConnect, popup.</summary>
    public void ShowConnectFailure()
    {
        Host.Invoke(Host.Entity, "failConnect");
        Host.Invoke(Host.Entity, "popup");
    }

    /// <summary>FUN_1419e28a0 (uilogindialog.cpp:510-514): popdown.</summary>
    public void Popdown() => Host.Invoke(Host.Entity, "popdown");

    /// <summary>FUN_1419e2370 (uilogindialog.cpp:438-446): m_inputID's text.</summary>
    public string Id => InputText("m_inputID");

    /// <summary>FUN_1419e2450 (uilogindialog.cpp:449-457): m_inputPW's text.</summary>
    public string Password => InputText("m_inputPW");

    string InputText(string member) =>
        Host.Get(Host.Entity, member) is { } input && Host.Get(input, "text") is string s ? s : "";

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
