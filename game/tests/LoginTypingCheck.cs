using Godot;
using Ms2.Formats.Options;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Display check (res://tests/login_typing.tscn; args after `--`: --size WxH for a full window of that content size,
/// --typing-shots DIR for the picture after each field): the login screen as the game opens it, with the run's message box
/// layer and tooltip layer under the tree's root (<see cref="Flow.GameFlow"/>), takes typed keys into both fields. The
/// keys go through the engine's input as a keyboard's do (key events with their characters): an id into the id field
/// the dialog focuses, Tab to the password field, a password. The idle message box layer, the root's last child, took
/// every key first, so nothing reached the login dialog. Quits with 0 when both fields hold what was typed, else 1.
/// </summary>
public partial class LoginTypingCheck : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        var driver = new LoginTypingDriver();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--typing-shots") driver.ShotDir = args[i + 1];
            if (args[i] == "--size" && args[i + 1].Split('x') is [var w, var h]) driver.Size = new Vector2I(int.Parse(w), int.Parse(h));
        }
        if (driver.Size is { } s) ScreenMode.ApplyRequest(new WindowRequest(false, true, s.X, s.Y));
        MessageBoxLayer.Ensure(this);   // as GameFlow._Ready: the run's layers, then the login scene
        TooltipLayer.Ensure(this);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, Flow.GameFlow.LoginScene);
    }
}

/// <summary>The check's steps; under the tree's root, so it outlives the scene change to the login screen.</summary>
public partial class LoginTypingDriver : Node
{
    public string ShotDir;
    public Vector2I? Size;
    const string Id = "abc12", Password = "secret";
    int frames;

    static void Press(Key key, char ch)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = ch, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    static void Type(string text)
    {
        foreach (var ch in text) Press(char.IsAsciiLetter(ch) ? (Key)char.ToUpperInvariant(ch) : (Key)ch, ch);
    }

    void Shot(string name)
    {
        if (ShotDir == null) return;
        DirAccess.MakeDirRecursiveAbsolute(ShotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(ShotDir, name));
    }

    public override void _Process(double delta)
    {
        frames++;
        if (GetTree().CurrentScene is not LoginScene login)
        {
            if (frames > 600) { GD.Print("login typing: FAIL no login scene"); GetTree().Quit(1); }
            return;
        }
        if (frames == 120) Type(Id);
        if (frames == 150) { Shot("login_id_typed.png"); Press(Key.Tab, '\t'); }
        if (frames == 155) Type(Password);
        if (frames < 185) return;
        Shot("login_password_typed.png");
        bool ok = login.Controller.Id == Id && login.Controller.Password.Length == Password.Length;
        GD.Print($"login typing: window {GetTree().Root.Size}, content {GetTree().Root.GetVisibleRect().Size}, id \"{login.Controller.Id}\", password {login.Controller.Password.Length} chars -> {(ok ? "ok" : "FAIL")}");
        SetProcess(false);
        GetTree().Quit(ok ? 0 : 1);
    }
}
