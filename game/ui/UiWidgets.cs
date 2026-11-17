using System.Globalization;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>Raises what a dialog tells the game, the way the movies call ExternalInterface.call(movieId, name, args...). No network here.</summary>
public static class UiBridge
{
    public static event Action<string, string, object[]> ToHost;
    public static void Raise(string movie, string method, params object[] args)
    {
        GD.Print($"UiBridge: {movie}.{method}({string.Join(", ", args.Select(a => a is string s ? "\"" + s + "\"" : a?.ToString()))})");
        ToHost?.Invoke(movie, method, args);
    }
}

/// <summary>CLIK-like behaviour attached to a movie clip by the AS3 base class of its symbol.</summary>
public abstract class Widget
{
    public MovieClipNode Node;
    public bool Enabled = true, Visible = true;
    public event Action<Widget> Click;
    public event Action<Widget, bool> Select;      // flash Event.SELECT: toggled
    protected void RaiseClick() => Click?.Invoke(this);
    protected void RaiseSelect(bool sel) => Select?.Invoke(this, sel);
    public virtual void Init() { }
    public virtual void Set(string prop, object value)
    {
        switch (prop)
        {
            case "enabled": Enabled = (bool)value; Refresh(); break;
            case "visible": Visible = (bool)value; Node.Visible = Visible; break;
        }
    }
    public virtual void Refresh() { }
    public virtual bool HandlesMouse => false;
    public virtual void MouseOver(bool over) { }
    public virtual void MouseDown(bool down) { }
}

public sealed class ButtonWidget : Widget
{
    public bool Toggle, Selected, Hover, Down;
    public bool IsCheckBox;
    public string Label;
    public override bool HandlesMouse => true;

    public override void Init() => Refresh();

    public override void Set(string prop, object value)
    {
        switch (prop)
        {
            case "toggle": if (!IsCheckBox) Toggle = (bool)value; break;
            case "selected": SetSelected((bool)value, false); return;
            case "label": Label = (string)value; Node.SetText(Node.Movie.Assets.Str(Label)); break;
            default: base.Set(prop, value); return;
        }
        Refresh();
    }

    public void SetSelected(bool sel, bool raise)
    {
        if (Selected == sel) return;
        Selected = sel; Refresh();
        if (raise) RaiseSelect(sel);
    }

    public override void MouseOver(bool over) { Hover = over; if (!over) Down = false; Refresh(); }

    public override void MouseDown(bool down)
    {
        bool wasDown = Down;
        Down = down && Enabled; Refresh();
        if (!down && wasDown && Hover && Enabled)
        {
            if (Toggle) SetSelected(!Selected, true);
            RaiseClick();
        }
    }

    public override void Refresh()
    {
        string state = !Enabled ? "disabled" : Down ? "down" : Hover ? "over" : "up";
        string label = Toggle && Selected ? "selected_" + state : state;
        if (!Node.HasLabel(label)) label = Node.HasLabel(state) ? state : "up";
        Node.GotoLabel(label, true);
    }
}

public sealed class LabelWidget : Widget
{
    public override void Set(string prop, object value)
    {
        if (prop == "text") Node.SetText(Node.Movie.Assets.Str((string)value));
        else base.Set(prop, value);
    }
}

/// <summary>Single-line text input: a Godot LineEdit placed on the symbol's text field; frame labels default / focused / disabled.</summary>
public sealed class TextInputWidget : Widget
{
    public LineEdit Edit;
    public string Text { get => Edit.Text; set => Edit.Text = value ?? ""; }
    public int Length => Edit.Text.Length;
    public bool Focused => Edit.HasFocus();
    public void Focus() { Edit.GrabFocus(); }

