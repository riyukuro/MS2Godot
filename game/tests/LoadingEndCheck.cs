using System.Collections.Generic;
using Godot;
using Ms2Godot.Flow;
using Ms2Godot.Ui;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Tests;

/// <summary>
/// Check on a display (res://tests/loading_end.tscn; the select scene's loading screen waits for a drawn frame): the offline
/// flow from the login to character select, Start on the selected character, and, while the field loads, the case after
/// `--` (--case resize: the window is resized at several points of the load; --case click: another row of the list is
/// clicked at several points; --case late: the movie's Start `--delay N` frames after a click on another row,
/// while that row's select scene still reloads; --case switch, or --then-switch after another case: then the
/// system menu's character select and a second Start; --case none). Once the game state is reached and the loading screen's fade has had its time,
/// no loading screen may be up or drawn, the HUD scene's field must be the chosen character's, and the scene must not have
/// gone back to character select. --le-shots DIR saves a picture at each step. Quits with 0 when all of that holds, else 1.
/// </summary>
public partial class LoadingEndCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new LoadingEndDriver());
        GameFlow.Start(GetTree());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene changes.</summary>
public partial class LoadingEndDriver : Node
{
    string mode = "none", shotDir;
    bool thenSwitch;
    int failures;
    readonly List<string> log = new();

    void Fail(string what) { failures++; GD.Print("loading end: FAIL " + what); }

