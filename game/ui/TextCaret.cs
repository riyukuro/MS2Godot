using System.Linq;
using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;

namespace Ms2Godot.Ui;

/// <summary>
/// The blinking text cursor of the focused input field, as Scaleform's EditorKit draws it (SF 4.1.8,
/// src/ms2/scaleform/gfx/sfr_gfx_07.cpp). It is drawn in the field's holder, in field units, so it takes the field's
/// matrix, visibility and colour transform as the field's text does.
/// <list type="bullet">
/// <item>Blink (EditorKit::Advance): while the field has the cursor, the blink flips once more than 0.5 s has passed since
/// the last flip, unless the timer is blocked; the block lasts one period.</item>
/// <item>ResetBlink: shown and the timer restarted when the cursor moves (EditorKit::SetCursorPos, ResetBlink(true)); shown,
/// restarted and the next flip skipped when the field gets the focus (TextField::OnFocus, sfr_gfx_03e.cpp:1124,
/// ResetBlink(true, true)).</item>
/// <item>Box (CalcCursorRectInLineBuffer, AddDrawCursorInfo): x is the line's x plus the advances of the glyphs before the
/// cursor, y the line's top and the height the line's (ascent plus descent, no leading); moved by the scroll into the
/// text rect (the field less its 2 px gutter), cut at the text rect's bottom when the line is shown, drawn 1 px wide and
/// only when the text rect holds it.</item>
/// <item>Colour: the default text format's colour when it is set, else opaque black (CursorColor's 0xFF000000). The client
/// also takes a highlight's text colour under the cursor; the runtime keeps no highlights, so there is none to take.</item>
/// <item>Only an editable field (not read-only) that has the focus draws it.</item>
/// </list>
/// </summary>
public partial class TextCaret : Node2D
{
    AsTextField field;
    int pos = -1, version = -1;
    bool blink, blocked;
    double timer;
    Rect2? box;
    Color color;

    public TextCaret() { Name = "Caret"; }

    /// <summary>Follows the movie's focus: the field that has it (when it is an input field drawn under
    /// <paramref name="holder"/>), its cursor moves and the blink; <paramref name="delta"/> is the time since the last call.</summary>
    public void Update(AsTextField focused, Node2D holder, Node2D content, double delta)
    {
        if (focused != null && (focused.type != "input" || holder == null)) focused = null;
        if (focused != field)
        {
            field = focused;
            if (field != null) ResetBlink(true, true);
            pos = field?.caretIndex ?? -1;
            version = field?.Version ?? -1;
        }
        if (field == null)
        {
            GetParent()?.RemoveChild(this);
            return;
        }
        if (GetParent() != holder) { GetParent()?.RemoveChild(this); holder.AddChild(this); }
        if (GetIndex() != holder.GetChildCount() - 1) holder.MoveChild(this, -1);
        if (field.caretIndex != pos || field.Version != version)
        {
            if (field.caretIndex != pos) ResetBlink(true, false);
            pos = field.caretIndex;
            version = field.Version;
        }
        Advance(delta);
        var b = Box(field, content);
        var c = Color(field);
        if (b != box || c != color || Visible != blink)
        {
            box = b; color = c; Visible = blink;
            QueueRedraw();
        }
    }

    void ResetBlink(bool state, bool block)
    {
        blink = state;
        timer = 0;
        if (block) blocked = true;
    }

    void Advance(double delta)
    {
        if (timer + delta > 0.5)
        {
            if (!blocked) blink = !blink;
            timer = 0;
            blocked = false;
        }
        else timer += delta;
    }

