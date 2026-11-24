using System.Text.Json.Nodes;
using Godot;
using Godot.Collections;

namespace Ms2Godot.Ui;

/// <summary>
/// The text field's lines as Scaleform's ParagraphFormatter lays them out with each run at its own FONT size
/// (sfr_render_03b.cpp: Format, CheckWordWrap, FinalizeLine), over the label's own layout: a run at the field's size is laid
/// out exactly as the label lays it out, and a run at another size changes only what that size changes.
/// <list type="bullet">
/// <item>Every glyph has its run's size (Format: FontSize = GetActualFontSize(), Scale = FontSize / EM, advance = font
/// advance * Scale). A run is shaped at the label's raster size times its size over the field's and drawn from the glyph
/// cache's raster for its own screen size (SnapFontSizeToRamp) scaled to that size.</item>
/// <item>The lines break where those advances cross the text rect (CheckWordWrap: the label's own word-wrap rule over the
/// sized advances).</item>
/// <item>A line is IRound(MaxFontAscent + MaxFontDescent) twips tall and the next starts IRound(lineHeight + leading)
/// below it, the leading being the field's own when its format sets one and the line's largest font leading otherwise;
/// the baseline is MaxFontAscent below the line top (FinalizeLine; TrackFontParams per glyph, so each line takes the
/// largest size on it, and an empty line its own run's). The label already places lines of the field's size at the
/// field's advance and baseline (BaselineShift, FlashTextHeight), so a line moves by the difference between its own
/// metrics and those of a line at the field's size.</item>
/// </list>
/// </summary>
public partial class TextNode
{
    /// <summary>One glyph as drawn: its code point, font and glyph id, the raster size it is drawn from and the scale from that
    /// raster to label pixels, its pen on the baseline and offset in label pixels, its advance in label pixels and its run's
    /// size in label pixels.</summary>
    public readonly record struct PlacedGlyph(int Index, int Count, Rid Font, long Glyph, int Raster, float Scale, float Pen, Vector2 Offset,
        float Advance, int Repeat, float Size)
    {
        /// <summary>The code point past the glyph's cluster and whether the glyph has a character position of its own in the
        /// label (Label::get_character_bounds: a cluster's glyph with a glyph id, or a space).</summary>
        public int End { get; init; }
        public bool Bounded { get; init; }
    }

    /// <summary>One laid out line: its code point range, top and baseline in label pixels and its glyphs (pens without the
    /// line's exact-alignment move, TextNode.LineDx).</summary>
    public sealed class LayoutLine
    {
        public int Start, End;
        public float Top, Baseline;
        /// <summary>The baseline below the line top and the next line's top below it (with the paragraph spacing after a
        /// paragraph's last line), label pixels.</summary>
        public float Ascent;
        public double Pitch;
        /// <summary>The exact-alignment move of the line, label pixels (TextNode.AlignLine).</summary>
        public float Dx;
        /// <summary>The x the label puts the line at (Label::_get_line_rect), label pixels.</summary>
        public float LabelX;
        public (float Width, float Ratio) Width;
        /// <summary>The line's width as shaped, before its alignment (what <see cref="LabelX"/> is placed from), and its pitch without
        /// the paragraph spacing: a line kept for another width of the label is placed and spaced again from them.</summary>
        public float RawWidth;
        public double BasePitch;
        public string Text;
        /// <summary>The label's own glyphs of the line (its paragraph's shaped line), pens from the line's x; LineFace and
        /// GlyphShadow draw these as the label does (Label::draw_glyph: each glyph repeat times, one advance apart).</summary>
        public readonly List<PlacedGlyph> Shaped = new();
    }

    List<LayoutLine> layout;
    System.Collections.Generic.Dictionary<(int Start, int End), LayoutLine> lineCache;
    (string, float, int, TextServer.AutowrapMode, HorizontalAlignment, Font, GlyphStyle[]) linesKey;
    (string Text, float Width, float Spacing, int FontSize, TextServer.AutowrapMode Wrap, HorizontalAlignment Align, Font Font, GlyphStyle[] Styles) layoutKey;

    /// <summary>True when some run has a FONT size other than the field's.</summary>
    public bool HasSizedRuns { get; private set; }

