using Godot;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// The login dialog with no Flash at run time: native_login.tscn lays it out (positions from the movie), the widgets draw
/// art baked from the player's uilogindialog, and this script is scaleform.ms2.controls.dialog.LoginDialog ported:
/// initData, onConnect (once, until failConnect), onSaveID / onChkLocaleName (Event.SELECT), onEnterKey, onTabKey,
/// and LobbyDialog's popup (the "popup" label's frames replayed as a tween) and popdown. Calls to the game go through
/// the same contract as the Flash path (<see cref="UILoginDialogContract.ToHost"/>).
/// Args after `--`: --native-shots DIR saves login_default.png and login_demo.png and quits.
/// </summary>
public partial class NativeLoginScene : Control
{
    const string Movie = "uilogindialog";
    NWindow dialog;
    Control body;
    NTextInput inputId, inputPw;
    NButton lockBtn, helpBtn, loginBtn, chkLocaleName;
    bool mouseState = true;
    string localeName;
    string shotDir; int frames;
    public readonly List<string> Calls = new();

    public override void _EnterTree()
    {
        if (!NativeUi.Init()) { GetTree().Quit(2); return; }
        NativeUi.UseStageScale(GetWindow(), 1024);    // LobbyDialog.applyScale: Common.getLobbyRatioY (stage height 1024)
        RenderingServer.SetDefaultClearColor(new Color(0.1f, 0.1f, 0.12f));
    }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--native-shots") shotDir = args[i + 1];
        dialog = GetNode<NWindow>("LoginDialog");
        body = dialog.GetNode<Control>("m_dialog");
        inputId = body.GetNode<NTextInput>("m_inputID");
        inputPw = body.GetNode<NTextInput>("m_inputPW");
        lockBtn = body.GetNode<NButton>("m_lock");
        helpBtn = body.GetNode<NButton>("m_help");
        loginBtn = body.GetNode<NButton>("m_login");
        chkLocaleName = body.GetNode<NButton>("m_chkLocaledName");

        // configUI
        lockBtn.Select += sel => Raise(UILoginDialogContract.ToHost.OnSaveID, sel);
        chkLocaleName.Select += sel => Raise(UILoginDialogContract.ToHost.OnChkLocaleName, sel);
        loginBtn.Click += OnConnect;
        helpBtn.Click += () => { };                  // onHelp: empty in the movie
        NTooltip.Attach(lockBtn, () => "$s_login_msg_saveid");
        UiBridge.ToHost += (m, n, a) => { if (m == UILoginDialogContract.Movie) Calls.Add(n); };

        InitData("", false, true, "KR");
        if (shotDir == null) Popup(); else SetPopupFrame(8);
    }

    void Raise(string method, params object[] args) => UiBridge.Raise(UILoginDialogContract.Movie, method, args);

    /// <summary>initData(id, saveId, useLocaleName, region).</summary>
    public void InitData(string id, bool saveId, bool useLocaleName, string region)
    {
        if (id.Length > 0) inputId.Text = id;
        inputId.Focus();
        inputId.SelectAll();
        lockBtn.Selected = saveId;
        chkLocaleName.Selected = useLocaleName;
        localeName = region;
    }

    public void OnConnect()
    {
        if (mouseState)
        {
            SetMouseState(false);
            Raise(UILoginDialogContract.ToHost.OnConnect);
        }
    }

    /// <summary>failConnect: the host answers a failed login.</summary>
    public void FailConnect() => SetMouseState(true);

    /// <summary>LobbyDialog.setMouseState: mouseChildren / mouseEnabled off while a login is pending (a blocker on top).</summary>
    void SetMouseState(bool on)
    {
        mouseState = on;
        if (blocker == null)
        {
            blocker = new Control { MouseFilter = MouseFilterEnum.Stop, Size = dialog.Size };
            body.AddChild(blocker);
        }
        blocker.Visible = !on;
    }
    Control blocker;

    public void OnEnterKey()
    {
        if (!mouseState) return;
        if (inputId.Length > 0) OnConnect(); else inputId.Focus();
    }

    public void OnTabKey()
    {
        if (!mouseState) return;
        if (!inputId.Focused && !inputPw.Focused) inputId.Focus();
        else if (inputId.Focused) inputPw.Focus();
        else inputId.Focus();
    }

    // ---- LobbyDialog popup / popdown: login_dialog's labels replayed (60 fps) ---------------------------------------

    List<(Transform2D Xf, float Alpha)> track;
    Vector2 rest;

    void SetPopupFrame(int f)
    {
        track ??= NativeUi.Art.Track(Movie, "login_dialog", "m_dialog", 0, 34);
        rest = track[8].Xf.Origin;
        var (xf, a) = track[Math.Clamp(f, 0, track.Count - 1)];
        body.Position = xf.Origin - rest;
        body.Scale = xf.Scale;
        body.Modulate = new Color(1, 1, 1, a);
    }

    void Play(int from, int to, Action done = null)
    {
        SetPopupFrame(from);
        var tw = CreateTween();
        for (int f = from + 1; f <= to; f++) { int k = f; tw.TweenInterval(1 / 60.0); tw.TweenCallback(Callable.From(() => SetPopupFrame(k))); }
        if (done != null) tw.TweenCallback(Callable.From(done));
    }

    public void Popup() => Play(0, 8);

    /// <summary>popdown: frames 9..34, then OnEndPopdown.</summary>
    public void Popdown() => Play(9, 34, () => Raise(UILoginDialogContract.ToHost.OnEndPopdown));

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode is Key.Enter or Key.KpEnter) { OnEnterKey(); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Tab) { OnTabKey(); GetViewport().SetInputAsHandled(); }
        }
    }

    public override void _Process(double delta)
    {
        if (shotDir == null) return;
        frames++;
        if (frames == 10) Save("login_default.png");
        if (frames == 11)
        {
            // demo: type an id, Tab to the password, hover the login button
            inputId.Text = "player01";
            inputId.Edit.CaretColumn = 8;
            OnTabKey();
            inputPw.Text = "secret";
            inputPw.Edit.CaretColumn = 6;
            GD.Print($"Tab: id focused={inputId.Focused}, password focused={inputPw.Focused}");
            loginBtn.SetState("over");
        }
        if (frames == 14) { OnTabKey(); GD.Print($"Tab: id focused={inputId.Focused}, password focused={inputPw.Focused}"); OnTabKey(); }
        if (frames == 16) { lockBtn.HandleClick(); OnEnterKey(); OnEnterKey(); GD.Print("host calls: " + string.Join(", ", Calls)); }
        if (frames == 40) { Save("login_demo.png"); FailConnect(); Popdown(); }
        if (frames == 52) Save("login_popdown.png");
        if (frames == 80) GetTree().Quit();
    }


    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
