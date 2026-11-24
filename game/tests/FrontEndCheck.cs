using Godot;
using Ms2.Ui.Hud;
using Ms2Godot.Flow;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Live check of the front end's connection errors (res://tests/front_end.tscn, with the launcher's --ip and --port after
/// `--`), MS2_RETRY_ACCOUNT=user:password the account. The server side is stopped by the script that runs it:
/// <list type="bullet">
/// <item>--case login-down: the login server is down. The login's connect fails: box s_login_err_connect with the socket
/// error (window message 0x467) on the login scene; its OK leaves the login scene up.</item>
/// <item>--case start --expect E: log in, print "front end: at character select", wait for the file MS2_FE_GO (the script
/// stops a server then makes it), then Start the selected character twice. Each start's box must come with the list down,
/// and its OK must leave the list up and taking input. The list pops up as the box opens (vfunc_79 case 0xe calls ShowPopup
/// with the refusal's box, as the client does); the box takes the input while it is up. E is connect (box s_login_err_connect with 10061, the game
/// server's connect refused), refused:N (OnLoginToGame's box for result N) or any.</item>
/// <item>--case lost: the same wait; the script stops the login server, whose shutdown resets the connection (socket error
/// 10054, --expect-code): the flow goes to the login scene with box s_login_err_disconnected, the error's reason and its
/// code (window message 0x465), and its OK leaves the login scene up. (A clean close, which shows nothing, is the unit
/// tests': this server's shutdown does not close cleanly.)</item>
/// </list>
/// A shot of each box and of the list after it into MS2_SHOTS. Quits with 0 when the case holds, else 1.
/// </summary>
public partial class FrontEndCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new FrontEndDriver());
        GameFlow.Start(GetTree());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene changes.</summary>
public partial class FrontEndDriver : Node
{
    string mode = "start", expect = "any";
    int expectCode = 10054;
    int failures, shots;

