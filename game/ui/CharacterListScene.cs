using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// Character select (uicharacterlistdialog) built from the player's own Gfx.m2d, filled from a stub character list.
/// Click selects a row, a second click or Enter starts (OnGameStart / OnCreatePC); the delete and server list buttons
/// raise their contract callbacks, printed, no network. Args after `--`: --shots DIR renders a shot and quits.
/// </summary>
public partial class CharacterListScene : Control
{
    UiAssets assets;
    MovieClipNode root;
    CharacterListLogic logic;
    UiWidgets.MouseRouter mouse;
    string shotDir;
    int frames;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        Color clear = new Color(0.12f, 0.12f, 0.16f);
        for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "--shots") shotDir = args[i + 1]; if (args[i] == "--clear") clear = Color.FromHtml(args[i + 1]); }
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data)) { GD.PushError("Set MS2_DATA (the client's Data folder), or fill in local.cfg."); GetTree().Quit(2); return; }
        RenderingServer.SetDefaultClearColor(clear);
        assets = new UiAssets(new PackSet(data));
        var size = GetViewportRect().Size;
        float s = size.Y / 1200f;

        var movie = new UiMovie(assets, UICharacterListDialogContract.Movie);
        root = movie.BuildRoot(s);
        root.Scale = new Vector2(s, s);
        // the dialog is aligned to the right edge of the 1920 wide design space, top aligned
        root.Position = new Vector2(size.X - 1920f * s, -35f);
        UiWidgets.ApplyInspector(root, UICharacterListDialogContract.Inspector);
        var dlg = root.Children.First(c => c.Node is MovieClipNode).Node as MovieClipNode;
        dlg.GotoLabel("popup");
        AddChild(root);

        logic = new CharacterListLogic(movie, root);
        var chars = Flow.Session.List();       // the game flow's list (the stub list when this scene runs alone)
        logic.BuildCharacterList(chars, Flow.Session.SelectedIndex, 6);
        root.FindClip(UICharacterListDialogContract.Instances.M_content)?.SetNamedText("m_serverName", "Bigleaf  CH.1");
        root.FindClip(UICharacterListDialogContract.Instances.M_content)?.SetNamedText("m_regionName", "");
        root.FindClip(UICharacterListDialogContract.Instances.M_title)?.SetText(assets.Str("$s_charselect_title"));
        // region and event extras the host attaches on demand (attachHealthGameCN, attachBurningLevelupDescBtn, ...)
        foreach (var name in new[] { UICharacterListDialogContract.Instances.M_healthGameCN, UICharacterListDialogContract.Instances.M_characterCraeteShutdownCN, UICharacterListDialogContract.Instances.M_burningEventDescBtn })
            if (root.FindClip(name) is { } extra) extra.Visible = false;
        mouse = new UiWidgets.MouseRouter(root);
    }

    public override void _Input(InputEvent e)
    {
        mouse?.Handle(e);
        if (e is InputEventMouseButton mb) logic.OnMouse(mb);
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode is Key.Enter or Key.KpEnter) logic.OnEnterKey();
            else if (k.Keycode == Key.Down) logic.Select(Math.Min(logic.SelectedIndex + 1, CharacterListLogic.MaxRow - 1), true);
            else if (k.Keycode == Key.Up) logic.Select(Math.Max(logic.SelectedIndex - 1, 0), true);
        }
    }

    public override void _Process(double delta)
    {
        if (shotDir == null) return;
        frames++;
        if (frames == 10) Save("charselect_default.png");
        if (frames == 11) { logic.Select(1, true); logic.DeleteButton?.MouseOver(true); }
        if (frames == 12) logic.ClickStart();
        if (frames == 22) { Save("charselect_demo.png"); GetTree().Quit(); }
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
