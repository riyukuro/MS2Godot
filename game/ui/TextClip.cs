using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The clip of a dynamic text field's glyphs to its text rect, the field less its 2 px gutters (DocView::SetViewRect,
/// sfr_render_03b.cpp:994: LineBuffer's VisibleRect). Scaleform lays a field's glyphs out in that rect and
/// (LineBuffer::CreateVisibleTextLayout, sfr_render_04d.cpp:494 / :575) drops a glyph wholly left of it, stops the line at
/// a glyph starting at or past its right edge, and gives the text layout the rect as its clip box (a mask the text mesh
/// draws its glyphs, shadows and underlines under, TextMeshProvider::addMask, sfr_render_07o.cpp:1775) when a glyph runs
/// across either side or the first shown line runs past the bottom. Static text gets none of it, and neither does text
/// that fits (DocView::Format, sfr_render_03b.cpp:2588: a read-only field whose text width and height are within the
/// field and which is not scrolled sideways is marked NoClipping, keeping the overhang of an italic glyph). So: text that
/// fits draws as it is; text that does not is drawn cut at the text rect.
/// <para>The node is a control between the field's node and its glyph nodes; <see cref="Inner"/> takes the field's
/// origin back, so the glyph nodes keep their places. It takes no mouse input.</para>
/// </summary>
public partial class TextClip : Control
{
    /// <summary>The parent of the field's glyph nodes, at the field node's origin.</summary>
    public readonly Node2D Inner = new() { Name = "Inner" };

    public TextClip()
    {
        Name = "TextClip";
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(Inner);
    }

    /// <summary>The clip, on or off; <paramref name="rect"/> is the text rect in the field node's own space.</summary>
    public void Set(bool on, Rect2 rect)
    {
        if (ClipContents != on) ClipContents = on;
        if (!on) return;
        var size = new Vector2(Math.Max(0, rect.Size.X), Math.Max(0, rect.Size.Y));
        if (Position != rect.Position) Position = rect.Position;
        if (Size != size) Size = size;
        if (Inner.Position != -rect.Position) Inner.Position = -rect.Position;
    }

    /// <summary>Scaleform's test: a read-only field's text of <paramref name="width"/> by <paramref name="height"/> (field units)
    /// fits a field of <paramref name="field"/> when neither is past the field's own size (ViewRect, the gutters included).</summary>
    public static bool Fits(bool readOnly, float width, float height, Vector2 field)
        => readOnly && width <= field.X && height <= field.Y;

    /// <summary>The text rect of a field box: the box less 2 px on every side.</summary>
    public static Rect2 TextRect(Rect2 box) => new(box.Position + new Vector2(2, 2), box.Size - new Vector2(4, 4));
}
