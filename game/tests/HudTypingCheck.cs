using System.Reflection;
using Godot;
using Ms2.Avm2.Flash;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/hud_typing.tscn): letters and digits that are hotkeys, typed into a movie's focused text
/// field, go into the field and run no HUD key. In the client CKeyTable::FUN_141431fc0 (@0x141431fc0, keytable.cpp:2620)
/// keeps ExecuteActionKey's toggles off and Unk_PCMove::FUN_141683ef0 (@0x141683ef0) keeps the cast and pickup keys off
/// while CInputKeyHandler::FUN_14116d7a0(0xf) holds, and CUIService::IsTextFieldFocused is one of its bits; the keys go
/// to the focused field, as the client's dialogs are clips of one movie with one focus (here the HUD's later views, the
/// screen mask among them, must pass them on). It opens the HUD (res://ui/hud.tscn) offline, opens the game dictionary,
/// clicks into its search field (m_inputSearch) and types
/// m, p, l, i, k and 1 (the world map, my info, the quest log, the inventory, the skill book and the first quick slot) as
/// the window's keys, then holds Up, X and the space there: the own player neither moves nor takes the battle stance, X
/// does not read as a held cast key (Unk_PCMove reads no key while the mask holds) and the field's action key (npc talk,
/// portal, gather, taxi) does not read the space. Then the focus moves as a left press moves
/// main.gfx's one focus (MovieImpl, sfr_gfx_01i.cpp:3673; QueueSetFocusTo, :2672): a press on the world clears it and Up
/// moves the player again; a click on the chat's input focuses it and a and b go there, not to the dictionary; a press on
/// the dictionary's surface (over the chat's view) takes the chat's focus away too. Ctrl + Left leaves the camera alone
/// outside debug mode and turns it in debug mode (CTargetCameraMode::vfunc_9's keys are debug-only). Quits with 0 when all of that holds
/// (the field holds "mplik1x", none of those dialogs opened, the player stayed, no cast key read as held, ...), else 1.
/// </summary>
public partial class HudTypingCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64; the presses need the HUD's real layout
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new HudTypingDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class HudTypingDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Typed = "mplik1";
    static readonly Key[] Keys = { Key.M, Key.P, Key.L, Key.I, Key.K, Key.Key1 };
    HudScene hud;
    AsTextField field;
    int stage, wait;
    readonly ulong startMs = Time.GetTicksMsec();

    string held;
    Vector2 worldPoint, worldMove, chatPoint, dictPoint;
    MovieDialog chatMovie;
    AsTextField chatField;
    ulong movedMs;
    float yaw0;

    static void CameraKeys(bool down)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Ctrl, PhysicalKeycode = Key.Ctrl, Pressed = down });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Left, PhysicalKeycode = Key.Left, Pressed = down });
    }

    static object Get(object o, string name) => o?.GetType().GetField(name, Private)?.GetValue(o);
    Ms2.Avm2.Flash.Player DictionaryPlayer => ((MovieDialog)Get(hud, "gameDictionaryMovie")).Player;
    static Vector2 ScreenOf(MovieDialog m, double x, double y) => ((As3View)Get(m, "view")).GetGlobalTransformWithCanvas() * new Vector2((float)x, (float)y);
    static Vector2 StageOf(MovieDialog m, Vector2 screen) => ((As3View)Get(m, "view")).GetGlobalTransformWithCanvas().AffineInverse() * screen;

    /// <summary>The window's mouse moves there (the views sample the mouse every 50 ms).</summary>
    void Move(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        movedMs = Time.GetTicksMsec();
    }

    bool Settled() => ++wait >= 5 && Time.GetTicksMsec() - movedMs > 150;

    /// <summary>A left click of the window's mouse.</summary>
    static void Press(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false });
    }
    static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) if (Find<T>(c) is { } f) return f;
        return null;
    }
    static bool Shown(object side) => side?.GetType().GetProperty("IsClosed")?.GetValue(side) is false;

    void Finish(bool ok, string what)
    {
        GD.Print("hud typing: " + (ok ? "OK " : "FAIL ") + what);
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, $"timed out at stage {stage}"); return; }
        switch (stage)
        {
            case 0:
                if (GetTree().CurrentScene is not HudScene h || !Field.LoadComplete) return;
                if (++wait < 30) return;
                hud = h; wait = 0;
                hud.GetType().GetMethod("OpenGameDictionary", Private)!.Invoke(hud, null);
                stage = 1;
                return;
            case 1:
            {
                if (Get(hud, "gameDictionaryMovie") is not MovieDialog { Loaded: true } m) { if (++wait > 600) Finish(false, "no game dictionary"); return; }
                if (++wait < 30) return;
                var host = m.Host;
                if (host.Child("m_inputSearch") is not { } input || host.Get(input, "textField") is not AsTextField tf) { Finish(false, "no m_inputSearch"); return; }
                host.Set(input, "text", "");
                var b = tf.GetBounds(null);
                m.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);   // a click into the field, as a player gives it the focus
                field = tf; wait = 0; stage = 2;
                return;
            }
            case 2:
            {
                if (++wait < 10) return;    // CLIK moves the focus to the component's textField a frame on
                var m = (MovieDialog)Get(hud, "gameDictionaryMovie");
                if (!ReferenceEquals(m.Player.Focus, field)) { var fo = m.Player.Focus as AsDisplayObject; Finish(false, $"the field took no focus ({fo?.GetType().Name} {fo?.InstanceName} parent={fo?.Parent?.InstanceName} tfparent={field.Parent?.InstanceName} tfname={field.InstanceName})"); return; }
                wait = 0; stage = 3;
                return;
            }
            default:
            {
                int i = stage - 3;
                if (i < Keys.Length)
                {
                    Input.ParseInputEvent(new InputEventKey { Keycode = Keys[i], PhysicalKeycode = Keys[i], Unicode = Typed[i], Pressed = true });
                    Input.ParseInputEvent(new InputEventKey { Keycode = Keys[i], PhysicalKeycode = Keys[i], Pressed = false });
                    stage++;
                    return;
                }
                if (++wait < 10) return;
                var opened = new System.Collections.Generic.List<string>();
                if (hud.GetType().GetProperty("WorldMapOpen", Private)?.GetValue(hud) is true) opened.Add("world map");
                if (Shown(Get(hud, "myInfo"))) opened.Add("my info");
                if (Shown(Get(hud, "questLogSide"))) opened.Add("quest log");
                if (Shown(Get(hud, "inventory"))) opened.Add("inventory");
                if (Shown(Get(hud, "skillBook"))) opened.Add("skill book");
                if (field.Text != Typed || opened.Count != 0)
                {
                    Finish(false, $"field \"{field.Text}\", opened: {(opened.Count == 0 ? "none" : string.Join(", ", opened))}");
                    return;
                }
                // the own player's keys, held with the field still focused: an arrow (movement) and X (the offline attack)
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, PhysicalKeycode = Key.X, Unicode = 'x', Pressed = true });
                // and the action key (npc talk, portal, gather, taxi): a space typed into the field
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Unicode = ' ', Pressed = true });
                wait = 0; stage = 100;
                return;
            }
            case 100:
            {
                if (++wait < 10) return;
                var move = Movement.MoveInput.Read().Move;
                var me = GetTree().GetFirstNodeInGroup(Player.Group) as Player;
                bool battle = me?.InBattle == true;
                // the charging skill's release reads its cast key as StateSkill does (IsKeyDown(m_nA4)): X (DIK 0x2d) held
                bool castKey = hud.GetType().GetMethod("CastKeyDown", Private)?.Invoke(hud, new object[] { 0x2d }) is true;
                // the held-key poll over the bound keys (MyPC::FUN_141526730 with bAuto) reads the same keys: no key taken
                var pollArgs = new object[] { false };
                object polled = hud.GetType().GetMethod("PollOwnKey", Private)?.Invoke(hud, pollArgs);
                int pollKey = polled is int pk ? pk : -1;
                // the field's action key poll (FieldContents' physics step) took the held space as pressed or not
                var contents = Find<World.FieldContents>(GetTree().Root);
                bool actionKey = Get(contents, "actionHeld") is true;
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = false });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, PhysicalKeycode = Key.X, Pressed = false });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
                bool ok = me != null && move == Vector2.Zero && !battle && !castKey && pollKey == 0 && contents != null && !actionKey && field.Text == Typed + "x ";
                held = $"field \"{field.Text}\", none opened; held Up: move {move}; held X: battle stance {battle}, cast key down {castKey}, poll key {pollKey} (player {(me != null ? "found" : "missing")}); held space: action key {actionKey} (field contents {(contents != null ? "found" : "missing")})";
                if (!ok) { Finish(false, held); return; }
                // a left press on the world: no movie takes it, each one's press on nothing clears its focus
                var vs = GetViewport().GetVisibleRect().Size;
                worldPoint = new Vector2(vs.X * 0.5f, vs.Y * 0.25f);
                Move(worldPoint);
                wait = 0; stage = 110;
                return;
            }
            case 110:
                if (!Settled()) return;
                Press(worldPoint);
                wait = 0; stage = 111;
                return;
            case 111:
            {
                if (++wait < 10) return;
                if (Ms2.Ui.InputBlock.TextFieldFocused() || DictionaryPlayer.Focus != null) { Finish(false, $"{held}; a press on the world at {worldPoint} left the focus ({DictionaryPlayer.Focus})"); return; }
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = true });
                wait = 0; stage = 112;
                return;
            }
            case 112:
            {
                if (++wait < 10) return;
                var move = Movement.MoveInput.Read().Move;
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = false });
                if (move == Vector2.Zero) { Finish(false, $"{held}; after a press on the world held Up: move {move}"); return; }
                worldMove = move;
                // the chat's TextInput, clicked as a player does
                if (Get(hud, "messengers") is not System.Collections.IList { Count: > 0 } ms || ms[0] is not System.Runtime.CompilerServices.ITuple t
                    || t[0] is not MovieDialog { Loaded: true } chat) { Finish(false, $"{held}; no chat window"); return; }
                chatMovie = chat;
                if (chat.Host.Child("m_textInput") is not { } ti || chat.Host.Get(ti, "textField") is not AsTextField ctf) { Finish(false, "no chat m_textInput"); return; }
                chatField = ctf;
                var b = ctf.GetBounds(null);
                chatPoint = ScreenOf(chat, b.X + b.W / 2, b.Y + b.H / 2);
                Move(chatPoint);
                wait = 0; stage = 120;
                return;
            }
            case 120:
                if (!Settled()) return;
                Press(chatPoint);
                wait = 0; stage = 121;
                return;
            case 121:
            {
                if (++wait < 10) return;
                if (!ReferenceEquals(chatMovie.Player.Focus, chatField)) { Finish(false, $"{held}; a click on the chat input at {chatPoint} gave it no focus ({chatMovie.Player.Focus}); input bounds {chatField.GetBounds(null)}"); return; }
                foreach (var key in new[] { Key.A, Key.B })
                {
                    Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = (char)('a' + (key - Key.A)), Pressed = true });
                    Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                }
                wait = 0; stage = 122;
                return;
            }
            case 122:
            {
                if (++wait < 10) return;
                if (chatField.Text != "ab" || field.Text != Typed + "x ") { Finish(false, $"{held}; typed ab with the chat focused: chat \"{chatField.Text}\", dictionary \"{field.Text}\""); return; }
                // a press on the dictionary's own surface (not its field) with the chat's field focused: one stage, one focus
                var m = (MovieDialog)Get(hud, "gameDictionaryMovie");
                var b = field.GetBounds(null);
                (double X, double Y)[] tries = { (b.X - 12, b.Y + b.H / 2), (b.X + b.W / 2, b.Y + b.H + 12), (b.X + b.W / 2, b.Y - 12), (b.X + b.W + 12, b.Y + b.H / 2) };
                bool found = false;
                foreach (var (x, y) in tries)
                {
                    if (!m.Player.HitTestShapes(x, y)) continue;
                    var sp = ScreenOf(m, x, y);
                    if (chatMovie.Player.HitTestShapes(StageOf(chatMovie, sp).X, StageOf(chatMovie, sp).Y)) continue;
                    dictPoint = sp; found = true; break;
                }
                if (!found) { Finish(false, $"{held}; no free point of the dictionary next to its field"); return; }
                Move(dictPoint);
                wait = 0; stage = 130;
                return;
            }
            case 130:
            {
                if (!Settled()) return;
                var view = (As3View)Get(Get(hud, "gameDictionaryMovie"), "view");
                if (!view.MouseOverUi) { Finish(false, $"{held}; the dictionary does not take the mouse at {dictPoint}"); return; }
                Press(dictPoint);
                wait = 0; stage = 131;
                return;
            }
            case 131:
            {
                if (++wait < 10) return;
                bool chatKept = chatMovie.Player.Focus != null, block = Ms2.Ui.InputBlock.TextFieldFocused();
                held += $"; press on the world: focus cleared, held Up: move {worldMove}; click on the chat input: typed \"{chatField.Text}\" there, dictionary \"{field.Text}\"; press on the dictionary at {dictPoint}: chat focus kept {chatKept}, text-field focus {block}";
                if (chatKept || block) { Finish(false, held); return; }
                // Ctrl + Left turns the camera only in debug mode (CTargetCameraMode::vfunc_9 reads them debug-only)
                yaw0 = (float)Get(Field.Active, "camYaw");
                CameraKeys(true);
                wait = 0; stage = 132;
                return;
            }
            case 132:
            case 133:
            {
                if (++wait < 20) return;
                CameraKeys(false);
                float yaw = (float)Get(Field.Active, "camYaw");
                bool debug = stage == 133;
                Ms2.Ui.InputKeyHandler.DebugMode = false;
                if (!debug)
                {
                    if (yaw != yaw0) { Finish(false, $"{held}; Ctrl+Left outside debug mode turned the camera {yaw0} -> {yaw}"); return; }
                    Ms2.Ui.InputKeyHandler.DebugMode = true;
                    CameraKeys(true);
                    wait = 0; stage = 133;
                    return;
                }
                bool turned = yaw > yaw0;
                Finish(turned, $"{held}; Ctrl+Left: no turn outside debug mode, in debug mode {yaw0} -> {yaw}");
                return;
            }
        }
    }
}
