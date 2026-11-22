using System.Reflection;
using Godot;
using Ms2.Avm2.Flash;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/create_name_typing.tscn): the character name box of the create page takes typed keys. It
/// opens character creation offline, picks the job, opens the name box as the page's Create does
/// (UICharacterCreateDialog::OnCreate @0x1418e6210: FUN_140d73e40(10, 1, 0, ""), SetInput(2, "", 12, 0, 0)), clicks
/// the box's name field with the window's mouse (the message box layer hands the press to the box's movie) and types
/// letters and digits there as the window's keys. Quits with 0 when the field holds them and the box's m_textInput holds them (GetInputText, what the create callback reads), else 1.
/// </summary>
public partial class CreateNameTypingCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64; the press needs the real layout
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new CreateNameTypingDriver());
        Ui.Create.CharacterCreateScene.Enter(GetTree());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the create scene's reloads.</summary>
public partial class CreateNameTypingDriver : Node
{
    const string Typed = "abc12";
    int stage, wait;
    readonly ulong startMs = Time.GetTicksMsec();
    AsTextField field;
    string pressNote;

    static object Get(object o, string name) => o?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(o);

    static void Press(Key key, char ch)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = ch, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    void Finish(bool ok, string what)
    {
        GD.Print("create name typing: " + what + (ok ? " -> ok" : " -> FAIL"));
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, $"timed out at stage {stage}"); return; }
        var layer = MessageBoxLayer.Instance;
        switch (stage)
        {
            case 0:     // the job page
                if (++wait < 60) return;
                Ui.Create.CharacterCreateScene.PickJobScripted();
                wait = 0; stage = 1;
                return;
            case 1:     // the look page, then its Create
                if (!Ui.Create.CharacterCreateScene.OnCreatePage) { if (++wait > 3000) Finish(false, "no create page"); return; }
                if (++wait < 60) return;
                Ui.Create.CharacterCreateScene.OpenNameBoxScripted();
                wait = 0; stage = 2;
                return;
            case 2:     // the box's name field, pressed with the window's mouse
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
                wait = 0; stage = 3;
                return;
            }
            case 3:
            {
                if (++wait < 10) return;    // CLIK moves the focus to the component's textField a frame on
                var m = MessageBoxLayer.LayerMovie;
                var fo = m.Player.Focus as AsDisplayObject;
                pressNote = ReferenceEquals(fo, field) ? "focused" : $"focus {fo?.GetType().Name} {fo?.InstanceName}";
                foreach (var ch in Typed) Press(char.IsAsciiLetter(ch) ? (Key)char.ToUpperInvariant(ch) : (Key)ch, ch);
                wait = 0; stage = 4;
                return;
            }
            default:
            {
                if (++wait < 90) return;    // held, so a window run shows the typed name before the quit
                string got = field.text, input = Ms2.Avm2.Rt.ToString(MessageBoxLayer.LayerMovie.Host.Get(layer.Controller.MovieBox(), "m_textInput") is { } ti ? MessageBoxLayer.LayerMovie.Host.Get(ti, "text") : null);
                Finish(got == Typed && input == Typed, $"press on the name field: {pressNote}, typed \"{got}\", box input \"{input}\", restrict {(field.restrict == null ? "null" : "\"" + field.restrict + "\"")} (want \"{Typed}\")");
                return;
            }
        }
    }
}
