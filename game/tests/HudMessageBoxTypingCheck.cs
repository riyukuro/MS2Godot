using System.Reflection;
using Godot;
using Ms2.Avm2.Flash;
using Ms2.Ui.Hud;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/hud_message_box_typing.tscn), GitHub issue 13: letters and digits that are hotkeys, typed into
/// the message box's input field (the guild-create name prompt is one), go into the field and run no HUD key. In the client one
/// movie, main.gfx, holds every dialog and the message box (UIMessageBox, a UIHeadUpDisplay, uimessagebox.cpp:371), so
/// CUIService::IsTextFieldFocused (@0x141adbb00, uiservice.cpp:1364 -> GFxManager::IsTextFieldFocused(2) @0x1422af2d0) is true
/// for the box's focused field and CInputKeyHandler::FUN_14116d7a0(0xf) holds the action keys and the own player's keys. It
/// opens the HUD offline, shows a name box (type 5, SetInput(0, "", 16, 0, 0), the guild create and search prompts), clicks into its field and types i, m, k, p, l and
/// 1 (inventory, world map, skill book, my info, quest log, the first quick slot): the field holds them, none opened. Then Escape
/// closes the box (onEscapeKey) and an i opens the inventory again, as soon as the focus is gone. The own player's move keys are
/// held the whole time (Left, Up and the jump key C, Unk_PCMove's key reads, MoveInput.Read): while the field has the focus the read
/// gives no move and no jump and OwnKeysBlocked holds (it is the gate of the quick slot keys too), and once the box has closed the
/// same held keys read as a move again. Quits with 0 when all of that holds, else 1.
/// </summary>
public partial class HudMessageBoxTypingCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64; the press needs the real layout
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new HudMessageBoxTypingDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The box's listener, as the guild dialog is: any answer closes the box (MessageBoxQueue.Next).</summary>
sealed class BoxListener : IMessageBoxListener
{
    public bool OnMessageBox(MessageBoxController sender, int button, string tag) { MessageBoxQueue.Next(); return true; }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class HudMessageBoxTypingDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Typed = "imkpl1";
    static readonly Key[] Keys = { Key.I, Key.M, Key.K, Key.P, Key.L, Key.Key1 };
    /// <summary>The own player's move keys held down while the field is typed into: left, up and the jump (MoveInput.ReadKeys).</summary>
    static readonly Key[] Held = { Key.Left, Key.Up, Key.C };
    HudScene hud;
    AsTextField field;
    int stage, wait;
    readonly ulong startMs = Time.GetTicksMsec();
    string note;

    static object Get(object o, string name) => o?.GetType().GetField(name, Private)?.GetValue(o);
    static bool Shown(object side) => side?.GetType().GetProperty("IsClosed")?.GetValue(side) is false;

    static void Press(Key key, char ch = '\0')
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = ch, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    List<string> Opened()
    {
        var opened = new List<string>();
        if (hud.GetType().GetProperty("WorldMapOpen", Private)?.GetValue(hud) is true) opened.Add("world map");
        if (Shown(Get(hud, "myInfo"))) opened.Add("my info");
        if (Shown(Get(hud, "questLogSide"))) opened.Add("quest log");
        if (Shown(Get(hud, "inventory"))) opened.Add("inventory");
        if (Shown(Get(hud, "skillBook"))) opened.Add("skill book");
        return opened;
    }

    void Finish(bool ok, string what)
    {
        GD.Print("hud message box typing: " + (ok ? "OK " : "FAIL ") + what);
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, $"timed out at stage {stage}"); return; }
        var layer = MessageBoxLayer.Instance;
        switch (stage)
        {
            case 0:
            {
                if (GetTree().CurrentScene is not HudScene h || !Field.LoadComplete) return;
                if (++wait < 30) return;
                hud = h; wait = 0;
                var box = MessageBoxQueue.Create(5, "name", new BoxListener(), "t");   // the guild create and search prompts: type 5 with SetInput(0, "", max, 0, 0)
                box.SetInput(0, "", 16, 0, 0);
                MessageBoxQueue.Show(box);
                stage = 1;
                return;
            }
            case 1:     // the box's input field, pressed with the window's mouse
            {
                if (layer?.Shown != true || MessageBoxLayer.LayerMovie is not { } m) { if (++wait > 600) Finish(false, "no name box"); return; }
                if (++wait < 30) return;
                var h = m.Host;
                if (h.Get(layer.Controller.MovieBox(), "m_textInput") is not { } input || h.Get(input, "textField") is not AsTextField tf) { Finish(false, "no m_textInput"); return; }
                field = tf;
                var b = tf.GetBounds(null);
                var at = ((As3View)Get(m, "view")).GetGlobalTransformWithCanvas() * new Vector2((float)(b.X + b.W / 2), (float)(b.Y + b.H / 2));
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false });
                wait = 0; stage = 2;
                return;
            }
            case 2:
            {
                if (++wait < 10) return;    // CLIK moves the focus to the component's textField a frame on
                var fo = MessageBoxLayer.LayerMovie.Player.Focus as AsDisplayObject;
                if (!ReferenceEquals(fo, field)) { Finish(false, $"the field took no focus ({fo?.GetType().Name} {fo?.InstanceName})"); return; }
                if (!Ms2.Ui.InputBlock.TextFieldFocused()) { Finish(false, "the field has the focus but InputBlock.TextFieldFocused is false"); return; }
                foreach (var k in Held) Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
                for (int i = 0; i < Keys.Length; i++) Press(Keys[i], Typed[i]);
                wait = 0; stage = 3;
                return;
            }
            case 3:
            {
                if (++wait < 20) return;
                var opened = Opened();
                note = $"field \"{field.Text}\", opened: {(opened.Count == 0 ? "none" : string.Join(", ", opened))}";
                if (field.Text != Typed || opened.Count != 0) { Finish(false, note); return; }
                var read = Ms2Godot.Movement.MoveInput.Read();
                note += $", move keys held: move ({read.Move.X}, {read.Move.Y}) jump {read.Jump}, own keys blocked {Ms2.Ui.InputBlock.OwnKeysBlocked}";
                if (read.Move != Vector2.Zero || read.Jump || !Ms2.Ui.InputBlock.OwnKeysBlocked) { Finish(false, note + "; the held move keys reached the player"); return; }
                Press(Key.Escape);          // onEscapeKey: the box closes
                wait = 0; stage = 4;
                return;
            }
            case 4:
            {
                if (++wait < 20) return;
                if (layer?.Shown == true) { Finish(false, note + "; Escape left the box open"); return; }
                if (Ms2.Ui.InputBlock.TextFieldFocused()) { Finish(false, note + "; the focus stayed after the box closed"); return; }
                var after = Ms2Godot.Movement.MoveInput.Read();     // the same keys, still held, now move
                note += $"; after the close: move ({after.Move.X}, {after.Move.Y}) jump {after.Jump}";
                if (after.Move != new Vector2(-1, 1) || !after.Jump) { Finish(false, note + "; the held keys read nothing once the box is gone"); return; }
                foreach (var k in Held) Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
                Press(Key.I, 'i');          // no field has the focus: the window key acts again
                wait = 0; stage = 5;
                return;
            }
            default:
            {
                if (++wait < 20) return;
                bool inv = Shown(Get(hud, "inventory"));
                Finish(inv, note + $"; Escape closed the box; I after it: inventory {(inv ? "opened" : "did not open")}");
                return;
            }
        }
    }
}
