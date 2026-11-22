using Godot;
using Godot.Collections;

namespace Ms2Godot.Ui;

/// <summary>
/// The face of a text field's label drawn line by line, for a label of more than one line whose lines Scaleform places
/// each at its own exact x (TextNode.AlignLine): the label's own face is hidden and this draws the same glyphs where the
/// label lays them out, each line moved by its own x (LayoutLine.Dx). The lines and glyphs are TextNode.Layout's: each
/// glyph at its run's size, drawn from that size's raster scaled to it, at the label's own pen for it (each glyph of the
/// label's shaped line, repeat times, as Label::draw_glyph places them) and the baseline the line top plus the line ascent (as GlyphShadow places its glyphs); for text
/// with no FONT size, drawn with no move, it is the label's own face, pixel for pixel. A label showing htmlText formats
/// (TextNode.SetHtml) is drawn here too, each glyph in its run's colour, faux
/// bold and faux italic (FauxStyle), and each underlined run with its line. While the label shows its own face it draws
/// only a selection's glyphs over it (TextNode.SelectionOverFace), else nothing.
/// </summary>
public partial class LineFace : Node2D
{
    readonly TextNode text;

    public LineFace(TextNode text) { this.text = text; Name = NodeName; }

    static readonly StringName NodeName = "LineFace";

    public LineFace() { }

    public override void _Draw()
    {
        if (text == null) return;
        bool overFace = !text.FaceByLine && text.SelectionOverFace;
        if (!text.FaceByLine && !overFace) return;
        var label = text.Label;
        var ls = label.LabelSettings;
        if (ls?.Font == null || string.IsNullOrEmpty(label.Text)) return;
        var font = ls.Font;
        var ts = TextServerManager.GetPrimaryInterface();
        var canvas = GetCanvasItem();
        float scale = 1;                         // the draw transform's scale: label pixels per raster pixel
        bool shadow = !overFace && text.FaceDrawsShadow;
        foreach (var line in text.ShownLines())
        {
            float baseline = line.Baseline;
            // a hard drop shadow the label would draw (TextNode.FaceDrawsShadow) is drawn first: every glyph of the line at the
            // shadow offset in the shadow colour, as the label draws a line's shadows before its glyphs
            for (int pass = shadow ? 0 : 1; pass < 2; pass++)
            {
                bool face = pass == 1;
                var passOffset = face ? Vector2.Zero : ls.ShadowOffset;
                // the underline run being built: its start x, y, length and colour (a change of colour or underline closes it)
                float ulX = 0, ulY = 0, ulLen = 0;
                Color? ulColor = null;
                foreach (var g in line.Shaped)
                {
                    if (text.Hidden(g) || (overFace && !text.Selected(g.Index))) continue;
                    float pen = line.LabelX + g.Pen + line.Dx;
                    var style = text.StyleAt(g.Index);
                    var color = face ? text.GlyphColor(g.Index) : ls.ShadowColor;
                    float descent = text.DescentRatio * g.Size;
                    Color? underline = face && !overFace && style.Underline ? color : null;
                    if (underline != ulColor)
                    {
                        if (ulColor is Color uc) { Scale(ref scale, 1); Underline(ulX, ulY, ulLen, uc); }
                        ulX = pen; ulY = baseline + descent / 2; ulLen = g.Advance * g.Repeat;
                    }
                    else if (underline != null) ulLen += g.Advance * g.Repeat;
                    ulColor = underline;
                    if (face && text.ImageAt(g.Index) is TextNode.ImageBox img)
                    {
                        if (img.Tex != null && !overFace) { Scale(ref scale, 1); Image(img, pen, baseline); }
                        continue;
                    }
                    if (g.Glyph != 0)
                    {
                        // the glyph from its run's raster, scaled to the run's size
                        var (frid, shift) = FauxStyle.Font(font, g.Font, style.Bold, style.Italic, g.Raster, text.DescentRatio * g.Raster);
                        Scale(ref scale, g.Scale);
                        for (int r = 0; r < g.Repeat; r++)
                            ts.FontDrawGlyph(frid, canvas, g.Raster, (new Vector2(pen + r * g.Advance + g.Offset.X, baseline + g.Offset.Y) + passOffset) / g.Scale + shift, g.Glyph, color);
                    }
                }
                if (ulColor is Color last) { Scale(ref scale, 1); Underline(ulX, ulY, ulLen, last); }
            }
        }
        Scale(ref scale, 1);
    }

    /// <summary>
    /// An IMG char's image (TextMeshProvider::addImage, sfr_render_07o.cpp:1033): its top left at the pen and the baseline
    /// less its ascent, the pen x snapped to the nearest screen pixel (snapX: the text has no 3D and no free rotation),
    /// drawn at its size from its texture, clamped and filtered linearly. Label pixels.
    /// </summary>
    void Image(TextNode.ImageBox img, float pen, float baseline)
    {
        var k = text.Label.Scale;
        var toScreen = GetGlobalTransformWithCanvas();
        bool snap = toScreen.X.Y == 0 && toScreen.Y.X == 0;
        float x = pen;
        if (snap)
        {
            var p = toScreen * new Vector2(pen, baseline);
            x = (toScreen.AffineInverse() * new Vector2(MathF.Floor(p.X + 0.5f), p.Y)).X;
        }
        var rect = new Rect2(x, baseline - img.Ascent / k.Y, img.W / k.X, img.H / k.Y);
        DrawTextureRectRegion(img.Tex, rect, img.Region);
    }