    async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async System.Threading.Tasks.Task<bool> Until(System.Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++) { if (cond()) return true; await Frames(1); }
        return cond();
    }
    GameFlow Flow => GetTree().Root.GetNodeOrNull<GameFlow>("GameFlow");
    void Shot(string name)
    {
        if (shotDir == null) return;
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
    }

    /// <summary>Every loading screen in the tree that is up: started and not yet faded (Active).</summary>
    List<string> CoversUp()
    {
        var up = new List<string>();
        void Walk(Node n)
        {
            if (n is LoadingMovie m && m.IsVisibleInTree() && m.Active)
                up.Add($"{m.GetPath()} active={m.Active}");
            for (int i = 0; i < n.GetChildCount(); i++) Walk(n.GetChild(i));
        }
        Walk(GetTree().Root);
        return up;
    }

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        thenSwitch = System.Array.IndexOf(args, "--then-switch") >= 0;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--case") mode = args[i + 1];
            if (args[i] == "--le-shots") shotDir = args[i + 1];
        }
        if (!await Until(() => Flow?.Current == GameFlow.State.Login && GetTree().CurrentScene is LoginScene, 3000)) { Done("no login scene"); return; }
        await Frames(30);
        UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);
        var open = System.Diagnostics.Stopwatch.StartNew();
        if (!await Until(() => GetTree().CurrentScene is CharacterListScene { ListReady: true }, 6000)) { Done("character select never took input"); return; }
        GD.Print($"loading end: character select took input {open.ElapsedMilliseconds} ms after the login, {Time.GetTicksMsec()} ms after the start");
        await Frames(30);
        var list = (CharacterListScene)GetTree().CurrentScene;
        int chosen = list.ListIndex;
        long chosenId = Session.List()[chosen].User.CharacterId;
        int chosenMap = Session.List()[chosen].User.MapA;
        Shot("01_select.png");
        if (mode == "late")
        {
            // a Start that comes while another row's selection is still being handled (its select scene reload under way): the
            // movie's OnGameStart, `--delay` frames after the click on the other row. The client pops the list down and sends the
            // enter only once the preview is in (FUN_1418f13f0 skips HidePopups under m_180; PreviewCharacter -> FUN_1418f1d00),
            // so no reload may overlap the load start.
            int delay = 0;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--delay") int.TryParse(args[i + 1], out delay);
            if (list.RowPoint(other0(chosen)) is { } p0) { Click(p0); log.Add($"row {other0(chosen)} clicked, start {delay} frames later"); }
            await Frames(delay);
            // the movie's Start sends the row it has selected: the clicked one
            chosen = other0(chosen);
            chosenId = Session.List()[chosen].User.CharacterId;
            chosenMap = Session.List()[chosen].User.MapA;
            // the first dialog that can take it (a select scene reloading over its new map has none until its field is in)
            if (!await Until(() => (GetTree().CurrentScene as CharacterListScene)?.SendGameStart(chosenId.ToString()) == true, 3000)) { Done("no dialog took the start"); return; }
        }
        else
        {
        // Enter on the list (vfunc_13 -> onEnterKey -> clickStart -> OnGameStart), as the flow's own script starts
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
        }
        // a click on another row right after Start, in the same frame, as a player clicking on while the game starts
        if (mode == "click" && list.RowPoint(other0(chosen)) is { } first) { Click(first); log.Add($"frame 0: row {other0(chosen)} clicked at {first}"); }
        // the load: the flow's Loading state, the select scene still up for a few frames, then the HUD scene's field build
        int other = chosen == 0 ? 1 : 0;
        var sizes = new[] { new Vector2I(1600, 900), new Vector2I(1024, 640), new Vector2I(1440, 810) };
        int n = 0;
        Node loadingFrom = null;
        for (int f = 0; f < 3000 && Flow.Current != GameFlow.State.Game; f++)
        {
            // no select scene may come into the tree once the load has started (a reload over the load start)
            if (Flow.Current == GameFlow.State.Loading && loadingFrom == null) loadingFrom = GetTree().CurrentScene ?? this;
            if (loadingFrom != null && GetTree().CurrentScene is CharacterListScene again && again != loadingFrom)
            { Fail($"frame {f}: a select scene came in after the load started"); loadingFrom = again; }
            // a window drag is many size changes: one every 5 frames from the first frame after Start to the game state;
            // clicks on another row on every frame the select scene is still current
            if (mode == "resize" && f % 5 == 1)
            {
                var size = sizes[n++ % sizes.Length];
                GetWindow().Size = size;
                log.Add($"frame {f}: resize to {size} ({GetTree().CurrentScene?.Name}, field loaded {Field.LoadComplete})");
            }
            if (mode == "click" && GetTree().CurrentScene is CharacterListScene s && s.RowPoint(other) is { } at)
            {
                if (s.ListIndex != chosen) Fail($"frame {f}: the list moved to row {s.ListIndex} after Start");
                Click(at);
                log.Add($"frame {f}: row {other} clicked at {at}");
            }
            if (f == 20) Shot("02_loading.png");
            await Frames(1);
        }
        foreach (var l in log) GD.Print("loading end: " + l);
        if (Flow.Current != GameFlow.State.Game) { Done("the game state was never reached"); return; }
        // StopProgress after the field's settle frames, then the movie's 500 ms fade answers OnFadeInComplete
        await Until(() => CoversUp().Count == 0, 600);
        await Frames(10);
        Shot("03_game.png");
        if (mode == "resize")
        {
            // and once more with the game up: the faded screen gets the new stage too (MovieDialog.Relayout)
            GetWindow().Size = new Vector2I(1280, 720);
            await Frames(60);
            Shot("04_game_resized.png");
        }
        foreach (var c in CoversUp()) Fail("a loading screen is still up: " + c);
        if (World.FieldContents.CoverUp) Fail("FieldContents.CoverUp after the load");
        if (Ms2.Ui.FieldMoveCover.Up) Fail("the field move cover still holds the input");
        if (GetTree().CurrentScene is not HudScene) Fail($"the scene is {GetTree().CurrentScene?.Name}, not the HUD");
        if (GetTree().GetFirstNodeInGroup(World.FieldContents.Group) is not World.FieldContents fc) Fail("no field in the game scene");
        else if (fc.FieldId != chosenMap) Fail($"the game field is {fc.FieldId}, not the chosen character's {chosenMap}");
        if (Session.Chosen?.User.CharacterId != chosenId) Fail($"the chosen character changed: {Session.Chosen?.User.CharacterId} not {chosenId}");
        if (Flow.Current != GameFlow.State.Game) Fail($"the flow left the game state: {Flow.Current}");
        if (mode == "switch" || thenSwitch) await SwitchCharacter(chosen, chosenId);
        Done(null);
    }

    /// <summary>
    /// --case switch: the system menu's character select (UISystemMenuDialog::OnSelectCharacter) from the game, back to the list
    /// with the same characters and the played one selected, then Start again into the game.
    /// </summary>
    async System.Threading.Tasks.Task SwitchCharacter(int chosen, long chosenId)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
        await Frames(30);
        if (GetTree().CurrentScene is not HudScene hud) { Fail("no HUD for the system menu"); return; }
        // the button clicked through the window's input, so a layer over the menu that takes the mouse would stop it as it
        // stops a hand click
        if (!await Until(() => hud.SystemMenuPoint("m_selectCharacter") != null, 120)) { Fail("the system menu has no character select button"); return; }
        var at = hud.SystemMenuPoint("m_selectCharacter").Value;
        Click(at);
        var back = System.Diagnostics.Stopwatch.StartNew();
        GD.Print($"loading end: character select clicked at {at}");
        if (!await Until(() => GetTree().CurrentScene is CharacterListScene { ListReady: true } && Flow.Current == GameFlow.State.Lobby, 6000))
        { Fail($"character select did not come back (flow {Flow.Current}, scene {GetTree().CurrentScene?.Name})"); return; }
        GD.Print($"loading end: character select took input {back.ElapsedMilliseconds} ms after its button in the game");
        await Frames(30);
        Shot("05_back_at_select.png");
        var list = (CharacterListScene)GetTree().CurrentScene;
        if (list.ListIndex != chosen) Fail($"the list selects row {list.ListIndex}, not the played character's {chosen}");
        if (World.FieldContents.CoverUp) Fail("a loading screen is up over character select");
        if (GetTree().Root.FindChildren("*", nameof(HudScene), true, false).Count > 0) Fail("a HUD is still in the tree");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
        if (!await Until(() => Flow.Current == GameFlow.State.Game, 6000)) { Fail("the second Start did not reach the game"); return; }
        await Until(() => CoversUp().Count == 0, 600);
        await Frames(10);
        Shot("06_second_entry.png");
        foreach (var c in CoversUp()) Fail("a loading screen is still up after the second entry: " + c);
        if (Session.Chosen?.User.CharacterId != chosenId) Fail("the second entry is another character");
    }

    static int other0(int chosen) => chosen == 0 ? 1 : 0;

    /// <summary>A left click at a window point, as the mouse gives it (press and release, through the viewport's input).</summary>
    static void Click(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
    }

    void Done(string fail)
    {
        if (fail != null) Fail(fail);
        GD.Print($"loading end ({mode}): {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