    float FieldSize => UiBuilder.F(Def?["size"], 12);

    /// <summary>The FONT size of the code point at <paramref name="index"/> (the field's own when it has none).</summary>
    float SizeAt(int index) => StyleAt(index).Size ?? FieldSize;

    /// <summary>
    /// The lines the field shows, laid out with each run at its size; all of them, the visible character limit
    /// (Label.VisibleCharacters) applied by the reader.
    /// </summary>
    public List<LayoutLine> Layout()
    {
        var ls = Label.LabelSettings;
        var key = (Label.Text, Label.Size.X, ls.LineSpacing, ls.FontSize, Label.AutowrapMode, Label.HorizontalAlignment, ls.Font, runStyles);
        if (layout != null && key == layoutKey) return layout;
        layoutKey = key;
        // the lines are kept for the next width: a line of the same code points at the same text, font and spacing is the same
        // line (a break moves, the shaping and metrics of what is between two breaks do not)
        var kept = (key.Text, key.LineSpacing, key.FontSize, key.AutowrapMode, key.HorizontalAlignment, key.Font, key.runStyles);
        if (!kept.Equals(linesKey)) { lineCache = null; linesKey = kept; }
        return layout = BuildLayout();
    }

    List<LayoutLine> BuildLayout()
    {
        var res = new List<LayoutLine>();
        var ls = Label.LabelSettings;
        var font = ls.Font;
        string t = Label.Text;
        var cps = new List<int>();                 // code point -> UTF-16 index
        for (int c = 0; c < t.Length; c += char.IsSurrogatePair(t, c) ? 2 : 1) cps.Add(c);
        utf16 = cps;
        codePoints = cps.Select(c => char.ConvertToUtf32(t, c)).ToArray();
        if (font == null || string.IsNullOrEmpty(t)) return res;
        // the font's faces and OpenType features, read once per layout and freed after it: each read is a new tracked Godot
        // Array and Dictionary, which per run were left to the finalizer
        var fontRids = font.GetRids();
        using var fontRidsOwner = (Godot.Collections.Array)fontRids;
        using var features = font.GetOpentypeFeatures();
        int ramp = ls.FontSize;
        int Utf16(int cp) => cp < cps.Count ? cps[cp] : t.Length;
        var ts = TextServerManager.GetPrimaryInterface();
        string lang = string.IsNullOrEmpty(Label.Language) ? TranslationServer.GetLocale() : Label.Language;

        // Label::_shape: a paragraph per line break, each with a zero-width space at its end, broken at the label's width
        // (whole pixels) with its autowrap flags and the default trim flags; here each run is a span at its shaping size
        var flags = TextServer.LineBreakFlag.Mandatory | (Label.AutowrapMode switch
        {
            TextServer.AutowrapMode.Arbitrary => TextServer.LineBreakFlag.GraphemeBound,
            TextServer.AutowrapMode.Word => TextServer.LineBreakFlag.WordBound,
            TextServer.AutowrapMode.WordSmart => TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive,
            _ => 0,
        }) | Label.AutowrapTrimFlags;
        // Label::get_layout_data / get_character_bounds: whole-pixel font height and line spacing
        int fontH = (int)font.GetHeight(ramp), spacing = (int)ls.LineSpacing, paraSpacing = (int)ls.ParagraphSpacing;
        var before = lineCache;
        lineCache = new();
        int pstart = 0;
        while (pstart <= cps.Count)
        {
            int pend = pstart;
            while (pend < cps.Count && codePoints[pend] != '\n') pend++;
            var rid = ts.CreateShapedText();
            ts.ShapedTextSetDirection(rid, TextServer.Direction.Ltr);
            // the zero-width space ends the paragraph's last run (an empty paragraph: a run of its own, at its line break's size)
            var runs = Runs(pstart, pend).ToList();
            if (runs.Count == 0)
                ts.ShapedTextAddString(rid, "\u200B", fontRids, ShapeSize(SizeAt(Math.Min(pend, cps.Count - 1))), features, lang);
            for (int r = 0; r < runs.Count; r++)
            {
                var (a, b) = runs[r];
                bool last = r == runs.Count - 1;
                if (ImageAt(a) is ImageBox img)
                {
                    // an IMG char: an object as wide as its advance (TextNode.ImageBox)
                    ts.ShapedTextAddObject(rid, a, new Vector2(img.Advance / Label.Scale.X, img.H / Label.Scale.Y), InlineAlignment.Center, 1);
                    if (last) ts.ShapedTextAddString(rid, "\u200B", fontRids, ShapeSize(FieldSize), features, lang);
                    continue;
                }
                ts.ShapedTextAddString(rid, t.Substring(Utf16(a), Utf16(b) - Utf16(a)) + (last ? "\u200B" : ""), fontRids,
                    ShapeSize(SizeAt(a)), features, lang);
            }
            var breaks = ts.ShapedTextGetLineBreaks(rid, (int)Label.Size.X, 0, flags);
            for (int i = 0; i + 1 < breaks.Length; i += 2)
            {
                int start = pstart + breaks[i], end = Math.Min(pstart + breaks[i + 1], pend);
                if (before != null && before.TryGetValue((start, end), out var kept))
                {
                    kept.Pitch = kept.BasePitch;
                    kept.LabelX = LabelLineX(kept.RawWidth);
                    lineCache[(start, end)] = kept;
                    res.Add(kept);
                    continue;
                }
                var lrid = ts.ShapedTextSubstr(rid, breaks[i], breaks[i + 1] - breaks[i]);
                var line = new LayoutLine { Start = start, End = end, Text = t.Substring(Utf16(start), Utf16(Math.Max(start, end)) - Utf16(start)) };
                // the line as the label places it: as tall as its ascent and descent, at least the font height, centred in it,
                // measured with every run at the field's size
                var plain = new TextLine();
                plain.AddString(line.Text, font, ramp);
                double asc = ts.ShapedTextGetAscent(plain.GetRid()), dsc = ts.ShapedTextGetDescent(plain.GetRid());
                plain.Dispose();
                // the baseline as LineFace finds it (in floats, the font's own height)
                float fasc = (float)asc, fdsc = (float)dsc, fh = (float)font.GetHeight(ramp);
                if (fasc + fdsc < fh) fasc += (fh - (fasc + fdsc)) / 2;
                if (asc + dsc < fontH) { double diff = fontH - (asc + dsc); asc += diff / 2; dsc += diff - diff / 2; }
                var (ascMove, advMove) = LineMove(start, end, cps.Count);
                line.Ascent = fasc + ascMove;
                line.Pitch = asc + dsc + spacing + advMove;
                line.Width = PlaceGlyphs(line, ts, lrid, ramp, pstart);
                line.BasePitch = line.Pitch;
                line.RawWidth = (float)ts.ShapedTextGetSize(lrid).X * line.Width.Ratio;
                line.LabelX = LabelLineX(line.RawWidth);
                lineCache[(start, end)] = line;
                res.Add(line);
                ts.FreeRid(lrid);

            }
            ts.FreeRid(rid);
            if (res.Count > 0) res[^1].Pitch += paraSpacing;
            pstart = pend + 1;
        }
        return res;
    }

