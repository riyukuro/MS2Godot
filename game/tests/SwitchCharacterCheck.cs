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
/// comes (OnGameToLogin's FUN_141bbc580(0, 2)), and the list takes input with the move's cover down.
/// --other: each round starts the next row's character, not the one before (the account needs two). --create NAME: after the
/// first return the create page makes NAME; --seed NAME makes NAME first on an account with no character (its job page's first job, the look page's defaults) and that round starts it.
/// Every round checks the own character is the one started: the own FieldAddUser's id and name in the session, the name
/// tag's name and the character whose items the player's look was built with; the run script also fails on any field load
/// error or packet exception in the log. Quits with 0 when every round got back to the list and into the game as the
/// started character, else 1.
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
    bool other;
    string create, seed;

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

    async System.Threading.Tasks.Task<bool> ListReady(int round) =>
        await Until(() => GetTree().CurrentScene is CharacterListScene { ListReady: true } && Ms2.Ui.Hud.MessageBoxQueue.IsIdle, 3600)
        || Done($"round {round}: character select never took input");

    /// <summary>Starts row <paramref name="row"/>'s character (-1: the selected one) and checks the game shows that character.</summary>
    async System.Threading.Tasks.Task<bool> IntoGame(int round, int row = -1)
    {
        if (!await ListReady(round)) return false;
        if (row >= 0 && GetTree().CurrentScene is CharacterListScene list && list.ListIndex != row)
        {
            list.SelectRow(row);   // another job's lobby map reloads the scene
            await Frames(10);
            if (!await ListReady(round)) return false;
        }
        await Frames(30);
        var listed = Session.List();
        int at = (GetTree().CurrentScene as CharacterListScene)?.ListIndex ?? -1;
        var target = at >= 0 && at < listed.Count ? listed[at].User : null;
        if (target == null) return Done($"round {round}: no character on row {at}");
        GD.Print($"switch character: round {round} starts row {at}, '{target.Name}' ({target.CharacterId})");
        Key(Godot.Key.Enter);
        if (!await Until(() => Flow.Current == GameFlow.State.Game, 3600)) return Done($"round {round}: the game state was never reached");
        var live = Net.LiveSession.Instance;
        bool own = await Until(() => live.Me.User?.CharacterId == target.CharacterId && live.OwnPlayer != null, 1800);
        await Frames(60);
        var tag = GetTree().Root.FindChild("PlayerNameTag", true, false) as PlayerNameTag;
        GD.Print($"switch character: round {round} in the game: session character {live.CharacterId}, own FieldAddUser '{live.Me.User?.Name}' ({live.Me.User?.CharacterId}), " +
                 $"name tag '{tag?.Shown}', look of {Look.PlayerLook.LiveLookOf}");
        if (!own) return Done($"round {round}: the own FieldAddUser of '{target.Name}' never bound the player");
        if (live.CharacterId != target.CharacterId) return Done($"round {round}: the session entered {live.CharacterId}, not {target.CharacterId}");
        if (tag?.Shown != target.Name) return Done($"round {round}: the name tag shows '{tag?.Shown}', not '{target.Name}'");
        if (Look.PlayerLook.LiveLookOf != target.CharacterId) return Done($"round {round}: the player's look is of {Look.PlayerLook.LiveLookOf}, not {target.CharacterId}");
        Shot($"round{round}_{target.Name}.png");
        await Frames(120);
        return true;
    }

    void Shot(string name)
    {
        var dir = System.Environment.GetEnvironmentVariable("MS2_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name));
        GD.Print($"switch character: shot {name}");
    }

    /// <summary>The list's Create (OnCreatePC), the job page's Create for its first job, then the look page's Create with
    /// <paramref name="name"/> in the name box; back at the list with the new character on its row.</summary>
    async System.Threading.Tasks.Task<int> CreateCharacter(int round, string name)
    {
        if (!await ListReady(round)) return -1;
        UiBridge.Raise(Ui.Contracts.UICharacterListDialogContract.Movie, Ui.Contracts.UICharacterListDialogContract.ToHost.OnCreatePC);
        if (!await Until(() => Flow.Current == GameFlow.State.Create, 1800)) { Done($"round {round}: the create page never opened"); return -1; }
        await Frames(180);
        Ui.Create.CharacterCreateScene.PickJobScripted();
        if (!await Until(() => Ui.Create.CharacterCreateScene.OnCreatePage, 1800)) { Done($"round {round}: the look page never opened"); return -1; }
        await Frames(120);
        Ui.Create.CharacterCreateScene.ConfirmScripted(name);
        if (!await Until(() => Flow.Current == GameFlow.State.Lobby && Session.List().Exists(c => c.User.Name == name), 1800)) { Done($"round {round}: '{name}' was not created"); return -1; }
        int row = Session.List().FindIndex(c => c.User.Name == name);
        GD.Print($"switch character: round {round} created '{name}' on row {row}");
        return row;
    }

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--rounds" && i + 1 < args.Length) int.TryParse(args[i + 1], out rounds);
            if (args[i] == "--other") other = true;
            if (args[i] == "--create" && i + 1 < args.Length) create = args[i + 1];
            if (args[i] == "--seed" && i + 1 < args.Length) seed = args[i + 1];
        }
        var acct = (System.Environment.GetEnvironmentVariable("MS2_RETRY_ACCOUNT") ?? "").Split(':', 2);
        if (!await Until(() => Flow?.Current == GameFlow.State.Login && GetTree().CurrentScene is LoginScene s && s.Controller.Host?.Entity != null, 3000)) { Done("no login dialog"); return; }
        await Frames(60);
        var c = ((LoginScene)GetTree().CurrentScene).Controller;
        foreach (var (field, text) in new[] { ("m_inputID", acct[0]), ("m_inputPW", acct.Length > 1 ? acct[1] : "") })
            if (c.Host.Get(c.Host.Entity, field) is { } input) c.Host.Set(input, "text", text);
        await Frames(5);
        Key(Godot.Key.Enter);
        if (!await Until(() => Flow.Current == GameFlow.State.Lobby, 1800)) { Done("the login did not reach character select"); return; }
        if (seed != null && Session.List().Count == 0 && await CreateCharacter(0, seed) < 0) return;
        if (!await IntoGame(0, other ? 0 : -1)) return;
        int lastRow = other ? 0 : -1;
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
            int row = -1;
            if (r == 1 && create != null) { row = await CreateCharacter(r, create); if (row < 0) return; }
            else if (other)
            {
                // the return empties the list until the login server's list comes (CharacterListReturned)
                if (!await Until(() => Session.List().Count > 0, 3600)) { Done($"round {r}: the character list never came back"); return; }
                if (!await ListReady(r)) return;
                row = (lastRow + 1) % Session.List().Count;
            }
            if (row >= 0) lastRow = row;
            if (!await IntoGame(r, row)) return;
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
