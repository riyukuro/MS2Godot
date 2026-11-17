using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The first UI scene: the login backdrop (uiscenelogin) and the login dialog (uilogindialog) built from the player's
/// own Gfx.m2d. Tab and Enter work, the Login button raises the contract callback (printed, no network).
/// Args after `--`: --shots DIR renders a default and a demo shot (hover, typed id) and quits.
/// </summary>
public partial class LoginScene : Control
{
    UiAssets assets;
    MovieClipNode dialogRoot;
    LoginDialogLogic logic;
    UiWidgets.MouseRouter mouse;
    string shotDir;
    int frames;
    int stage;

    public override void _EnterTree() => LobbyLayout.UseWindowPixels(this);

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        Color clear = new Color(0.1f, 0.1f, 0.12f);
        for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "--shots") shotDir = args[i + 1]; if (args[i] == "--clear") clear = Color.FromHtml(args[i + 1]); }
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }

        assets = new UiAssets(new PackSet(data));
        var window = GetViewportRect().Size;    // window pixels (LobbyLayout.UseWindowPixels)
        float u = UiScale.For(window);
        var stage = UiScale.MovieStage(window);
        RenderingServer.SetDefaultClearColor(clear);

        var movie = new UiMovie(assets, UILoginDialogContract.Movie);
        dialogRoot = movie.BuildRoot(u);
        dialogRoot.Scale = new Vector2(u, u);
        UiWidgets.ApplyInspector(dialogRoot, UILoginDialogContract.Inspector);
        var dlg = (MovieClipNode)LobbyLayout.Dialog(dialogRoot).Node;   // login_dialog: the LoginDialog
        // LoginDialog.alignment: no applyScale (CommonPath.flash is false, set by Main.addedToStage), so the dialog is drawn
        // at the UI scale only, at Align.alignment(this, ALIGN_BOTTOMCENTER, 0, round(-39 * getUIScale(stage))). That runs
        // on the first frame of the "popup" animation, where this.width / height is m_dialog at 0.8 from the origin
        // (340.8 x 176), not its size once the popup has played (426 x 220 from (-43.5, -19)).
        dlg.GotoFrame(0);
        var size = dlg.ContentRect()?.Size ?? Vector2.Zero;
        int dy = (int)LobbyLayout.AsRound(-39 * LobbyLayout.AsUiScale(stage));
        LobbyLayout.Place(dialogRoot, new Vector2((stage.X - size.X) * 0.5f, stage.Y - size.Y + dy), u);   // Align.computePosition
        dlg.GotoFrame(8);                       // the last frame of the "popup" animation (played below unless taking shots)
        AddChild(dialogRoot);
        if (shotDir == null) dlg.PlayRange(0, 8);

        logic = new LoginDialogLogic(dialogRoot);
        logic.InitData("", false, true, "KR");
        mouse = new UiWidgets.MouseRouter(dialogRoot);
        UiBridge.ToHost += (m, n, a) => { };
    }

    public override void _Input(InputEvent e)
    {
        mouse?.Handle(e);
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode is Key.Enter or Key.KpEnter) { logic.OnEnterKey(); GetViewport().SetInputAsHandled(); }
            else if (k.Keycode == Key.Tab) { logic.OnTabKey(); GetViewport().SetInputAsHandled(); }
        }
    }

    public override void _Process(double delta)
    {
        if (shotDir == null) return;
        frames++;
        if (frames == 8) Save("login_default.png");
        if (frames == 9)
        {
            // demo: type an id, hover the login button
            logic.IdInput.Text = "player01";
            logic.IdInput.Edit.CaretColumn = 8;
            logic.PasswordInput.Text = "secret";
            logic.PasswordInput.Edit.Secret = true;
            logic.PasswordInput.Focus();
            logic.LoginButton.MouseOver(true);
        }
        if (frames == 11) { logic.OnTabKey(); GD.Print($"Tab: id focused={logic.IdInput.Focused}, password focused={logic.PasswordInput.Focused}"); }
        if (frames == 12) { logic.OnTabKey(); GD.Print($"Tab: id focused={logic.IdInput.Focused}, password focused={logic.PasswordInput.Focused}"); logic.OnEnterKey(); }
        if (frames == 18) { Save("login_demo.png"); GetTree().Quit(); }
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
