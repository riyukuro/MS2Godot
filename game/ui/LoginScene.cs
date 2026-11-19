using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The first UI scene (UISceneLogin, 0x6200009): the login backdrop (the month's background movie), the scene's own
/// movie (uiscenelogin: logo, rating marks, loading bar, the bottom left buttons) with UISceneLogin's C++ side
/// (<see cref="SceneLoginController"/>), and the login dialog, run from its own movie (uilogindialog) with UILoginDialog's
/// C++ side (<see cref="LoginDialogController"/>), which the scene opens once its load progress is full. Enter and Tab go to the movie as the client's
/// vfunc_13 sends them; the Login button raises the contract callback for the flow.
/// Args after `--`: --shots DIR saves login_default.png and, with --type ID, login_id_typed.png (the id typed into the
/// focused id field) and login_password_typed.png (Tab, then a password), then quits; with --resize WxH the window is then resized and a third shot taken.
/// </summary>
public partial class LoginScene : Control
{
    UiAssets assets;
    MovieClipNode background;
    LoginBackground.Measure backgroundSize;
    MovieDialog sceneMovie, dialog;
    UiModel model;
    Vector2 window;
    public readonly LoginDialogController Controller = new();
    public readonly SceneLoginController Scene = new();
    string shotDir, typeId;
    Vector2I? resizeTo;
    int frames;

    public override void _EnterTree() => LobbyLayout.UseWindowPixels(this);

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        Color clear = new Color(0.1f, 0.1f, 0.12f);
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--shots") shotDir = args[i + 1];
            if (args[i] == "--clear") clear = Color.FromHtml(args[i + 1]);
            if (args[i] == "--type") typeId = args[i + 1];
            if (args[i] == "--resize" && args[i + 1].Split('x') is [var w, var h] && int.TryParse(w, out int rw) && int.TryParse(h, out int rh)) resizeTo = new Vector2I(rw, rh);
        }
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }

        assets = new UiAssets(PackSet.Shared(data));
        window = GetViewportRect().Size;        // window pixels (LobbyLayout.UseWindowPixels)
        RenderingServer.SetDefaultClearColor(clear);

        MouseFilter = MouseFilterEnum.Ignore;   // the movie view takes the mouse as unhandled input
        AddBackground(window, args.Contains("--flow-shots") || shotDir != null);
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the login movies run interpreted");
        Controller.Connect += () => GD.Print("login: OnConnect");
        Controller.Connect += () => UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);
        model = new UiModel(GameTables.Shared(assets.Packs, assets.Language));
        AddScene();
        // the client sizes every movie to the back buffer: on a window resize the backdrop runs onResize
        // (GFxMovieBackground::vfunc_16) and the open dialogs align again (UIDialog::vfunc_43 -> alignment)
        GetViewport().SizeChanged += QueueRelayout;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= QueueRelayout;

    bool relayoutQueued;
    void QueueRelayout()
    {
        if (relayoutQueued) return;
        relayoutQueued = true;
        CallDeferred(MethodName.Relayout);
    }

    /// <summary>The backdrop fitted again; the dialog gets the new stage and aligns again (UIDialog::vfunc_43 -> alignment).</summary>
    void Relayout()
    {
        relayoutQueued = false;
        if (!IsInsideTree()) return;
        var window = GetViewportRect().Size;
        if (background != null) LoginBackground.Fit(background, window, backgroundSize);
        sceneMovie?.Relayout(window);
        dialog?.Relayout(window);
    }

    /// <summary>
    /// The backdrop (LoginBackground): this month's movie from table/loginbgbgm.xml, picked at random as the client does,
    /// or the month's first one for shots (repeatable). Shots show it settled: the title on its last frame, the intro gone.
    /// </summary>
    void AddBackground(Vector2 window, bool forShots)
    {
        var url = LoginBackground.Select(assets.Table("table/loginbgbgm.xml"), DateTime.Now.Month, n => forShots ? 0 : (int)(GD.Randi() % (uint)n));
        if (string.IsNullOrEmpty(url)) return;
        MovieClipNode bg;
        try { bg = new UiMovie(assets, LoginBackground.MovieName(url)).BuildRoot(); }
        catch (Exception e) { GD.PushWarning($"login backdrop {url}: {e.Message}"); return; }
        backgroundSize = LoginBackground.Measured(bg);
        LoginBackground.Fit(bg, window, backgroundSize);
        background = bg;
        AddChild(bg);
        LoginBackground.Play(bg, forShots);
    }

    /// <summary>
    /// The scene's movie over the backdrop. vfunc_49 (entering the scene) starts the progress; the backdrop is already
    /// loaded, so the load runs vfunc_60 (the title shown, the backdrop on to endTitleAni). vfunc_55 (bit 1) comes when
    /// the login field is loaded (OnLoadComplete's WorldService::FUN_141bcfbd0, state 4); the backdrop is that field here.
    /// </summary>
    void AddScene()
    {
        var packs = assets.Packs;
        Scene.IsFeatureEnabled = id => id >= 1 && id <= FeatureTable.Names.Length && LobbyFeatures.IsEnabledByName(packs, FeatureTable.Names[id - 1]);
        Scene.BackgroundGotoAndPlay = label => background != null && LoginBackground.GotoAndPlay(background, label);
        Scene.OpenLoginDialogs += OpenLoginDialog;
        Scene.GameExit += ShowGameExitBox;
        Scene.GameOption += () => GD.Print("login: OnGameOption (the options dialog is not hosted on this scene yet)");
        Scene.Prev += () => GD.Print("login: OnPrev (the mode select scene is not ported)");
        Scene.Enter();
        if (background != null) Scene.BackgroundLoaded();
        sceneMovie = new MovieDialog(assets, Scene, model, window);
        AddChild(sceneMovie);
        if (background != null) Scene.SceneShown();
    }

    /// <summary>OpenLoginDialogs (uiscenelogin.cpp, @0x141acfa00): the login dialog 0x4e22 over the scene's movie.</summary>
    void OpenLoginDialog()
    {
        if (dialog != null) return;
        dialog = new MovieDialog(assets, Controller, model, window);
        AddChild(dialog);
    }

    /// <summary>OnGameExit (FUN_1418fc760, uichatballoon.cpp:467): FUN_140d73bc0(0x288 s_msg_game_over, 0x17, 0, "gameexit");
    /// the message box layer's yes answer quits (GameFlow).</summary>
    void ShowGameExitBox()
    {
        var common = new GameStrings(assets.Packs, assets.Language).Common();
        MessageBoxQueue.ShowMessageBox(common("s_msg_game_over") ?? "", 0x17, null, "gameexit");
    }

    /// <summary>
    /// The login background as it is when the character select opens over it: the login dialog gone (the client
    /// pops it down before the list opens), one frame drawn, then kept.
    /// </summary>
    public async System.Threading.Tasks.Task<ImageTexture> CaptureBackdrop()
    {
        if (dialog != null) dialog.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return ImageTexture.CreateFromImage(GetViewport().GetTexture().GetImage());
    }

    /// <summary>
    /// UISceneLogin::vfunc_13: Escape skips the title. UILoginDialog::vfunc_13 (uilogindialog.cpp:310-327): Enter ->
    /// onEnterKey, Tab -> onTabKey, ahead of the movie.
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && Scene.EscapePressed())
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (dialog == null || !dialog.Visible) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode is Key.Enter or Key.KpEnter) { Controller.EnterKey(); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Tab) { Controller.TabKey(); GetViewport().SetInputAsHandled(); }
        }
    }

    public override void _Process(double delta)
    {
        Scene.Tick();       // UISceneLogin::vfunc_15
        if (shotDir == null) return;
        frames++;
        if (frames == 90) Save("login_default.png");
        if (frames == 91 && typeId != null)
            foreach (var ch in typeId)
            {
                // typed into the id field initData focused
                dialog.Player.Key(0, ch, true);
                dialog.Player.Key(0, 0, false);
            }
        if (frames == 120 && typeId != null)
        {
            Save("login_id_typed.png");
            Controller.TabKey();                 // to the password field (its TextField takes the focus when it draws)
        }
        if (frames == 125 && typeId != null)
            foreach (var ch in "secret") { dialog.Player.Key(0, ch, true); dialog.Player.Key(0, 0, false); }
        if (frames == 135 && typeId != null)
        {
            // a click on the Login button (window pixels)
            var at = new Vector2(640, 670);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }
        if (frames == 140)
        {
            if (typeId != null) Save("login_password_typed.png");
            if (resizeTo == null) GetTree().Quit();
            else GetWindow().Size = resizeTo.Value;          // the window resized after the scene is up
        }
        if (frames == 170) { Save($"login_resized_{resizeTo?.X}x{resizeTo?.Y}.png"); GetTree().Quit(); }
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name + $" (id \"{Controller.Id}\", password {Controller.Password.Length} chars)");
    }
}
