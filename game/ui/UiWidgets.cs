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

public class ButtonWidget : Widget
{
    public bool Toggle, Selected, Hover, Down;
    /// <summary>CLIK Button.allowDeselect: a selected toggle is deselected by a click (Button.handleClick).</summary>
    public bool AllowDeselect = true;
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
            case "label": Label = (string)value; SetLabel(); break;
            default: base.Set(prop, value); return;
        }
        Refresh();
    }

    /// <summary>
    /// Button.updateText: textField.text = label. A "$" key is translated by the movie's translator, FontTranslator::Translate
    /// (fontconfig.cpp), which hands the string back as HTML (SetResultHtml), so a translated label shows its markup's formats.
    /// </summary>
    void SetLabel()
    {
        var text = Node.Movie.Assets.Str(Label);
        if (Label != null && Label.StartsWith('$') && text != Label) Node.SetHtmlText(text); else Node.SetText(text);
    }

    public virtual void SetSelected(bool sel, bool raise)
    {
        if (Selected == sel) return;
        Selected = sel; Refresh();
        if (raise) RaiseSelect(sel);
    }

    public override void MouseOver(bool over) { Hover = over; if (!over) Down = false; Refresh(); }

    /// <summary>ButtonEvent.PRESS: the mouse went down on the enabled button (Button.handleMousePress).</summary>
    public event Action<ButtonWidget> Press;
    /// <summary>The ButtonEvent.CLICK with isRepeat set that an auto repeating button sends while it is held.</summary>
    public event Action<ButtonWidget> RepeatClick;
    /// <summary>Button.autoRepeat, repeatDelay and repeatInterval (milliseconds).</summary>
    public bool AutoRepeat;
    public double RepeatDelay = 500, RepeatInterval = 200;
    double repeatLeft = -1;           // _repeatTimer: the milliseconds to its next tick, -1 with no timer
    bool repeatArmed, repeating;      // _autoRepeatEvent set; _isRepeating
    bool repeatBegun;                 // beginRepeat ran: the timer ticks every repeatInterval

    /// <summary>
    /// Button.handleMousePress / handleMouseRelease. A press on the enabled button shows "down", starts the repeat timer
    /// when it auto repeats (repeatDelay, then every repeatInterval: beginRepeat / handleRepeat) and sends PRESS. The release
    /// stops the timer; released over the button, a click selects a toggle and sends CLICK, unless the button repeated.
    /// </summary>
    public override void MouseDown(bool down)
    {
        bool wasDown = Down;
        Down = down && Enabled; Refresh();
        if (down)
        {
            if (!Enabled) return;
            if (AutoRepeat && repeatLeft < 0) { repeatArmed = true; repeatBegun = false; repeatLeft = RepeatDelay; }
            Press?.Invoke(this);
            return;
        }
        repeatArmed = false;
        repeatLeft = -1;
        if (wasDown && Hover && Enabled)
        {
            if (Toggle && (!Selected || AllowDeselect)) SetSelected(!Selected, true);
            if (!repeating) { RaiseClick(); Clicked(); }
        }
        repeating = false;
    }

    /// <summary>
    /// The repeat timer (a flash Timer: one tick at most per frame). Its end after repeatDelay only switches it to
    /// repeatInterval (beginRepeat); each tick after that sends the repeat click while the press lasts (handleRepeat).
    /// </summary>
    public void Tick(double ms)
    {
        if (repeatLeft < 0) return;
        repeatLeft -= ms;
        if (repeatLeft > 0) return;
        repeatLeft = RepeatInterval;
        if (!repeatBegun) { repeatBegun = true; return; }
        if (!repeatArmed) { repeatLeft = -1; return; }
        repeating = true;
        RepeatClick?.Invoke(this);
    }

    /// <summary>After the click event: what the control's own class does on a click.</summary>
    protected virtual void Clicked() { }

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

