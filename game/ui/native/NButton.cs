using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// CLIK Button (scaleform.clik.controls.Button) as a Godot control. States and their fallbacks are the AS3 _stateMap
/// (release -> over, out -> up, kb_release -> out -> up, ...); a selected button looks for "selected_" + state first.
/// Each state shows the symbol's frames from its label to its stop frame, baked to textures and played at the movie's
/// frame rate (the hover glow fades exactly as the timeline does). The label is the skin's "textField" drawn natively.
/// Toggle buttons flip <see cref="Selected"/> on click and raise <see cref="SelectEventHandler"/> (Event.SELECT).
/// A scale-9 skin resized in the scene stretches nine-patch style; its text field widens with it (CLIK constraints).
/// </summary>
[GlobalClass]
public partial class NButton : Control
{
    [Export] public string Movie = "";
    [Export] public string Skin = "";
    [Export] public string Text = "";
    /// <summary>Keep the text field's authored text when <see cref="Text"/> is empty (no "label" in the inspector).</summary>
    [Export] public bool KeepInitialText;
    [Export] public bool Toggle;
    [Export] public bool Selected { get => selected; set { selected = value; if (IsInsideTree()) SetState(state); } }
    [Export] public bool Disabled { get => disabled; set { disabled = value; if (IsInsideTree()) SetState(value ? "disabled" : "up"); } }
    /// <summary>Instance name of the text field in the skin ("textField" for CLIK buttons).</summary>
    [Export] public string TextField = "textField";
    /// <summary>Children the component's code hides (visible = false), comma separated.</summary>
    [Export] public string Hide = "";

    [Signal] public delegate void ClickEventHandler();
    [Signal] public delegate void SelectEventHandler(bool selected);
    [Signal] public delegate void StateChangedEventHandler(string state);

    static readonly Dictionary<string, string[]> StateMap = new()
    {
        ["up"] = new[] { "up" }, ["over"] = new[] { "over" }, ["down"] = new[] { "down" }, ["release"] = new[] { "release", "over" },
        ["out"] = new[] { "out", "up" }, ["disabled"] = new[] { "disabled" }, ["selecting"] = new[] { "selecting", "over" },
        ["toggle"] = new[] { "toggle", "up" }, ["kb_selecting"] = new[] { "kb_selecting", "up" },
        ["kb_release"] = new[] { "kb_release", "out", "up" }, ["kb_down"] = new[] { "kb_down", "down" },
    };

    bool selected, disabled, pressed, hover;
    string state = "up";
    Dictionary<string, int> labels = new();
    int totalFrames = 1;
    double fps = 30;
    int frame, endFrame;
    double clock;
    NativeSkin current;
    Label label;
    public Label LabelNode => label;
    public string State => state;
    public NativeSkin CurrentSkin => current;
    /// <summary>The symbol's own size; the control's size may differ (nine-patch).</summary>
    public Vector2 SkinSize { get; private set; }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Stop;
        if (NativeUi.Art == null) return;
        (labels, totalFrames) = NativeUi.Art.Timeline(Movie, Skin);
        fps = NativeUi.Art.Fps(Movie, Skin);
        var first = NativeUi.Art.Bake(Movie, Skin, 0, false, 1, Hide);
        if (first != null)
        {
            SkinSize = first.Size; origin0 = first.Origin;
            if (Size == Vector2.Zero) Size = first.Size;
        }
        MouseEntered += () => { hover = true; if (!disabled) SetState(pressed ? "down" : "over"); };
        MouseExited += () => { hover = false; if (!disabled) SetState(pressed ? "release" : "out"); };
        SetState(disabled ? "disabled" : "up");
    }

    public override void _GuiInput(InputEvent e)
    {
        if (disabled || e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        if (mb.Pressed) { pressed = true; SetState("down"); AcceptEvent(); return; }
        if (!pressed) return;
        pressed = false;
        AcceptEvent();
        if (!hover) { SetState("out"); return; }
        SetState("release");
        HandleClick();
    }

    /// <summary>handleClick: a toggle flips its selection (Event.SELECT), then MouseEvent.CLICK.</summary>
    public void HandleClick()
    {
        if (Toggle) { selected = !selected; SetState(hover ? "over" : "up"); EmitSignal(SignalName.Select, selected); }
        EmitSignal(SignalName.Click);
    }

    /// <summary>Button.setState: the first label of the state's map found under the selected/default prefixes.</summary>
    public void SetState(string s)
    {
        state = s;
        if (!StateMap.TryGetValue(s, out var names)) names = new[] { s };
        var prefixes = selected ? new[] { "selected_", "" } : new[] { "" };
        foreach (var p in prefixes)
            foreach (var n in names)
                if (labels.TryGetValue(p + n, out int start))
                {
                    frame = start; endFrame = NativeArt.RestFrame(labels, totalFrames, p + n); clock = 0;
                    ShowFrame(frame);
                    EmitSignal(SignalName.StateChanged, s);
                    return;
                }
        if (labels.Count == 0) ShowFrame(0);
    }

    public override void _Process(double delta)
    {
        if (frame >= endFrame) return;
        clock += Ms2Godot.GameClock.Real(delta);
        int f = Math.Min(endFrame, frame + (int)(clock * fps));
        if (f == frame) return;
        clock -= (f - frame) / fps;
        frame = f;
        ShowFrame(f);
    }

    void ShowFrame(int f)
    {
        current = NativeUi.Art?.Bake(Movie, Skin, f, false, 1, Hide);
        if (current == null) return;
        var tf = current.Texts.FirstOrDefault(t => t.Name == TextField);
        if (tf != null)
        {
            if (label == null) { label = NativeUi.MakeLabel(tf.Def, tf.Filters); AddChild(label); label.VerticalAlignment = VerticalAlignment.Center; }
            float size = UiBuilder.F(tf.Def?["size"], 12);
            float grow = Size.X - SkinSize.X;
            label.Position = tf.Rect.Position + new Vector2(0, -0.12f * size);
            label.Size = tf.Rect.Size + new Vector2(Math.Max(0, grow), 0);
            label.Text = Text == "" && KeepInitialText ? NativeUi.Str((string)tf.Def?["initial"] ?? "") : NativeUi.Str(Text);
        }
        else if (label != null) label.Text = "";
        QueueRedraw();
    }

    /// <summary>The node's origin is the symbol's registration point; the frame 0 bounds (resized) are the hit area.</summary>
    /// <summary>The hit area (a component's hitArea); by default the skin's bounds at the node's size.</summary>
    public Rect2? HitArea;
    public Rect2 HitRect => HitArea ?? new(origin0, Size);
    Vector2 origin0;

    public override bool _HasPoint(Vector2 p) => HitRect.HasPoint(p);

    public override void _Draw()
    {
        if (current == null) return;
        current.Draw(this, new Rect2(current.Origin, current.Size + (Size - SkinSize)), Colors.White);
    }
}