    public override void Init()
    {
        var fieldChild = Node.Children.FirstOrDefault(c => c.Name == "textField" && c.Node is TextNode);
        var tf = fieldChild?.Node as TextNode;
        var b = tf?.Bounds ?? Node.Bounds;
        // the field's bounds are in its own space: place them through the field's placement in the symbol (messenger_textInput
        // puts textField at x 150, y 8; CLIK TextInput keeps that margin, constraints on textField with Constraints.ALL)
        if (fieldChild != null) b = new Rect2(fieldChild.Base * b.Position, b.Size);
        float asx = (float)Node.sx, asy = (float)Node.sy;
        var font = Node.Scope.Assets.FontFor((string)tf?.Def["fontClass"]);
        int size = Math.Max(1, (int)Math.Round(UiBuilder.F(tf?.Def["size"], 12) * asy));
        var empty = new StyleBoxEmpty();
        Edit = new LineEdit
        {
            Position = b.Position, Size = new Vector2(b.Size.X * asx, b.Size.Y * asy), Scale = new Vector2(1f / asx, 1f / asy),
            Flat = true, CaretBlink = true, ContextMenuEnabled = false, SelectAllOnFocus = false,
        };
        foreach (var s in new[] { "normal", "focus", "read_only" }) Edit.AddThemeStyleboxOverride(s, empty);
        Edit.AddThemeFontOverride("font", font);
        Edit.AddThemeFontSizeOverride("font_size", size);
        var col = UiBuilder.ParseColor((string)tf?.Def["color"], Colors.White);
        Edit.AddThemeColorOverride("font_color", col);
        Edit.AddThemeColorOverride("caret_color", col);
        Edit.AddThemeColorOverride("selection_color", new Color(0.3f, 0.5f, 0.9f, 0.6f));
        Edit.AddThemeConstantOverride("minimum_character_width", 1);
        // a flash text field draws its line from the top 2-unit gutter, which centres a single line in the field; a LineEdit
        // centres its line in its own height, which its font makes taller than the field, so centre that height on the field
        float h = Math.Max(Edit.GetCombinedMinimumSize().Y, b.Size.Y * asy);
        Edit.Size = new Vector2(Edit.Size.X, h);
        Edit.Position = new Vector2(b.Position.X, b.Position.Y + (b.Size.Y - h / asy) / 2);
        Edit.FocusEntered += () => Node.GotoLabel("focused");
        Edit.FocusExited += () => Node.GotoLabel("default");
        if (fieldChild != null) fieldChild.Node.Visible = false;
        Node.AddChild(Edit);
        Node.GotoLabel("default");
        // the text field child must stay hidden when frames change
        if (fieldChild != null) fieldChild.Last = -1;
        foreach (var c in Node.Children) if (c.Name == "textField") c.Last = -1;
    }

    public override void Set(string prop, object value)
    {
        switch (prop)
        {
            case "text": Edit.Text = Node.Movie.Assets.Str((string)value); break;
            case "maxChars": Edit.MaxLength = Convert.ToInt32(value); break;
            case "displayAsPassword": Edit.Secret = (bool)value; break;
            case "editable": Edit.Editable = (bool)value; break;
            case "enabled": Enabled = (bool)value; Edit.Editable = Enabled; Node.GotoLabel(Enabled ? "default" : "disabled"); break;
            default: base.Set(prop, value); break;
        }
    }
}

public static class UiWidgets
{
    public static void Attach(MovieClipNode n)
    {
        Widget w = null;
        if (n.IsA("scaleform.clik.controls.CheckBox")) w = new ButtonWidget { Toggle = true, IsCheckBox = true };
        else if (n.IsA("scaleform.clik.controls.Button")) w = new ButtonWidget();
        else if (n.IsA("scaleform.clik.controls.TextArea")) return;      // multi-line text areas (chat log) show their own text field
        else if (n.IsA("scaleform.clik.controls.TextInput")) w = new TextInputWidget();
        else if (n.IsA("scaleform.clik.controls.Label")) w = new LabelWidget();
        if (w == null) return;
        w.Node = n; n.Widget = w;
        w.Init();
    }

    public static object ParseValue(string expr)
    {
        if (expr == "true") return true;
        if (expr == "false") return false;
        if (expr == "null") return null;
        if (expr.Length >= 2 && expr[0] == '"' && expr[^1] == '"') return expr.Substring(1, expr.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
        if (double.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d == Math.Floor(d) && Math.Abs(d) < int.MaxValue ? (int)d : d;
        return expr;
    }

    /// <summary>Applies component inspector values (instance name, property, AS3 constant) to the widgets found under root.</summary>
    public static void ApplyInspector(MovieClipNode root, IEnumerable<(string Instance, string Property, string Value)> values)
    {
        var byName = new Dictionary<string, MovieClipNode>();
        foreach (var c in root.AllClips())
            foreach (var ch in c.Children)
                if (ch.Name != null && ch.Node is MovieClipNode mc) byName.TryAdd(ch.Name, mc);
        foreach (var (inst, prop, val) in values)
            if (byName.TryGetValue(inst, out var mc) && mc.Widget != null) mc.Widget.Set(prop, ParseValue(val));
    }

    /// <summary>Mouse routing for button-like widgets: hover, press, release (the topmost visible widget under the cursor).</summary>
    public sealed class MouseRouter
    {
        readonly MovieClipNode root;
        ButtonWidget hover, pressed;
        public MouseRouter(MovieClipNode root) { this.root = root; }

        ButtonWidget Pick(Vector2 p)
        {
            ButtonWidget hit = null;
            foreach (var c in root.AllClips())
                if (c.Widget is ButtonWidget b && b.Enabled && c.IsVisibleInTree() && c.GlobalBounds().HasPoint(p)) hit = b;   // later = on top
            return hit;
        }

        public void Handle(InputEvent e)
        {
            if (e is InputEventMouseMotion mm)
            {
                var h = Pick(mm.Position);
                if (h != hover) { hover?.MouseOver(false); hover = h; hover?.MouseOver(true); }
            }
            else if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { pressed = Pick(mb.Position); pressed?.MouseDown(true); }
                else { var p = pressed; pressed = null; p?.MouseDown(false); }
            }
        }
    }
}
