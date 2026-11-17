using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// CLIK TextInput: the skin's "default" / "focused" / "disabled" frames behind a native LineEdit placed on the skin's
/// text field, with the field's font, size and colour. Focus follows the CLIK focused property (gained on click or set by
/// the dialog's tab logic); Enter is left to the dialog (LoginDialog.onEnterKey), as in the client.
/// </summary>
[GlobalClass]
public partial class NTextInput : Control
{
    [Export] public string Movie = "";
    [Export] public string Skin = "";
    [Export] public bool Password;
    [Export] public int MaxChars;
    [Export] public string TextField = "textField";

    [Signal] public delegate void FocusChangedEventHandler(bool focused);

    public LineEdit Edit { get; private set; }
    NativeSkin current;
    Dictionary<string, int> labels = new();
    int frames = 1;
    Vector2 skinSize;
    bool disabled;

    public string Text { get => Edit?.Text ?? ""; set { if (Edit != null) Edit.Text = value ?? ""; } }
    public int Length => Text.Length;
    public bool Focused => Edit?.HasFocus() == true;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        if (NativeUi.Art == null) return;
        (labels, frames) = NativeUi.Art.Timeline(Movie, Skin);
        var first = NativeUi.Art.Bake(Movie, Skin, 0);
        if (first == null) return;
        skinSize = first.Size;
        if (Size == Vector2.Zero) Size = first.Size;
        var tf = first.Texts.FirstOrDefault(t => t.Name == TextField) ?? first.Texts.FirstOrDefault();
        var def = tf?.Def;
        var font = NativeUi.Assets.FontFor((string)def?["fontClass"]);
        int size = (int)Math.Round(UiBuilder.F(def?["size"], 12));
        var color = UiBuilder.ParseColor((string)def?["color"], Colors.White);
        Edit = new LineEdit
        {
            Secret = Password || (bool)(def?["password"] ?? false), MaxLength = MaxChars > 0 ? MaxChars : (int?)def?["maxLength"] ?? 0,
            Flat = true, CaretBlink = true, ContextMenuEnabled = false, SelectAllOnFocus = false,
        };
        var empty = new StyleBoxEmpty();
        foreach (var s in new[] { "normal", "focus", "read_only" }) Edit.AddThemeStyleboxOverride(s, empty);
        Edit.AddThemeFontOverride("font", font);
        Edit.AddThemeFontSizeOverride("font_size", size);
        Edit.AddThemeColorOverride("font_color", color);
        Edit.AddThemeColorOverride("caret_color", color);
        Edit.AddThemeConstantOverride("minimum_character_width", 0);
        var r = tf?.Rect ?? new Rect2(Vector2.Zero, Size);
        float grow = Size.X - skinSize.X;
        Edit.Position = r.Position + new Vector2(0, -0.12f * size);
        Edit.Size = new Vector2(r.Size.X + grow, r.Size.Y);
        AddChild(Edit);
        Edit.FocusEntered += () => { SetState("focused"); EmitSignal(SignalName.FocusChanged, true); };
        Edit.FocusExited += () => { SetState(disabled ? "disabled" : "default"); EmitSignal(SignalName.FocusChanged, false); };
        SetState("default");
    }

    public bool Disabled { get => disabled; set { disabled = value; if (Edit != null) Edit.Editable = !value; SetState(value ? "disabled" : Focused ? "focused" : "default"); } }

    /// <summary>focused = 1: take the keyboard focus (and the caret).</summary>
    public void Focus() { Edit?.GrabFocus(); Edit?.CallDeferred(LineEdit.MethodName.GrabFocus); }

    public void SelectAll() => Edit?.SelectAll();

    void SetState(string s)
    {
        int f = NativeArt.RestFrame(labels, frames, s);
        current = NativeUi.Art?.Bake(Movie, Skin, Math.Max(0, f));
        QueueRedraw();
    }

    public override void _Draw()
    {
        current?.Draw(this, new Rect2(current.Origin, current.Size + (Size - skinSize)), Colors.White);
    }
}