    /// <summary>
    /// The lines the label shows (Label::get_layout_data): from its first unskipped line (LinesSkipped), those whose running
    /// height fits the label's height plus one line spacing, and the first one always (as the client shows it), at most
    /// MaxLinesVisible; each line's top and baseline from the
    /// first shown line's top at 0. Glyphs past VisibleCharacters are not drawn (the reader skips them).
    /// </summary>
    public List<LayoutLine> ShownLines()
    {
        var all = Layout();
        var res = new List<LayoutLine>();
        int fit = FitLines(all);
        float top = 0;
        for (int i = Label.LinesSkipped; i < all.Count; i++)
        {
            var l = all[i];
            if (res.Count >= fit) break;
            if (Label.MaxLinesVisible >= 0 && res.Count >= Label.MaxLinesVisible) break;
            l.Top = top;
            l.Baseline = top + l.Ascent;
            top = (float)(top + l.Pitch);
            res.Add(l);
        }
        return res;
    }

    /// <summary>
    /// How many lines from the first unskipped one LineBuffer::IsLineVisible (sfr_render_04d.cpp:365 @0x1426696b0) shows:
    /// the first always (its top is in the view), each other one when its bottom (its offset from the first one plus its
    /// height) is at most the text rect's height (the field less its 2 px gutters) plus half a gutter. The offsets and
    /// heights are the client's (<see cref="ClientLine"/>, whole twips), not the label's pitch: Godot's whole-pixel line
    /// box can put a line a pixel lower than the client does, which must not hide it. Measured against the field, not the
    /// label: Godot raises a label to its minimum size, all its lines.
    /// </summary>
    int FitLines(List<LayoutLine> all)
    {
        if (!(Bounds.Size.Y > 0)) return all.Count - Label.LinesSkipped;
        double room = Bounds.Size.Y - 4 + 1, top = 0;
        int n = 0;
        for (int i = Label.LinesSkipped; i < all.Count; i++)
        {
            var (_, height, advance) = ClientLine(all[i].Start, all[i].End, codePoints.Length, j => SizeAt(j));
            if (i > Label.LinesSkipped && top + height > room) break;
            n++;
            top += advance;
        }
        return n;
    }

