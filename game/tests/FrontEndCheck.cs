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
/// <item>--case rename --name N --taken T --new W: N is a character whose name is due a change (the script made it longer
/// than the 12 letter form), T another character's name. The list's EndList flag is on; Start on N opens the rename box
/// (FUN_1418f13f0: type 10, s_char_rename_input, the character's id) with no popdown. Its OK with no name keeps the box;
/// T is refused by the server (CharacterList 7 code 2, common string 0x2a with the name); W renames the character, the
/// server sends the list again with the new name, and Start on W enters the game.</item>
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
    string mode = "start", expect = "any", renameFrom, renameTaken, renameTo;
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
            if (args[i] == "--name") renameFrom = args[i + 1];
            if (args[i] == "--taken") renameTaken = args[i + 1];
            if (args[i] == "--new") renameTo = args[i + 1];
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
            case "rename":
                await Rename();
                break;
            default: Fail("unknown case " + mode); break;
        }
        Done();
    }

    /// <summary><paramref name="name"/>'s row selected in the movie and the dialog (the preview loaded).</summary>
    async System.Threading.Tasks.Task<bool> Select(string name)
    {
        if (!await ListUp()) return false;
        int row = Session.List().FindIndex(c => c.User.Name == name);
        if (row < 0) return Fail($"'{name}' is not on the list ({string.Join(", ", Session.List().Select(c => c.User.Name))})");
        if (List.ListIndex != row || Session.SelectedIndex != row)
        {
            if (List.ListIndex == row) { List.SelectRow(-1); await Frames(5); }
            List.SelectRow(row);
            await Frames(10);
            if (!await ListUp()) return false;
        }
        await Frames(30);
        return List.ListIndex == row && Session.SelectedIndex == row
            || Fail($"row {row} of '{name}' was not selected (movie row {List.ListIndex}, dialog row {Session.SelectedIndex})");
    }

    /// <summary>Start (Enter) on the selected row until the rename box is up; false when another box or the game came.</summary>
    async System.Threading.Tasks.Task<MessageBoxData> RenameBox(string step, long id)
    {
        Key(Godot.Key.Enter);
        if (!await Until(() => BoxUp || Flow.Current != GameFlow.State.Lobby, 1800)) { Fail($"{step}: no box came"); return null; }
        if (Flow.Current != GameFlow.State.Lobby) { Fail($"{step}: the flow left character select: {Flow.Current}"); return null; }
        await Frames(60);
        var box = MessageBoxQueue.Current();
        GD.Print($"front end: {step}: box type {box?.Type} tag '{box?.Extra}' id {box?.ArgLong(0, 0)} '{box?.Msg}', list popped down {List?.Controller?.Starting}");
        Shot(step);
        if (box == null || box.Extra != Ms2.Ui.InvalidNameRename.Tag || box.Type != 10) { Fail($"{step}: not the rename box"); return null; }
        if (box.ArgLong(0, 0) != id) Fail($"{step}: the box's character is {box.ArgLong(0, 0)}, expected {id}");
        if (box.Msg != MessageBoxData.CommonByKey("s_char_rename_input")) Fail($"{step}: the box says '{box.Msg}'");
        if (List.Controller.Starting) Fail($"{step}: the start went on under the box");
        return box;
    }

    async System.Threading.Tasks.Task Rename()
    {
        if (string.IsNullOrEmpty(renameFrom) || string.IsNullOrEmpty(renameTaken) || string.IsNullOrEmpty(renameTo)) { Fail("--name, --taken and --new are needed"); return; }
        if (!await Until(() => Flow.Current == GameFlow.State.Lobby, 1800)) { Fail("the login did not reach character select"); return; }
        if (!await ListUp()) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        GD.Print($"front end: the list's EndList flag {live?.CheckNames}, the dialog's {List?.Controller?.CheckNames}");
        if (live?.CheckNames != true || List?.Controller?.CheckNames != true) { Fail("the list's flag is off: no name is due"); return; }
        if (!await Select(renameFrom)) return;
        long id = Session.List().First(c => c.User.Name == renameFrom).User.CharacterId;

        // 1: OK with no name: s_char_rename_input's error (common 0xd) under the input, the box stays, nothing is sent
        var box = await RenameBox("rename1", id);
        if (box == null) return;
        MessageBoxLayer.Instance.Click(0);
        await Frames(60);
        Shot("rename1_empty");
        if (MessageBoxQueue.Current() != box) { Fail("an empty name closed the box"); return; }

        // 2: another character's name: CharacterList 7 code 2, its box, and the input back
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);   // ButtonDelayTimer
        MessageBoxLayer.Instance.Type(renameTaken);
        await Frames(10);
        MessageBoxLayer.Instance.Click(0);
        string taken = Ms2.Ui.InvalidNameRename.AnswerText(2, renameTaken, MessageBoxData.Common);
        if (!await Until(() => MessageBoxQueue.Current() is { } b && b != box, 1800)) { Fail("no answer box for the taken name"); return; }
        await Frames(60);
        string msg = MessageBoxQueue.Current()?.Msg;
        GD.Print($"front end: rename2: box '{msg}'");
        Shot("rename2_taken");
        if (msg != taken) Fail($"the taken name's box says '{msg}', expected '{taken}'");
        MessageBoxLayer.Instance.Click(0);
        if (!await Until(() => MessageBoxQueue.IsIdle, 300)) { Fail("the answer box's OK did not close it"); return; }
        if (Session.List().All(c => c.User.Name != renameFrom)) { Fail("the refused name changed the list"); return; }

        // 3: a new name: the list comes again with it, its flag off, and Start enters the game
        if (!await Select(renameFrom)) return;
        box = await RenameBox("rename3", id);
        if (box == null) return;
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        MessageBoxLayer.Instance.Type(renameTo);
        await Frames(10);
        MessageBoxLayer.Instance.Click(0);
        if (!await Until(() => Session.List().Any(c => c.User.Name == renameTo), 1800)) { Fail($"'{renameTo}' never reached the list ({string.Join(", ", Session.List().Select(c => c.User.Name))})"); return; }
        if (!await ListUp()) return;
        await Frames(60);
        GD.Print($"front end: rename3: the list has {string.Join(", ", Session.List().Select(c => c.User.Name))}, flag {live.CheckNames}");
        Shot("rename3_list");
        if (!await Select(renameTo)) return;
        Key(Godot.Key.Enter);
        if (!await Until(() => Flow.Current == GameFlow.State.Game, 3600)) { Fail($"Start on '{renameTo}' did not enter the game (flow {Flow.Current}, box '{MessageBoxQueue.Current()?.Msg}')"); return; }
        await Frames(120);
        GD.Print($"front end: rename3: '{renameTo}' entered the game");
        Shot("rename3_game");
    }

    void Done()
    {
        GD.Print($"front end ({mode}): {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
