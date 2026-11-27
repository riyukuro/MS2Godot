using System.Reflection;
using Godot;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/modal_keys.tscn): a modal dialog shown holds the own pc's move, jump, attack and quick slot keys and
/// the dialog toggles (UIModalDialog::vfunc_8 @0x141a55380 -> vfunc_47 @0x141a556d0, uimodaldialog.cpp:34/84; the pc's state flag 0x20
/// and CKeyTable::SetMode(0), uicommon.cpp:1540-1682). It drives real key events through the window's input path
/// (Input.ParseInputEvent), the same one a pressed key takes, and reads what the game does with them. It opens the HUD offline,
/// holds Up as the baseline (the own player walks), then opens the auto-loot options (a UIModalDialog, type 0x4e6c) and presses
/// Up, C (jump), X (attack), Space, 1 (quick slot) and I (inventory): the player must stay, take no stance and no dialog may open.
/// A key pressed and released with the dialog up must not move the player after it closes (no stuck move), and a key pressed with it up
/// works after it closes. Quits with 0 when all of that holds, else 1.
/// </summary>
public partial class ModalKeysCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64; the HUD needs its real layout
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new ModalKeysDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class ModalKeysDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Key[] Held = { Key.Up, Key.C, Key.X, Key.Space, Key.Key1, Key.I };
    HudScene hud;
    Player me;
    Vector3 start;
    int stage, wait;
    readonly ulong startMs = Time.GetTicksMsec();
    string note = "";

    static object Get(object o, string name) => o?.GetType().GetField(name, Private)?.GetValue(o);
    static bool Shown(object side) => side?.GetType().GetProperty("IsClosed")?.GetValue(side) is false;

    static void Key_(Key key, bool pressed, char ch = '\0') =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = ch, Pressed = pressed });

    void Finish(bool ok, string what)
    {
        foreach (var k in Held) Key_(k, false);
        GD.Print("modal keys: " + (ok ? "OK " : "FAIL ") + what);
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    bool ModalUp() => Shown(Get(hud, "itemGetOption"));
    float Moved() => (me.GlobalPosition - start).Length();

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, $"timed out at stage {stage}"); return; }
        switch (stage)
        {
            case 0:                         // the HUD is up: the baseline, Up held walks the player
            {
                if (GetTree().CurrentScene is not HudScene h || !Field.LoadComplete) return;
                if (GetTree().GetFirstNodeInGroup(Player.Group) is not Player p) return;
                if (++wait < 60) return;
                hud = h; me = p; wait = 0;
                start = me.GlobalPosition;
                Key_(Key.Up, true);
                stage = 1;
                return;
            }
            case 1:
            {
                if (++wait < 30) return;
                Key_(Key.Up, false);
                if (Moved() < 0.5f) { Finish(false, $"baseline: held Up moved the player {Moved():F2}, the check has no walk to compare"); return; }
                note = $"baseline Up moved {Moved():F1}";
                wait = 0; stage = 2;
                return;
            }
            case 2:                         // settle, then open the modal
            {
                if (++wait < 30) return;
                hud.GetType().GetMethod("OpenItemGetOption", Private).Invoke(hud, new object[] { 0 });
                wait = 0; stage = 3;
                return;
            }
            case 3:
            {
                if (!ModalUp()) { if (++wait > 300) Finish(false, note + "; the dialog did not open"); return; }
                if (++wait < 30) return;
                // an arrow, jump, attack, the action key, a quick slot and the inventory toggle, all held
                start = me.GlobalPosition;
                foreach (var k in Held) Key_(k, true, k == Key.Space ? ' ' : '\0');
                wait = 0; stage = 4;
                return;
            }
            case 4:
            {
                if (++wait < 40) return;
                var move = Movement.MoveInput.Read();
                bool inv = Shown(Get(hud, "inventory"));
                string held = $"modal up, held Up C X Space 1 I: moved {Moved():F2}, read move {move.Move} jump {move.Jump}, stance {me.InBattle}, inventory {(inv ? "opened" : "closed")}";
                if (Moved() > 0.05f || move.Move != Vector2.Zero || move.Jump || me.InBattle || inv) { Finish(false, note + "; " + held); return; }
                note += "; " + held;
                // Up is released while the dialog is still up: nothing may carry on after the close
                foreach (var k in Held) Key_(k, false);
                wait = 0; stage = 5;
                return;
            }
            case 5:
            {
                if (++wait < 10) return;
                ((dynamic)Get(hud, "itemGetOption")).Close();
                wait = 0; stage = 6;
                return;
            }
            case 6:
            {
                if (++wait < 40) return;
                if (ModalUp()) { Finish(false, note + "; the dialog did not close"); return; }
                // the keys released while the dialog was up: a stuck move would show here
                var move = Movement.MoveInput.Read();
                if (Moved() > 0.05f || move.Move != Vector2.Zero) { Finish(false, note + $"; after the close with Up released: moved {Moved():F2}, read move {move.Move}"); return; }
                // closed: the keys work again
                start = me.GlobalPosition;
                Key_(Key.Up, true);
                Key_(Key.I, true, 'i'); Key_(Key.I, false);
                wait = 0; stage = 7;
                return;
            }
            default:
            {
                if (++wait < 30) return;
                Key_(Key.Up, false);
                bool inv = Shown(Get(hud, "inventory"));
                bool ok = Moved() > 0.5f && inv;
                Finish(ok, note + $"; closed, Up moved {Moved():F1}, I: inventory {(inv ? "opened" : "closed")}");
                return;
            }
        }
    }
}