    /// <summary>
    /// The line this label draws from for a script field scrolled to the line starting at code point <paramref name="start"/>
    /// of a text of <paramref name="total"/> code points. The script's line count is the AVM's wrap, the label's is Godot's: the
    /// line holding that character, but never one below which fewer lines than the field holds would show; at the end of the
    /// scroll range the first line from which the last line still fits, so the newest line is always the last one drawn.
    /// </summary>
    public int FirstLineFor(int start, int total, bool atEnd)
    {
        var all = Layout();
        if (all.Count == 0) return 0;
        var starts = new int[all.Count];
        for (int i = 0; i < all.Count; i++) starts[i] = all[i].Start;
        if (total != codePoints.Length && total > 0) start = (int)((long)start * codePoints.Length / total);
        double room = Bounds.Size.Y > 0 ? Bounds.Size.Y - 4 + 1 : double.MaxValue;
        return Ms2.Ui.ScrolledLines.FirstLine(starts, LineHeightAdvance, room, start, atEnd);
    }

    /// <summary>A layout line's client height and advance (the metrics <see cref="FirstLineFor"/> measures lines by).</summary>
    (double Height, double Advance) LineHeightAdvance(int i)
    {
        var line = Layout()[i];
        var (_, height, advance) = ClientLine(line.Start, line.End, codePoints.Length, SizeAt);
        return (height, advance);
    }

    /// <summary>True when the glyph is past the label's visible character count.</summary>
    public bool Hidden(in PlacedGlyph g) => Label.VisibleCharacters >= 0 && g.Index >= Label.VisibleCharacters;

    Func<int, float> sizeAt;

    /// <summary>
    /// The selection's highlight in label pixels, as TextLayout builds it (sfr_render_04d.cpp:625): each selected glyph's
    /// rectangle, its pen to its advance across and its line's top (the baseline less BaseLineOffset, MaxFontAscent) to the
    /// line's height plus its leading when that is not negative down, joined while the glyphs follow one another (the
    /// highlight colour is the same throughout). Shown lines only, without the glyphs past the visible characters.
    /// </summary>
    public List<Rect2> SelectionRects()
    {
        var res = new List<Rect2>();
        if (!HasSelection || string.IsNullOrEmpty(Label.Text)) return res;
        sizeAt ??= SizeAt;
        foreach (var line in ShownLines())
        {
            var (asc, height, advance) = ClientLine(line.Start, line.End, codePoints.Length, sizeAt);
            float top = line.Baseline - asc / Label.Scale.Y, h = Math.Max(height, advance) / Label.Scale.Y;
            float x0 = 0, x1 = 0;
            bool open = false;
            foreach (var g in line.Shaped)
            {
                if (Hidden(g) || !Selected(g.Index))
                {
                    if (open) res.Add(new Rect2(x0, top, x1 - x0, h));
                    open = false;
                    continue;
                }
                float pen = line.LabelX + g.Pen + line.Dx, end = pen + g.Advance * g.Repeat;
                if (!open) { x0 = pen; x1 = end; open = true; }
                else { x0 = Math.Min(x0, pen); x1 = Math.Max(x1, end); }
            }
            if (open) res.Add(new Rect2(x0, top, x1 - x0, h));
        }
        return res;
    }