    /// <summary>Draws what follows scaled by <paramref name="to"/> (the label's own pixels at 1).</summary>
    void Scale(ref float current, float to)
    {
        if (current == to) return;
        current = to;
        DrawSetTransformMatrix(Transform2D.Identity.Scaled(new Vector2(to, to)));
    }

    /// <summary>
    /// An underline (TextMeshProvider: a single underline is a stroke on the middle of the screen pixel row its y falls in,
    /// one pixel wide: solid width 0 with a 0.96 edge AA band, or solid 0.5 a side without edge AA).
    /// </summary>
    void Underline(float x, float y, float length, Color color)
    {
        if (length <= 0) return;
        var toScreen = GetGlobalTransformWithCanvas();
        var a = toScreen * new Vector2(x, y);
        var b = toScreen * new Vector2(x + length, y);
        float row = MathF.Floor(a.Y);
        var back = toScreen.AffineInverse();
        var p0 = back * new Vector2(a.X, row);
        var p1 = back * new Vector2(b.X, row + 1);
        DrawRect(new Rect2(p0, p1 - p0), color);
    }
}

/// <summary>
/// A text field's selection highlight (TextNode.SetSelection): TextNode.SelectionRects in the selection's highlight colour,
/// behind the label's glyphs (TextMeshProvider's layers: TextLayer_Selection after the background, before the shadow and
/// the text).
/// </summary>
public partial class SelectionHighlight : Node2D
{
    readonly TextNode text;

    public SelectionHighlight(TextNode text) { this.text = text; Name = NodeName; ShowBehindParent = true; }

    static readonly StringName NodeName = "Selection";

    public SelectionHighlight() { }

    /// <summary>The rects the last draw put down (a display check compares them with the field's current ones).</summary>
    public System.Collections.Generic.IReadOnlyList<Rect2> Drawn => drawn;
    readonly System.Collections.Generic.List<Rect2> drawn = new();

    public override void _Draw()
    {
        drawn.Clear();
        if (text == null || !text.HasSelection) return;
        var color = text.SelectionFill;
        drawn.AddRange(text.SelectionRects());
        foreach (var r in drawn) DrawRect(r, color);
    }
}

/// <summary>
/// Scaleform's faux bold and italic (GlyphCache::copyAndTransformShape, GlyphCacheParams FauxItalicAngle 0.25 and FauxBoldRatio
/// 0.045): the game's fonts have no bold or italic face (gfxfontlib's fonts are plain), so FontManager makes a faux handle and
/// the glyph cache styles the outline. Italic shears it, x + tan(-0.25) * (y + descent) with y down, about the line
/// descent above the baseline; bold offsets it outward by half of 0.045 of the font size on every side (Stroker width,
/// equidistant), after the shear. The advance stays the plain one (TextLayout AddChar). Here each style is a linked
/// variation of the glyph's font: FreeType emboldens by embolden * size / 16 pixels in all (Godot TextServerAdvanced), so
/// 0.72 is 0.045 of the size, and slants with the matrix x + tan(0.25) * y with y up; FT_Outline_Embolden keeps the left and
/// bottom edges, and the matrix pivots on the baseline, so the glyph is moved by the difference.
/// Godot applies the emboldening before the slant, Scaleform after: a bold italic stroke is sheared after it is widened.
/// </summary>
public static class FauxStyle
{
    const float ItalicAngle = 0.25f, BoldRatio = 0.045f;
    static readonly System.Collections.Generic.Dictionary<(ulong, bool, bool), Rid> variants = new();

    /// <summary>The font among <paramref name="f"/> and its fallbacks (in Font.GetRids order) whose own font is <paramref name="rid"/>.</summary>
    static Font Owner(Font f, Rid rid, int depth)
    {
        if (f == null || depth > 64) return null;
        var rids = f.GetRids();
        if (rids.Count > 0 && rids[0] == rid) return f;
        foreach (var fb in f.Fallbacks) if (Owner(fb, rid, depth + 1) is Font o) return o;
        return null;
    }

    /// <summary>The font to draw a glyph of <paramref name="font"/> in and the move from the plain glyph's position.</summary>
    public static (Rid Font, Vector2 Shift) Font(Font labelFont, Rid font, bool bold, bool italic, float fontSize, float descent)
    {
        if (!bold && !italic) return (font, Vector2.Zero);
        var key = (font.Id, bold, italic);
        if (!variants.TryGetValue(key, out var v))
        {
            // a variation cache of its own (Font.FindVariation): a linked variation shares the base font's data, embolden and transform included
            var owner = Owner(labelFont, font, 0);
            if (owner == null) return (font, Vector2.Zero);
            v = owner.FindVariation(new Dictionary(), 0, bold ? BoldRatio * 16 : 0,
                italic ? new Transform2D(new Vector2(1, MathF.Tan(ItalicAngle)), new Vector2(0, 1), Vector2.Zero) : Transform2D.Identity);
            variants[key] = v;
        }
        float half = bold ? BoldRatio * fontSize / 2 : 0;
        float shear = italic ? -MathF.Tan(ItalicAngle) * descent : 0;
        // emboldened first, the left edge kept and the glyph raised by half: the slant then moves that raise along x too
        if (bold && italic) shear -= MathF.Tan(ItalicAngle) * half;
        return (v, new Vector2(shear - half, half));
    }
}
