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
/// the window's keys, then holds Up and X there: the own player neither moves nor takes the battle stance, and X does
/// not read as a held cast key (Unk_PCMove reads no key while the mask holds). Quits with 0 when the field holds
/// "mplik1x", none of those dialogs opened, the player stayed and no cast key read as held, else 1.
/// </summary>
public partial class HudTypingCheck : Node
{
    public override void _Ready()
    {
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

    static object Get(object o, string name) => o?.GetType().GetField(name, Private)?.GetValue(o);
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
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Up, PhysicalKeycode = Key.Up, Pressed = false });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.X, PhysicalKeycode = Key.X, Pressed = false });
                bool ok = me != null && move == Vector2.Zero && !battle && !castKey && pollKey == 0 && field.Text == Typed + "x";
                Finish(ok, $"field \"{field.Text}\", none opened; held Up: move {move}; held X: battle stance {battle}, cast key down {castKey}, poll key {pollKey} (player {(me != null ? "found" : "missing")})");
                return;
            }
        }
    }
}
