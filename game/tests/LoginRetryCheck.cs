using Godot;
using Ms2Godot.Flow;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Live check on a login server (res://tests/login_retry.tscn, with the launcher's --ip and --port after `--`): the login
/// dialog's own fields and the Enter key, as a player logs in. MS2_RETRY_ACCOUNT=user:password is the account.
/// <list type="bullet">
/// <item>--case password: a wrong password, the refusal's box answered, then the right password: the second try must reach
/// character select.</item>
/// <item>--case hold: log in, Start the selected character, and once in the game end the process at once (the connection
/// drops without a quit, as a crash or a closed window does); with --stay N it stays in the game N seconds instead, then
/// quits normally.</item>
/// <item>--case already: log in; each refusal's box is answered 15 s later. An AlreadyLogin box's OK must log in again by
/// itself; after any other refusal the login is tried again with Enter. Up to --tries N refusals (default 12). Passes when a
/// try reaches character select; prints the refusals it got.</item>
/// </list>
/// Quits with 0 when the case holds, else 1.
/// </summary>
public partial class LoginRetryCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new LoginRetryDriver());
        GameFlow.Start(GetTree());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene changes.</summary>
public partial class LoginRetryDriver : Node
{
    string mode = "password", user = "", pass = "";
    int tries = 12, stay, failures;

    void Fail(string what) { failures++; GD.Print("login retry: FAIL " + what); }

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

    /// <summary>The id and password into the dialog's fields (m_inputID, m_inputPW), then Enter.</summary>
    async System.Threading.Tasks.Task<bool> TryLogin(string password)
    {
        if (!await Until(() => GetTree().CurrentScene is LoginScene s && s.Controller.Host?.Entity != null && Ms2.Ui.Hud.MessageBoxQueue.IsIdle, 1200))
        { Fail("the login dialog is not up"); return false; }
        var c = ((LoginScene)GetTree().CurrentScene).Controller;
        foreach (var (field, text) in new[] { ("m_inputID", user), ("m_inputPW", password) })
            if (c.Host.Get(c.Host.Entity, field) is { } input) c.Host.Set(input, "text", text);
        await Frames(5);
        Key(Godot.Key.Enter);
        // the login goes out and the dialog pops down; then the answer
        await Frames(10);
        var live = Net.LiveSession.Instance;
        await Until(() => live != null && !live.Busy, 1800);
        return Flow.Current == GameFlow.State.Lobby || await Until(() => Flow.Current == GameFlow.State.Lobby, 60);
    }

    /// <summary>After a refusal: the box is up, the flow stays on the login, and the box is answered (OK).</summary>
    async System.Threading.Tasks.Task<bool> AnswerRefusal()
    {
        var r = Net.LiveSession.Instance?.LastLoginRefusal;
        if (r == null) { Fail("the login failed without a refusal (no server answer)"); return false; }
        GD.Print($"login retry: refused, state {r.State} ({r.Message})");
        if (!await Until(() => !Ms2.Ui.Hud.MessageBoxQueue.IsIdle, 120)) Fail("no box for the refusal");
        if (Flow.Current != GameFlow.State.Login) Fail($"the flow left the login: {Flow.Current}");
        await Frames(30);
        Key(Godot.Key.Enter);   // the box's OK, as a player answers it
        if (!await Until(() => Ms2.Ui.Hud.MessageBoxQueue.IsIdle, 60)) { GD.Print("login retry: NOTE Enter did not answer the box; closed with OK"); Ms2.Ui.Hud.MessageBoxQueue.Clear(0); }
        await Frames(30);
        return true;
    }

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--case") mode = args[i + 1];
            if (args[i] == "--tries") int.TryParse(args[i + 1], out tries);
            if (args[i] == "--stay") int.TryParse(args[i + 1], out stay);
        }
        var acct = (System.Environment.GetEnvironmentVariable("MS2_RETRY_ACCOUNT") ?? "").Split(':', 2);
        user = acct[0]; pass = acct.Length > 1 ? acct[1] : "";
        if (!await Until(() => Flow?.Current == GameFlow.State.Login && GetTree().CurrentScene is LoginScene, 3000)) { Done("no login scene"); return; }
        await Frames(60);
        switch (mode)
        {
            case "password":
                if (await TryLogin(pass + "x")) { Done("the wrong password logged in"); return; }
                if (!await AnswerRefusal()) { Done(null); return; }
                if (!await TryLogin(pass)) { Done("the second try with the right password did not reach character select"); return; }
                GD.Print("login retry: the second try reached character select");
                break;
            case "hold":
                if (!await TryLogin(pass)) { Done("the login did not reach character select"); return; }
                if (!await Until(() => GetTree().CurrentScene is CharacterListScene { ListReady: true }, 1800)) { Done("character select never took input"); return; }
                await Frames(30);
                Key(Godot.Key.Enter);
                if (!await Until(() => Flow.Current == GameFlow.State.Game, 3600)) { Done("the game state was never reached"); return; }
                await Frames(120);
                if (stay > 0)
                {
                    // the account stays in the game while another client logs in with it
                    GD.Print($"login retry: in the game, staying {stay} s");
                    await ToSignal(GetTree().CreateTimer(stay), SceneTreeTimer.SignalName.Timeout);
                    GD.Print($"login retry: still running after {stay} s (flow {Flow.Current})");
                    break;
                }
                GD.Print("login retry: in the game, the process ends without a quit");
                OS.Kill(OS.GetProcessId());
                return;
            case "already":
            {
                // each refusal's box stays up 15 s, then its OK; an AlreadyLogin box's OK logs in again by itself
                // (HandleAlreadyReconnectButton: ConnectToConfiguredServer), any other refusal is tried again with Enter
                bool reached = await TryLogin(pass);
                int refused = 0;
                while (!reached)
                {
                    if (++refused > tries) { Done($"no try of {tries} reached character select"); return; }
                    var state = Net.LiveSession.Instance?.LastLoginRefusal?.State ?? 0;
                    await ToSignal(GetTree().CreateTimer(15), SceneTreeTimer.SignalName.Timeout);
                    if (!await AnswerRefusal()) { Done(null); return; }
                    var live = Net.LiveSession.Instance;
                    if (state is 4 or 5)
                    {
                        if (!await Until(() => live.Busy || Flow.Current == GameFlow.State.Lobby, 300)) { Done("the AlreadyLogin box's OK did not log in again"); return; }
                        GD.Print("login retry: the box's OK logged in again");
                        await Until(() => !live.Busy, 1800);
                        reached = Flow.Current == GameFlow.State.Lobby || await Until(() => Flow.Current == GameFlow.State.Lobby, 60);
                    }
                    else reached = await TryLogin(pass);
                }
                GD.Print($"login retry: character select reached after {refused} refusal(s)");
                if (refused == 0) GD.Print("login retry: NOTE the server did not refuse the first try");
                break;
            }
            default: Done("unknown case " + mode); return;
        }
        Done(null);
    }

    void Done(string fail)
    {
        if (fail != null) Fail(fail);
        GD.Print($"login retry ({mode}): {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