    /// <summary>
    /// The label's character position x of code point <paramref name="index"/> in label pixels (Label::get_character_bounds:
    /// the pen of the first glyph with a position of its own whose cluster holds it), null when it has none; with no index,
    /// the line's first character that has one.
    /// </summary>
    public static float? CharX(LayoutLine line, int index = -1)
    {
        foreach (var g in line.Shaped)
            if (g.Bounded && (index < 0 || (index >= g.Index && index < g.End))) return line.LabelX + g.Pen;
        return null;
    }

    /// <summary>Label::_get_line_rect: a line's x in label pixels for its width (whole pixels, the offset truncated).</summary>
    float LabelLineX(float width) => Label.HorizontalAlignment switch
    {
        HorizontalAlignment.Center => (int)(Label.Size.X - width) / 2,
        HorizontalAlignment.Right => (int)(Label.Size.X - width),
        _ => 0,
    };

    /// <summary>Maximal ranges of code points in [a, b) that share a size; an IMG char is a range of its own.</summary>
    IEnumerable<(int, int)> Runs(int a, int b)
    {
        int s = a;
        for (int i = a + 1; i <= b; i++)
            if (i == b || SizeAt(i) != SizeAt(s) || ImageAt(i) != null || ImageAt(s) != null) { if (i > s) yield return (s, i); s = i; }
    }

    /// <summary>The run's size in label pixels (the label's raster size scaled by the run's size over the field's).</summary>
    float LabelSize(float size) => size == FieldSize ? Label.LabelSettings.FontSize : Label.LabelSettings.FontSize * size / FieldSize;

    /// <summary>The whole font size a run is shaped at: its size in label pixels.</summary>
    int ShapeSize(float size) => Math.Max(1, (int)MathF.Round(LabelSize(size)));

    /// <summary>
    /// The glyphs of the shaped line <paramref name="rid"/> (the label's pen: the sum of the advances before each), each
    /// run's advances and offsets scaled from its shaping size to its exact size, drawn from its own ramp raster
    /// (SnapFontSizeToRamp of its screen size). Returns the line's width in label pixels and that width over the shaped one.
    /// </summary>
    (float Width, float Ratio) PlaceGlyphs(LayoutLine line, TextServer ts, Rid rid, int ramp, int first)
    {
        float pen = 0, shaped = 0;
        var glyphs = ts.ShapedTextGetGlyphs(rid);
        using var glyphsOwner = (Godot.Collections.Array)glyphs;
        foreach (Dictionary g in glyphs)
        {
            using var _ = g;
            int index = first + (int)g[GlyphKeys.Start], at = Math.Min(index, codePoints.Length - 1), repeat = (int)g[GlyphKeys.Repeat];
            float size = SizeAt(at), labelSize = LabelSize(size);
            bool image = ImageAt(at) != null;                       // an IMG object: its advance is in label pixels already
            float k = image ? 1 : labelSize / ShapeSize(size);       // label pixels per shaped pixel
            // a run of another size in readable text keeps the field font's oversampling: its raster is the whole size nearest
            // its quarter-pixel size, times that
            int raster = size == FieldSize ? ramp : (int)MathF.Max(1, MathF.Round(CachedSize(size * ScreenScale, Readable)));
            float advance = (float)g[GlyphKeys.Advance];
            int count = (int)g[GlyphKeys.Count];
            long id = image ? 0 : (long)g[GlyphKeys.Index];
            var gflags = (TextServer.GraphemeFlag)(long)g[GlyphKeys.Flags];
            line.Shaped.Add(new PlacedGlyph(index, count, (Rid)g[GlyphKeys.FontRid], id, raster, labelSize / raster,
                pen, (Vector2)g[GlyphKeys.Offset] * k, advance * k, repeat, labelSize)
            {
                End = first + (int)g[GlyphKeys.End],
                Bounded = count > 0 && (image || id != 0 || (gflags & TextServer.GraphemeFlag.Space) != 0),
            });
            pen += advance * k * repeat;
            shaped += advance * repeat;
        }
        return (pen, shaped > 0 ? pen / shaped : 1);
    }