/// <summary>
/// CLIK DropdownMenu (scaleform.clik.controls.DropdownMenu, the movies' scaleform.ms2.controls.DropDownMenu /
/// AnimationDropDownMenu): a button showing the selected row's label; a click opens the list below it (the
/// <see cref="Dropdown"/> class, a ScrollingList, at x + menuOffset.left, y + height + menuOffset.top, as wide as the button
/// plus the left and right offsets when menuWidth is -1) with <see cref="MenuRowCount"/> rows of the
/// <see cref="ItemRenderer"/> class, menuMargin inside its frame; a row click selects that row (ListEvent.INDEX_CHANGE when the
/// index changes) and closes it, as does a click elsewhere. The button shows its selected_* states while the list is open.
/// Not ported: the AnimationDropDownMenu open animation (dropdownAni) and a list scroll bar (rows past menuRowCount).
/// </summary>
public sealed class DropdownWidget : ButtonWidget
{
    public readonly List<(string Label, object Data)> Items = new();
    public int SelectedIndex = -1, MenuRowCount = 5, MenuMargin = 1;
    public Vector4 MenuOffset;                 // left, top, right, bottom
    public string Dropdown = "export_scrollinglist_black", ItemRenderer = "export_renderer_dropdown";
    /// <summary>ListEvent.INDEX_CHANGE: the new index.</summary>
    public event Action<DropdownWidget, int> IndexChange;
    public MovieClipNode List { get; private set; }
    readonly List<MovieClipNode> rows = new();
    public IEnumerable<MovieClipNode> OpenClips => List == null ? Enumerable.Empty<MovieClipNode>() : rows.Prepend(List);
    public bool IsOpen => List != null;

    public override void Init() { Toggle = true; Refresh(); }

    public override void Set(string prop, object value)
    {
        switch (prop)
        {
            case "menuRowCount": MenuRowCount = Convert.ToInt32(value); break;
            case "menuMargin": MenuMargin = Convert.ToInt32(value); break;
            case "dropdown": Dropdown = (string)value; break;
            case "itemRenderer": ItemRenderer = (string)value; break;
            case "visible": if (!(bool)value) Close(); base.Set(prop, value); break;
            default: base.Set(prop, value); break;
        }
    }

    /// <summary>The button's own toggle state is the list being open (CLIK DropdownMenu.selected).</summary>
    public override void SetSelected(bool sel, bool raise) { Selected = sel; Refresh(); }

    protected override void Clicked() { if (IsOpen) Close(); else Open(); }

    /// <summary>dataProvider and selectedIndex: the label shows the selected row's text (updateLabel).</summary>
    public void SetData(IEnumerable<(string Label, object Data)> items, int selected)
    {
        Close();
        Items.Clear(); Items.AddRange(items);
        SelectedIndex = -1;
        Select(selected, false);
    }

    public void Select(int index, bool raise)
    {
        if (index >= Items.Count) index = Items.Count - 1;
        if (index == SelectedIndex) return;
        SelectedIndex = index;
        Node.SetText(index >= 0 ? Items[index].Label : "");
        if (raise) IndexChange?.Invoke(this, index);
    }

    public void Open()
    {
        if (IsOpen || Items.Count == 0 || Node.GetParent() is not Node2D parent) return;
        var movie = Node.Movie;
        double s = Node.sx;
        var size = Node.Bounds.Size;
        int count = Math.Min(MenuRowCount, Items.Count);
        float w = size.X + MenuOffset.X + MenuOffset.Z;
        var probe = movie.BuildClass(ItemRenderer, s) ?? movie.Assets.Common.BuildClass(ItemRenderer, s);
        if (probe == null) return;
        float rowH = probe.Bounds.Size.Y, rowW = w - MenuMargin * 2;
        probe.Free();
        float h = count * rowH + MenuMargin * 2;
        List = movie.BuildClassSized(Dropdown, s, w, h) ?? movie.Assets.Common.BuildClassSized(Dropdown, s, w, h);
        if (List == null) return;
        List.Position = Node.Position + new Vector2(MenuOffset.X, size.Y + MenuOffset.Y);
        parent.AddChild(List);
        rows.Clear();
        for (int i = 0; i < count; i++)
        {
            var r = movie.BuildClassSized(ItemRenderer, s, rowW, rowH) ?? movie.Assets.Common.BuildClassSized(ItemRenderer, s, rowW, rowH);
            if (r == null) break;
            if (r.Widget == null) UiWidgets.Attach(r);
            r.Position = new Vector2(MenuMargin, MenuMargin + i * rowH);
            r.SetText(Items[i].Label);
            int index = i;
            if (r.Widget is ButtonWidget b)
            {
                b.Toggle = true;
                b.SetSelected(i == SelectedIndex, false);
                b.Click += _ => { Close(); Select(index, true); };
            }
            List.AddChild(r);
            rows.Add(r);
        }
        SetSelected(true, false);
    }