    static Color Color(AsTextField tf)
    {
        uint c = tf.Format?.color is { } v && !Rt.IsNullish(v) ? 0xff000000 | Rt.ToUint32(v) : 0xff000000;
        return new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, (c >> 24) / 255f);
    }

    /// <summary>The cursor box in field units, null when the text rect does not hold it.</summary>
    Rect2? Box(AsTextField tf, Node2D content)
    {
        var text = new Rect2((float)tf.Box.X + 2, (float)tf.Box.Y + 2, (float)tf.Box.W - 4, (float)tf.Box.H - 4);
        int at = Math.Clamp(tf.caretIndex, 0, tf.Text.Length);
        float lineH = (float)TextMeasure.LineHeight(tf.FontClass, tf.FontSize, tf.Leading);
        float height = (float)TextMeasure.LineHeight(tf.FontClass, tf.FontSize, 0);
        int line; float x;
        var node = content?.GetChildren().OfType<TextNode>().FirstOrDefault();
        if (node != null) (line, x) = LabelCursor(node, at);
        else (line, x) = MeasuredCursor(tf, at, text);
        float y = (float)tf.FirstLineTop(Math.Max(1, tf.numLines)) + (line - (tf.scrollV - 1)) * lineH;
        var r = new Rect2(x, y, 0, height);
        bool shown = line >= tf.scrollV - 1 && line <= tf.bottomScrollV - 1;
        if (r.End.Y > text.End.Y && shown) r = new Rect2(r.Position, 0, text.End.Y - r.Position.Y);
        // RectF::Contains with the zero-width box (x2 = x1 until it is drawn)
        if (r.Position.X < text.Position.X || r.Position.X > text.End.X || r.Position.Y < text.Position.Y || r.End.Y > text.End.Y) return null;
        return new Rect2(r.Position, 1, r.Size.Y);
    }

    /// <summary>
    /// The cursor's line and x in field units from the label the field is drawn with: the pen of the first glyph at or past
    /// the cursor on its line (the sum of the advances before it), the end of the last glyph past the line's text, the line's
    /// own x on an empty line; mapped through the label's transforms as the glyphs are drawn.
    /// </summary>
    (int Line, float X) LabelCursor(TextNode node, int at)
    {
        var label = node.Label;
        var lines = node.Layout();
        float lx;
        int li = 0;
        if (lines.Count == 0)
            lx = label.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (int)label.Size.X / 2,
                HorizontalAlignment.Right => (int)label.Size.X,
                _ => 0,
            };
        else
        {
            li = lines.FindIndex(l => at >= l.Start && at < l.End);
            if (li < 0) li = at < lines[0].Start ? 0 : lines.Count - 1;
            var line = lines[li];
            lx = line.LabelX + line.Dx;
            float pen = 0;
            bool found = false;
            foreach (var g in line.Shaped)
            {
                if (g.Index >= at) { pen = g.Pen; found = true; break; }
                pen = g.Pen + g.Advance * g.Repeat;
            }
            if (found || line.Shaped.Count > 0) lx += pen;
        }
        var global = label.GetGlobalTransform() * new Vector2(lx, 0);
        return (li, (GetParent<Node2D>().GetGlobalTransform().AffineInverse() * global).X);
    }

    /// <summary>The cursor's line and x for a field drawn from the runtime's lines (FieldNode): the measured width of the
    /// line's text before the cursor from where FieldNode puts the line.</summary>
    static (int Line, float X) MeasuredCursor(AsTextField tf, int at, Rect2 text)
    {
        var (lines, fit) = tf.Fitted();
        double size = tf.FontSize * fit;
        int li = 0, start = 0;
        for (; li < lines.Count - 1 && at > start + lines[li].Length; li++) start += lines[li].Length;
        string s = lines.Count > 0 ? lines[li] : "";
        double w = TextMeasure.Width(tf.FontClass, size, s), before = TextMeasure.Width(tf.FontClass, size, s[..Math.Clamp(at - start, 0, s.Length)]);
        double x = text.Position.X;
        var align = tf.Format?.align as string ?? "left";
        if (align == "center") x = tf.Box.X + (tf.Box.W - w) / 2;
        else if (align == "right") x = tf.Box.X + tf.Box.W - 2 - w;
        return (li, (float)(x + before));
    }

    public override void _Draw()
    {
        if (box is Rect2 r) DrawRect(r, color);
    }
}