    /// <summary>
    /// How far the line [start, end) moves from a line of the field's size, in label pixels: its baseline (MaxFontAscent) and
    /// the next line's top (IRound(lineHeight + leading)), both 0 for a line all at the field's size.
    /// </summary>
    (float Ascent, float Advance) LineMove(int start, int end, int total)
    {
        var (asc, _, adv) = ClientLine(start, end, total, i => SizeAt(i));
        var (fasc, _, fadv) = ClientLine(start, end, total, _ => FieldSize, false);
        return ((asc - fasc) / Label.Scale.Y, (adv - fadv) / Label.Scale.Y);
    }

    /// <summary>
    /// How much taller the text is than the same lines at the field's size with no images, in field units (DocView::Format:
    /// the last line's offset plus its height): every line's advance but the last one's and the last line's height. 0
    /// without sized runs or images.
    /// </summary>
    float SizedHeightMove()
    {
        if (!HasSizedRuns && !HasImages) return 0;
        var lines = Layout();
        float move = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var (_, h, adv) = ClientLine(lines[i].Start, lines[i].End, codePoints.Length, j => SizeAt(j));
            var (_, fh, fadv) = ClientLine(lines[i].Start, lines[i].End, codePoints.Length, _ => FieldSize, false);
            move += i < lines.Count - 1 ? adv - fadv : h - fh;
        }
        return move;
    }

    /// <summary>
    /// FinalizeLine's metrics of the line [start, end) in field units, the sizes from <paramref name="size"/>: MaxFontAscent,
    /// the line height IRound(maxAsc + maxDesc) and the next line's offset, IRound(lineHeight + IRound(leading)) twips. TrackFontParams takes every glyph but
    /// the paragraph end; an empty line takes its paragraph end's. An IMG char takes no font: it raises MaxFontAscent to
    /// its ascent and MaxFontDescent to its height less that (sfr_render_03b.cpp:2150); with <paramref name="images"/>
    /// false it is the space the label lays out in its place.
    /// </summary>
    (float Ascent, float Height, float Advance) ClientLine(int start, int end, int total, Func<int, float> size, bool images = true)
    {
        float ascR = AscentRatio, descR = lineRatios.Height - AscentRatio;
        float maxAsc = 0, maxDesc = 0, maxLead = 0;
        bool any = false;
        for (int i = start; i < end; i++)
        {
            if (CodePointAt(i) == '\n') continue;
            if (images && ImageAt(i) is ImageBox img)
            {
                maxAsc = Math.Max(maxAsc, img.Ascent * 20); maxDesc = Math.Max(maxDesc, (img.H - img.Ascent) * 20);
                any = true;
                continue;
            }
            float s = size(i) * 20;
            maxAsc = Math.Max(maxAsc, ascR * s); maxDesc = Math.Max(maxDesc, descR * s); maxLead = Math.Max(maxLead, lineRatios.Leading * s);
            any = true;
        }
        if (!any)
        {
            float s = size(Math.Min(start, Math.Max(0, total - 1))) * 20;
            maxAsc = ascR * s; maxDesc = descR * s; maxLead = lineRatios.Leading * s;
        }
        float leading = Def?["leading"] != null ? UiBuilder.F(Def["leading"], 0) * 20 : maxLead;
        float lineHeight = IRound(maxAsc + maxDesc);
        return (maxAsc / 20, lineHeight / 20, IRound(lineHeight + IRound(leading)) / 20);
    }

    int[] codePoints = System.Array.Empty<int>();
    List<int> utf16 = new();

    /// <summary>The text of the code points [a, b).</summary>
    string Substring(int a, int b)
    {
        string t = Label.Text;
        int i = a < utf16.Count ? utf16[a] : t.Length, j = b < utf16.Count ? utf16[b] : t.Length;
        return t.Substring(i, j - i);
    }

    int CodePointAt(int cp) => cp >= 0 && cp < codePoints.Length ? codePoints[cp] : 0;

    /// <summary>Alg::IRound.</summary>
    static float IRound(float v) => MathF.Floor(v + 0.5f);

    /// <summary>The font's ascent per pixel of font size (DefineFont layout).</summary>
    public float AscentRatio;
}