    async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async System.Threading.Tasks.Task<bool> Until(System.Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++) { if (cond()) return true; await Frames(1); }
        return cond();
    }
    GameFlow Flow => GetTree().Root.GetNodeOrNull<GameFlow>("GameFlow");
    CharacterListScene List => GetTree().CurrentScene as CharacterListScene;
    bool BoxUp => MessageBoxLayer.Instance is { Shown: true };

    static void Key(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    bool Fail(string why) { failures++; GD.Print("front end: FAIL " + why); return false; }

    void Shot(string kind)
    {
        var dir = System.Environment.GetEnvironmentVariable("MS2_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        string file = $"{++shots}_{mode}_{kind}.png";
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, file));
        GD.Print($"front end: shot {file}");
    }

    /// <summary>The account into the login dialog's fields, then Enter.</summary>
    async System.Threading.Tasks.Task<bool> TypeLogin()
    {
        if (!await Until(() => Flow?.Current == GameFlow.State.Login && GetTree().CurrentScene is LoginScene s && s.Controller.Host?.Entity != null && MessageBoxQueue.IsIdle, 3000))
            return Fail("no login dialog");
        await Frames(60);
        var acct = (System.Environment.GetEnvironmentVariable("MS2_RETRY_ACCOUNT") ?? "").Split(':', 2);
        var c = ((LoginScene)GetTree().CurrentScene).Controller;
        foreach (var (field, text) in new[] { ("m_inputID", acct[0]), ("m_inputPW", acct.Length > 1 ? acct[1] : "") })
            if (c.Host.Get(c.Host.Entity, field) is { } input) c.Host.Set(input, "text", text);
        await Frames(5);
        Key(Godot.Key.Enter);
        return true;
    }

    /// <summary>The list takes input (ShowPopup's SetInput(true)) with no box up and a character on it.</summary>
    async System.Threading.Tasks.Task<bool> ListUp(int frames = 1800) =>
        await Until(() => List is { ListReady: true } && MessageBoxQueue.IsIdle && Session.List().Count > 0, frames)
        || Fail($"the list did not take input again (flow {Flow?.Current}, list {(List == null ? "gone" : $"input {List.Controller?.InputOn}")}, box '{MessageBoxQueue.Current()?.Msg}')");

    /// <summary>Waits for the script's go file (it stopped a server).</summary>
    async System.Threading.Tasks.Task<bool> WaitGo()
    {
        var go = System.Environment.GetEnvironmentVariable("MS2_FE_GO");
        GD.Print("front end: at character select");
        if (string.IsNullOrEmpty(go)) return Fail("MS2_FE_GO is not set");
        for (int i = 0; i < 180 && !System.IO.File.Exists(go); i++)
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        if (!System.IO.File.Exists(go)) return Fail("the script never made " + go);
        GD.Print("front end: the script stopped its server");
        await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
        return true;
    }

    string ExpectedText()
    {
        if (expect == "connect") return Ms2.Ui.FrontEndConnection.ConnectFailed(10061, MessageBoxData.Common);
        if (expect.StartsWith("refused:", System.StringComparison.Ordinal))
            return Ms2.Ui.StartRefusal.Build(System.Convert.ToInt32(expect[8..], 16), "", MessageBoxData.CommonByKey)?.Msg;
        return null;
    }

    /// <summary>Start on the selected row: the list pops down, a box comes, its OK pops the list up.</summary>
    async System.Threading.Tasks.Task<bool> StartWithBox(int n)
    {
        if (!await ListUp()) return false;
        await Frames(30);
        Key(Godot.Key.Enter);
        if (!await Until(() => BoxUp || Flow.Current != GameFlow.State.Lobby, 3600)) return Fail($"start {n}: no box came");
        if (Flow.Current != GameFlow.State.Lobby) return Fail($"start {n}: the flow left character select: {Flow.Current}");
        await Frames(60);
        string msg = MessageBoxQueue.Current()?.Msg;
        GD.Print($"front end: start {n}: box '{msg}', list input {List?.Controller?.InputOn}");
        Shot($"start{n}_box");
        string want = ExpectedText();
        if (want != null && msg != want) Fail($"start {n}: the box says '{msg}', expected '{want}'");
        if (string.IsNullOrEmpty(msg)) Fail($"start {n}: the box is empty");
        MessageBoxLayer.Instance.Click(0);
        if (!await ListUp(900)) return false;
        await Frames(30);
        GD.Print($"front end: start {n}: the list popped up again, input {List.Controller.InputOn}");
        Shot($"start{n}_list");
        return true;
    }

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--case") mode = args[i + 1];
            if (args[i] == "--expect") expect = args[i + 1];
            if (args[i] == "--expect-code") int.TryParse(args[i + 1], out expectCode);
        }
        if (!await TypeLogin()) { Done(); return; }
        switch (mode)
        {
            case "login-down":
            {
                string want = Ms2.Ui.FrontEndConnection.ConnectFailed(10061, MessageBoxData.Common);
                if (!await Until(() => BoxUp, 1800)) { Fail("no box for the failed connect"); break; }
                await Frames(60);
                string msg = MessageBoxQueue.Current()?.Msg;
                GD.Print($"front end: login box '{msg}' (flow {Flow.Current})");
                Shot("box");
                if (msg != want) Fail($"the box says '{msg}', expected '{want}'");
                if (Flow.Current != GameFlow.State.Login) Fail($"the flow left the login: {Flow.Current}");
                MessageBoxLayer.Instance.Click(0);
                if (!await Until(() => MessageBoxQueue.IsIdle, 300)) Fail("the box's OK did not close it");
                await Frames(90);
                GD.Print($"front end: after the OK the flow is {Flow.Current}, scene {GetTree().CurrentScene?.GetType().Name}");
                Shot("after");
                if (GetTree().CurrentScene is not LoginScene) Fail("the login scene is gone");
                break;
            }
            case "start":
                if (!await Until(() => Flow.Current == GameFlow.State.Lobby, 1800)) { Fail("the login did not reach character select"); break; }
                if (!await ListUp() || !await WaitGo()) break;
                for (int n = 1; n <= 2; n++)
                    if (!await StartWithBox(n)) break;
                break;
            case "lost":
            {
                if (!await Until(() => Flow.Current == GameFlow.State.Lobby, 1800)) { Fail("the login did not reach character select"); break; }
                if (!await ListUp() || !await WaitGo()) break;
                if (!await Until(() => BoxUp && Flow.Current == GameFlow.State.Login, 1800)) { Fail($"no box on the login scene (flow {Flow.Current})"); break; }
                await Frames(60);
                string msg = MessageBoxQueue.Current()?.Msg;
                string want = Ms2.Ui.FrontEndConnection.Disconnected(expectCode, MessageBoxData.Common);
                GD.Print($"front end: lost box '{msg}' (flow {Flow.Current}, scene {GetTree().CurrentScene?.GetType().Name})");
                Shot("box");
                if (msg != want) Fail($"the box says '{msg}', expected '{want}'");
                MessageBoxLayer.Instance.Click(0);
                if (!await Until(() => MessageBoxQueue.IsIdle, 300)) Fail("the box's OK did not close it");
                await Frames(90);
                Shot("after");
                if (GetTree().CurrentScene is not LoginScene) Fail("the login scene is gone");
                break;
            }
            default: Fail("unknown case " + mode); break;
        }
        Done();
    }

    void Done()
    {
        GD.Print($"front end ({mode}): {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
