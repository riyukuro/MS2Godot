using Godot;
using Godot.Collections;

namespace Ms2Godot.Ui;

/// <summary>
/// The face of a text field's label drawn line by line, for a label of more than one line whose lines Scaleform places
/// each at its own exact x (TextNode.AlignLine): the label's own face is hidden and this draws the same glyphs where the
/// label lays them out, each line moved by TextNode.LineDx. A line is its text shaped with the label's font and size,
/// the pen of each glyph cluster is the label's character bounds and the baseline is the line top plus the line ascent
/// centred in the font height (as GlyphShadow places its glyphs); drawn with no move it is the label's own face, pixel
/// for pixel. A label showing htmlText formats (TextNode.SetHtml) is drawn here too, each glyph in its run's colour, faux
/// bold and faux italic (FauxStyle), and each underlined run with its line. It draws nothing while the label shows its own
/// face.
/// </summary>
public partial class LineFace : Node2D
{
    readonly TextNode text;

    public LineFace(TextNode text) { this.text = text; Name = "LineFace"; }

    public LineFace() { }

    public override void _Draw()
    {
        if (text == null || !text.FaceByLine) return;
        var label = text.Label;
        var ls = label.LabelSettings;
        if (ls?.Font == null || string.IsNullOrEmpty(label.Text)) return;
        var font = ls.Font;
        int fontSize = ls.FontSize;
        float fontH = (float)font.GetHeight(fontSize);
        float descent = text.DescentRatio * fontSize;
        var ts = TextServerManager.GetPrimaryInterface();
        var canvas = GetCanvasItem();
        foreach (var (start, _, top, line) in TextNode.LabelLines(label))
        {
            float dx = text.LineDx(start);
            var tl = new TextLine();
            tl.AddString(line, font, fontSize);
            var rid = tl.GetRid();
            float asc = (float)ts.ShapedTextGetAscent(rid), dsc = (float)ts.ShapedTextGetDescent(rid);
            if (asc + dsc < fontH) asc += (fontH - (asc + dsc)) / 2;
            float baseline = top + asc, pen = 0;
            // the underline run being built: its start x, length and colour (a change of colour or underline closes it)
            float ulX = 0, ulLen = 0;
            Color? ulColor = null;
            foreach (Dictionary g in ts.ShapedTextGetGlyphs(rid))
            {
                int first = (int)g["start"], count = (int)g["count"], repeat = (int)g["repeat"];
                long index = (long)g["index"];
                float advance = (float)g["advance"];
                int at = start + first;
                if (count > 0)
                {
                    var b = label.GetCharacterBounds(at);
                    if (b.Size.Y > 0) pen = b.Position.X;
                }
                var style = text.StyleAt(at);
                var color = text.GlyphColor(at);
                Color? underline = style.Underline ? color : null;
                if (underline != ulColor)
                {
                    if (ulColor is Color uc) Underline(ulX, baseline + descent / 2, ulLen, uc);
                    ulX = pen + dx; ulLen = advance * repeat;
                }
                else if (underline != null) ulLen += advance * repeat;
                ulColor = underline;
                if (index != 0)
                {
                    var o = (Vector2)g["offset"];
                    var (frid, shift) = FauxStyle.Font(font, (Rid)g["font_rid"], style.Bold, style.Italic, fontSize, descent);
                    ts.FontDrawGlyph(frid, canvas, (long)g["font_size"], new Vector2(pen + dx + o.X, baseline + o.Y) + shift, index, color);
                }
                pen += advance * repeat;
            }
            if (ulColor is Color last) Underline(ulX, baseline + descent / 2, ulLen, last);
            tl.Dispose();
        }
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