    public void Close()
    {
        if (List == null) return;
        List.QueueFree();
        List = null;
        rows.Clear();
        SetSelected(false, false);
    }
}

public static class UiWidgets
{
    public static void Attach(MovieClipNode n)
    {
        Widget w = null;
        if (n.IsA("scaleform.clik.controls.DropdownMenu") || n.IsA("scaleform.ms2.controls.DropDownMenu")) w = new DropdownWidget();
        else if (n.IsA("scaleform.clik.controls.CheckBox")) w = new ButtonWidget { Toggle = true, IsCheckBox = true };
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

        /// <summary>
        /// Clips built at run time and added below the root outside its placements (renderers a list or bar makes from
        /// data, e.g. the job tabs); they are tested after the placed clips, as they draw on top of their holder.
        /// </summary>
        public Func<IEnumerable<MovieClipNode>> Extra;

        IEnumerable<DropdownWidget> OpenDropdowns() => root.AllClips().Select(c => c.Widget).OfType<DropdownWidget>().Where(d => d.IsOpen);

        ButtonWidget Pick(Vector2 p)
        {
            ButtonWidget hit = null;
            var clips = root.AllClips().Concat(OpenDropdowns().SelectMany(d => d.OpenClips).ToList());
            if (Extra != null) clips = clips.Concat(Extra().SelectMany(x => x.AllClips()));
            foreach (var c in clips)
                if (c.Widget is ButtonWidget b && b.Enabled && c.IsVisibleInTree() && Hits(c, p)) hit = b;   // later = on top
            return hit;
        }

        /// <summary>
        /// Whether a button takes the mouse at a canvas point: what its current frame shows there, masked content only inside
        /// its mask shape (<see cref="MovieClipNode.HitTest"/>); a clip with nothing shown keeps its bounds.
        /// </summary>
        static bool Hits(MovieClipNode c, Vector2 p)
        {
            var xf = c.GetGlobalTransform();
            if (c.ContentRect() == null || Mathf.IsZeroApprox(xf.Determinant())) return c.GlobalBounds().HasPoint(p);
            return c.HitTest(xf.AffineInverse() * p);
        }

        /// <summary>The button the last left press went to (null: none), until the release.</summary>
        public ButtonWidget Pressed => pressed;

        public void Handle(InputEvent e)
        {
            // a button whose clip was dropped (a list or bar built again) no longer takes the mouse
            if (hover != null && !GodotObject.IsInstanceValid(hover.Node) || hover?.Node.IsQueuedForDeletion() == true) hover = null;
            if (pressed != null && !GodotObject.IsInstanceValid(pressed.Node) || pressed?.Node.IsQueuedForDeletion() == true) pressed = null;
            if (e is InputEventMouseMotion mm)
            {
                var h = Pick(mm.Position);
                if (h != hover) { hover?.MouseOver(false); hover = h; hover?.MouseOver(true); }
            }
            else if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    pressed = Pick(mb.Position);
                    // DropdownMenu.handleStageClick: a press outside the button and its list closes the list
                    foreach (var d in OpenDropdowns().ToList())
                        if (pressed != d && !d.OpenClips.Any(c => c.Widget == pressed)) d.Close();
                    pressed?.MouseDown(true);
                }
                else { var p = pressed; pressed = null; p?.MouseDown(false); }
            }
        }
    }
}
