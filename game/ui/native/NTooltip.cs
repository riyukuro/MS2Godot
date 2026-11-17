using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// TooltipLabel (export_common_tooltip_tooltipText): a nine-patch panel sized to its text (text + 8, + margin 10, at
/// least 50 wide) and placed by the owner's tooltipData: align topCenter above the owner, shown after onDelay ms and
/// hidden after offDelay ms, kept on the stage. <see cref="Attach"/> wires it to any control.
/// </summary>
public partial class NTooltip : Control
{
    const string Movie = "component_v1", Symbol = "export_common_tooltip_tooltipText";
    NativeSkin panel;
    Label label;
    double shownAt;
    float offDelay;
    static NTooltip shared;

    public static void Attach(Control owner, Func<string> text, int onDelay = 300, int offDelay = 5000, Vector2 offset = default)
    {
        int pending = 0;                                       // bumps on every roll-over / roll-out; a stale timer does nothing
        owner.MouseEntered += () =>
        {
            int mine = ++pending;
            owner.GetTree().CreateTimer(onDelay / 1000.0).Timeout += () => { if (mine == pending && owner.IsVisibleInTree()) Show(owner, text(), offDelay, offset); };
        };
        owner.MouseExited += () => { pending++; shared?.Hide(); };
    }

    /// <summary>Shows a tooltip for <paramref name="owner"/> at once (no roll-over delay).</summary>
    public static void ShowNow(Control owner, string text, int offDelay = 5000) => Show(owner, text, offDelay, Vector2.Zero);

    static void Show(Control owner, string text, int offDelay, Vector2 offset)
    {
        if (string.IsNullOrEmpty(text) || !owner.IsInsideTree()) return;
        if (shared == null || !IsInstanceValid(shared))
        {
            var layer = new CanvasLayer { Layer = 100 };
            owner.GetTree().Root.AddChild(layer);
            shared = new NTooltip { MouseFilter = MouseFilterEnum.Ignore };
            layer.AddChild(shared);
        }
        shared.Set(text, offDelay);
        // Align.getTooltipAlignByRectangle, topCenter: centred above the owner's rect; clamped to the stage
        var rect = owner.GetGlobalRect();
        if (owner is NButton b) rect = new Rect2(owner.GlobalPosition + b.HitRect.Position, b.HitRect.Size);
        var stage = shared.GetViewportRect().Size;
        var p = new Vector2(rect.Position.X + (rect.Size.X - shared.Size.X) / 2, rect.Position.Y - shared.Size.Y) + offset;
        shared.Position = new Vector2(Math.Clamp(p.X, 0, stage.X - shared.Size.X), Math.Clamp(p.Y, 0, stage.Y - shared.Size.Y));
        shared.Visible = true;
    }

    void Set(string text, float off)
    {
        if (panel == null && NativeUi.Art != null)
        {
            panel = NativeUi.Art.Bake(Movie, Symbol + "/m_panel");
            var skin = NativeUi.Art.Bake(Movie, Symbol);
            var tf = skin?.Texts.FirstOrDefault(t => t.Name == "m_textField");
            label = tf != null ? NativeUi.MakeLabel(tf.Def, tf.Filters) : new Label();
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            AddChild(label);
        }
        label.Text = NativeUi.Str(text);
        var ts = label.GetMinimumSize();
        float tw = Mathf.Ceil(ts.X) + 8, th = Mathf.Ceil(ts.Y) + 8;
        Size = new Vector2(Math.Max(50, tw + 10), th + 10);
        label.Position = new Vector2((Size.X - tw) / 2 + 4, (Size.Y - th) / 2 + 4);
        offDelay = off; shownAt = Time.GetTicksMsec();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (Visible && offDelay > 0 && Time.GetTicksMsec() - shownAt > offDelay) Visible = false;
    }

    public override void _Draw() => panel?.Draw(this, new Rect2(Vector2.Zero, Size), Colors.White);
}
