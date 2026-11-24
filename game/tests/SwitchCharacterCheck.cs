using Godot;
using Ms2Godot.Flow;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Live check on a login server (res://tests/switch_character.tscn, with the launcher's --ip and --port after `--`):
/// log in through the login dialog (MS2_RETRY_ACCOUNT=user:password), start the selected character, then the system menu's
/// character select (UISystemMenuDialog::OnSelectCharacter, RequestQuit(0)) back to the list and into the game again,
/// --rounds N times (default 2). Each GameToLogin return and each game entry connects to the endpoint in its packet; the
/// session prints it. Each return puts the loading screen up for map 2 with the server move's line (showMove) before the list
/// comes (OnGameToLogin's FUN_141bbc580(0, 2)), and the list takes input with the move's cover down. Quits with 0 when every
/// round got back to the list and into the game, else 1.
/// </summary>
public partial class SwitchCharacterCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new SwitchCharacterDriver());
        GameFlow.Start(GetTree());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene changes.</summary>
public partial class SwitchCharacterDriver : Node
{
    int rounds = 2, failures;

    async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async System.Threading.Tasks.Task<bool> Until(System.Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++) { if (cond()) return true; await Frames(1); }
        return cond();
    }
    GameFlow Flow => GetTree().Root.GetNodeOrNull<GameFlow>("GameFlow");

    static void Key(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    async System.Threading.Tasks.Task<bool> IntoGame(int round)
    {
        if (!await Until(() => GetTree().CurrentScene is CharacterListScene { ListReady: true } && Ms2.Ui.Hud.MessageBoxQueue.IsIdle, 3600)) return Done($"round {round}: character select never took input");
        await Frames(30);
        Key(Godot.Key.Enter);
        if (!await Until(() => Flow.Current == GameFlow.State.Game, 3600)) return Done($"round {round}: the game state was never reached");
        GD.Print($"switch character: round {round} in the game");
        await Frames(180);
        return true;
    }

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--rounds") int.TryParse(args[i + 1], out rounds);
        var acct = (System.Environment.GetEnvironmentVariable("MS2_RETRY_ACCOUNT") ?? "").Split(':', 2);
        if (!await Until(() => Flow?.Current == GameFlow.State.Login && GetTree().CurrentScene is LoginScene s && s.Controller.Host?.Entity != null, 3000)) { Done("no login dialog"); return; }
        await Frames(60);
        var c = ((LoginScene)GetTree().CurrentScene).Controller;
        foreach (var (field, text) in new[] { ("m_inputID", acct[0]), ("m_inputPW", acct.Length > 1 ? acct[1] : "") })
            if (c.Host.Get(c.Host.Entity, field) is { } input) c.Host.Set(input, "text", text);
        await Frames(5);
        Key(Godot.Key.Enter);
        if (!await Until(() => Flow.Current == GameFlow.State.Lobby, 1800)) { Done("the login did not reach character select"); return; }
        if (!await IntoGame(0)) return;
        for (int r = 1; r <= rounds; r++)
        {
            Net.LiveSession.Instance?.SendRequestQuit();   // the system menu's character select
            bool cover = false, showMove = false;
            if (!await Until(() =>
                {
                    if (World.FieldContents.CoverUp) { cover = true; showMove |= World.FieldContents.CoverShowsMove; }
                    return Flow.Current == GameFlow.State.Lobby;
                }, 1800)) { Done($"round {r}: the return never reached character select (flow {Flow.Current})"); return; }
            GD.Print($"switch character: round {r} back at character select (loading screen {(cover ? "up" : "not seen")}, showMove {showMove}, move cover {(Ms2.Ui.FieldMoveCover.Up ? "up" : "down")})");
            if (!cover || !showMove) { Done($"round {r}: no server-move loading screen before character select"); return; }
            if (Ms2.Ui.FieldMoveCover.Up) { Done($"round {r}: the move's cover is still up at character select"); return; }
            if (r == 1) GD.Print($"switch character: retry line 1: {Net.LiveSession.RetryLine(1)}");
            if (!await IntoGame(r)) return;
        }
        Done(null);
    }

    bool Done(string fail)
    {
        if (fail != null) { failures++; GD.Print("switch character: FAIL " + fail); }
        GD.Print($"switch character: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
        return false;
    }
}
