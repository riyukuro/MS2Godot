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
    /// <remarks>Fields, not properties: a kept line's glyphs are read and moved in a full log's churn (<see cref="MoveLine"/>),
    /// and each property accessor first read there is a method compiled in the window.</remarks>
    public struct PlacedGlyph
    {
        public int Index, Count;
        public Rid Font;
        public long Glyph;
        public int Raster;
        public float Scale, Pen;
        public Vector2 Offset;
        public float Advance;
        public int Repeat;
        public float Size;
        /// <summary>The code point past the glyph's cluster.</summary>
        public int End;
        /// <summary>Whether the glyph has a character position of its own in the label (Label::get_character_bounds: a cluster's
        /// glyph with a glyph id, or a space).</summary>
        public bool Bounded;

        public PlacedGlyph(int index, int count, Rid font, long glyph, int raster, float scale, float pen, Vector2 offset,
            float advance, int repeat, float size)
        {
            Index = index; Count = count; Font = font; Glyph = glyph; Raster = raster; Scale = scale; Pen = pen; Offset = offset;
            Advance = advance; Repeat = repeat; Size = size; End = 0; Bounded = false;
        }
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
        /// <summary>An array, not a list: moving a kept line (<see cref="MoveLine"/>, each time the oldest line of a full log drops) reads and
        /// writes every glyph, and a list's accessors over this struct are JIT-compiled when the first line moves.</summary>
        public PlacedGlyph[] Shaped = new PlacedGlyph[0];
        /// <summary>TextNode.ClientLineX's last answer for the line and what it was for (its shown length, the alignment, the
        /// label's width and scale): a redraw of an unchanged line does not shape its runs again.</summary>
        public int ClientXLength = -1;
        public HorizontalAlignment ClientXAlign;
        public float ClientXWidth, ClientXScale;
        public float? ClientX;
    }

    List<LayoutLine> layout;
    // what the kept paragraphs were laid out at; compared field by field (a ValueTuple.Equals over these types is JIT-compiled)
    float keptSpacing; int keptFontSize; TextServer.AutowrapMode keptWrap; HorizontalAlignment keptAlign; Font keptFont;
    (string Text, float Width, float Spacing, int FontSize, TextServer.AutowrapMode Wrap, HorizontalAlignment Align, Font Font, GlyphStyle[] Styles) layoutKey;

    /// <summary>
    /// A paragraph laid out: the text and formats it was laid out from and its lines, kept for the next layout
    /// (DocView::Format, sfr_render_03b.cpp:2282: a paragraph whose id and modification counter are unchanged keeps its lines, they
    /// only move to the paragraph's new text position and y offset; only a new or modified paragraph is formatted again, which is
    /// all an append to the log is, DocView::AppendText, :251). A paragraph is the same one when its text and the formats of its
    /// chars (and of the char that ends it) are the same, wherever it now stands.
    /// </summary>
    sealed class LaidParagraph
    {
        public string Text;
        public GlyphStyle[] Styles;
        /// <summary>The code point the paragraph's lines are numbered from, and the label width they were broken for.</summary>
        public int Base;
        public float Width;
        public List<LayoutLine> Lines;
        /// <summary>The next paragraph with the same text hash.</summary>
        public LaidParagraph Next;
    }

    // the paragraphs of the last layout by the hash of their text; a paragraph found there moves to the next layout's table, and
    // each is used once (two paragraphs of the same text have lines of their own)
    System.Collections.Generic.Dictionary<int, LaidParagraph> paragraphs = new(), paragraphsNext = new();

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
        string text = LabelText;
        var key = (text, Label.Size.X, ls.LineSpacing, ls.FontSize, Label.AutowrapMode, Label.HorizontalAlignment, ls.Font, runStyles);
        if (layout != null && key == layoutKey) return layout;
        layoutKey = key;
        // the paragraphs are kept for the next text and width: a paragraph of the same text and formats at the same font and
        // spacing is the same paragraph (a break moves, the shaping and metrics of what is between two breaks do not)
        if (key.LineSpacing != keptSpacing || key.FontSize != keptFontSize || key.AutowrapMode != keptWrap || key.HorizontalAlignment != keptAlign || !ReferenceEquals(key.Font, keptFont))
        {
            paragraphs.Clear();
            (keptSpacing, keptFontSize, keptWrap, keptAlign, keptFont) = (key.LineSpacing, key.FontSize, key.AutowrapMode, key.HorizontalAlignment, key.Font);
        }
        return layout = BuildLayout(text);
    }

    /// <summary>The paragraph kept from the last layout whose text is the <paramref name="length"/> UTF-16 units of
    /// <paramref name="text"/> from <paramref name="at"/> and whose formats are those of code points [first, first + count).</summary>
    LaidParagraph TakeLaidParagraph(string text, int at, int length, int first, int count)
    {
        int hash = string.GetHashCode(text.AsSpan(at, length));
        if (!paragraphs.TryGetValue(hash, out var p)) return null;
        LaidParagraph prev = null;
        var span = text.AsSpan(at, length);
        var styles = FormatsOf(first, count);
        for (; p != null; prev = p, p = p.Next)
        {
            if (!span.SequenceEqual(p.Text) || !SameStyles(styles, p.Styles)) continue;
            if (prev == null) { if (p.Next == null) paragraphs.Remove(hash); else paragraphs[hash] = p.Next; }
            else prev.Next = p.Next;
            p.Next = null;
            return p;
        }
        return null;
    }

    /// <summary>A hash of every line's range, metrics and glyphs; with <paramref name="fresh"/> the paragraphs kept from earlier layouts
    /// are dropped first, so the lines are all laid out anew (the chat append shot checks the two are the same).</summary>
    public int LayoutDigest(bool fresh)
    {
        if (fresh) { paragraphs.Clear(); layout = null; }
        var h = new HashCode();
        foreach (var l in Layout())
        {
            h.Add(l.Start); h.Add(l.End); h.Add(l.Text); h.Add(l.Ascent); h.Add(l.BasePitch); h.Add(l.Pitch); h.Add(l.RawWidth); h.Add(l.LabelX); h.Add(l.Width);
            foreach (var g in l.Shaped) { h.Add(g.Index); h.Add(g.End); h.Add(g.Count); h.Add(g.Glyph); h.Add(g.Raster); h.Add(g.Pen); h.Add(g.Advance); h.Add(g.Size); h.Add(g.Bounded); }
        }
        return h.ToHashCode();
    }

    /// <summary>The formats of code points [first, first + count) as far as the text has them (the rest are the field's own).</summary>
    ReadOnlySpan<GlyphStyle> FormatsOf(int first, int count)
    {
        if (runStyles == null || first >= runCount) return default;
        return runStyles.AsSpan(first, Math.Min(count, runCount - first));
    }

    /// <summary>Keeps <paramref name="p"/> for the next layout.</summary>
    void KeepLaidParagraph(LaidParagraph p)
    {
        int hash = string.GetHashCode(p.Text.AsSpan());
        paragraphsNext.TryGetValue(hash, out var head);
        p.Next = head;
        paragraphsNext[hash] = p;
    }

    /// <summary>Whether two runs of formats are equal, field by field: GlyphStyle's own Equals goes through EqualityComparer
    /// instantiations over Color? and float?, which are JIT-compiled the first time a text with formats is laid out.</summary>
    static bool SameStyles(ReadOnlySpan<GlyphStyle> a, GlyphStyle[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < b.Length; i++)
        {
            ref readonly var x = ref a[i]; var y = b[i];
            if (x.Bold != y.Bold || x.Italic != y.Italic || x.Underline != y.Underline || !ReferenceEquals(x.Image, y.Image)) return false;
            if (x.Color.HasValue != y.Color.HasValue || (x.Color.HasValue && x.Color.Value != y.Color.Value)) return false;
            if (x.Size.HasValue != y.Size.HasValue || (x.Size.HasValue && x.Size.Value != y.Size.Value)) return false;
        }
        return true;
    }

    /// <summary>A kept line <paramref name="d"/> code points further on.</summary>
    static void MoveLine(LayoutLine line, int d)
    {
        if (d == 0) return;
        line.Start += d; line.End += d;
        var glyphs = line.Shaped;
        for (int i = 0; i < glyphs.Length; i++)
        {
            glyphs[i].Index += d;
            glyphs[i].End += d;
        }
    }

    List<LayoutLine> BuildLayout(string t)
    {
        var res = new List<LayoutLine>();
        var ls = Label.LabelSettings;
        var font = ls.Font;
        var cps = utf16;                           // code point -> UTF-16 index, the list kept from the last layout
        cps.Clear();
        for (int c = 0; c < t.Length; c += char.IsSurrogatePair(t, c) ? 2 : 1) cps.Add(c);
        if (codePoints.Length < cps.Count) codePoints = new int[Math.Max(cps.Count, codePoints.Length * 2)];
        cpCount = cps.Count;
        for (int c = 0; c < cpCount; c++) codePoints[c] = char.ConvertToUtf32(t, cps[c]);
        if (font == null || string.IsNullOrEmpty(t)) { paragraphs.Clear(); return res; }
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
        float width = Label.Size.X;
        paragraphsNext.Clear();
        int pstart = 0;
        while (pstart <= cps.Count)
        {
            int pend = pstart;
            while (pend < cps.Count && codePoints[pend] != '\n') pend++;
            // an empty paragraph takes its size from the char before it, so it is laid out where it stands every time; the formats
            // that count for the others are those of its chars and of the char that ends it (its zero-width space's size)
            LaidParagraph para = null;
            bool kept = false;
            if (pend > pstart)
            {
                para = TakeLaidParagraph(t, Utf16(pstart), Utf16(pend) - Utf16(pstart), pstart, pend + 1 - pstart);
                if (para != null && para.Width == width)
                {
                    // unchanged: its lines only move to the paragraph's new position
                    if (pstart != para.Base)      // a kept paragraph's lines move by the difference of its positions
                    {
                        foreach (var kept1 in para.Lines) MoveLine(kept1, pstart - para.Base);
                        para.Base = pstart;
                    }
                    foreach (var line in para.Lines)
                    {
                        line.Pitch = line.BasePitch;
                        line.LabelX = LabelLineX(line.RawWidth);
                        res.Add(line);
                    }
                    kept = true;
                }
            }
            if (kept)
            {
                KeepLaidParagraph(para);
                if (res.Count > 0) res[^1].Pitch += paraSpacing;
                pstart = pend + 1;
                continue;
            }
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
            var lines = new List<LayoutLine>();
            var breaks = ts.ShapedTextGetLineBreaks(rid, (int)Label.Size.X, 0, flags);
            // a space hangs past the text rect instead of wrapping (CheckWordWrap, sfr_render_03b.cpp:1918: `!isSpace`), the engine counts it
            if (breaks.Length > 2 && Label.AutowrapMode != TextServer.AutowrapMode.Off)
                breaks = Ms2.Ui.HangingSpaces.Fit(breaks, i => pstart + i < pend && codePoints[pstart + i] == ' ',
                    (a, b) => { var sub = ts.ShapedTextSubstr(rid, a, b - a); double w = ts.ShapedTextGetSize(sub).X; ts.FreeRid(sub); return w; }, (int)Label.Size.X);
            for (int i = 0; i + 1 < breaks.Length; i += 2)
            {
                int start = pstart + breaks[i], end = Math.Min(pstart + breaks[i + 1], pend);
                // the paragraph laid out before at another width: a line of the same code points is the same line
                LayoutLine old = null;
                if (para != null)
                    foreach (var l in para.Lines)
                        if (l.Start - para.Base == start - pstart && l.End - para.Base == end - pstart) { old = l; break; }
                if (old != null)
                {
                    MoveLine(old, pstart - para.Base);
                    old.Pitch = old.BasePitch;
                    old.LabelX = LabelLineX(old.RawWidth);
                    lines.Add(old);
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
                lines.Add(line);
                ts.FreeRid(lrid);
            }
            ts.FreeRid(rid);
            res.AddRange(lines);
            if (pend > pstart)
            {
                para ??= new LaidParagraph { Text = t.Substring(Utf16(pstart), Utf16(pend) - Utf16(pstart)), Styles = FormatsOf(pstart, pend + 1 - pstart).ToArray() };
                para.Base = pstart; para.Width = width; para.Lines = lines;
                KeepLaidParagraph(para);
            }
            if (res.Count > 0) res[^1].Pitch += paraSpacing;
            pstart = pend + 1;
        }
        (paragraphs, paragraphsNext) = (paragraphsNext, paragraphs);
        paragraphsNext.Clear();
        return res;
    }

    // ---- the label's window of the text ----------------------------------------------------------------------------------
    // The label's own layout is not what draws a coloured field: every glyph comes from Layout() (LineFace), the label's face is
    // hidden (SelfModulate). But Label shapes the whole text again whenever it changes (it keeps no paragraph of an earlier
    // shaping): a full chat log's append cost the engine 12 ms for that shaping alone. A long coloured text is therefore given
    // to the label as a window of whole paragraphs around the shown lines; the field keeps the text, the layout and the first
    // shown line of the whole (firstLine), and the label's LinesSkipped is that less the lines before its window.

    /// <summary>Text shorter than this goes to the label whole, and so does a text of fewer lines than <see cref="MinWindowLines"/>.</summary>
    const int MinWindowChars = 4000, MinWindowLines = 96;

    string engineText;                    // what the label was last given as its text
    List<LayoutLine> windowLayout;        // the layout the window was cut from, and its line range [windowLo, windowHi)
    int windowLo, windowHi;

    /// <summary>
    /// True when the label's own face is never seen: the field draws every glyph by line (the same condition AlignLine hides the
    /// face by, for text with html formats).
    /// </summary>
    bool FaceByRuns()
    {
        var ls = Label.LabelSettings;
        if (runStyles == null || ls == null) return false;
        bool plain = ls.ShadowColor.A <= 0 && ls.OutlineSize <= 0;
        return plain || ((HasSizedRuns || HasImages) && ls.OutlineSize <= 0 && ls.ShadowSize <= 0);
    }

    /// <summary>The text is a code point range of the layout: the UTF-16 index of code point <paramref name="cp"/> (from the last layout).</summary>
    int Utf16Of(int cp) => cp < utf16.Count ? utf16[cp] : labelText.Length;

    /// <summary>True when the layout line <paramref name="i"/> starts a paragraph (a line break before it, or the text's start).</summary>
    bool StartsParagraph(List<LayoutLine> all, int i) => i <= 0 || i >= all.Count || all[i].Start == 0 || codePoints[all[i].Start - 1] == '\n';

    /// <summary>
    /// Gives the label the text it draws: the whole text, or for a long coloured text the paragraphs from one screenful above the
    /// first shown line to one and a half below it (the window is kept while the shown lines stay inside it, so a scroll moves
    /// only the label's skipped count), with LinesSkipped set to the lines of the window before the first shown one.
    /// </summary>
    void SyncWindow()
    {
        string full = LabelText;
        List<LayoutLine> all = null;
        if (full.Length >= MinWindowChars && Label.VisibleCharacters < 0 && FaceByRuns() && (all = Layout()).Count >= MinWindowLines)
        {
            int first = Math.Min(firstLine, all.Count - 1), fit = Math.Max(1, FitLines(all));
            int skip;
            if (ReferenceEquals(windowLayout, all) && first >= windowLo && (windowHi >= all.Count || first + fit + 1 <= windowHi)) skip = first - windowLo;
            else
            {
                int lo = Math.Max(0, first - fit), hi = Math.Min(all.Count, first + fit + fit / 2 + 2);
                while (lo > 0 && !StartsParagraph(all, lo)) lo--;
                while (hi < all.Count && !StartsParagraph(all, hi)) hi++;
                windowLayout = all; windowLo = lo; windowHi = hi;
                int a = Utf16Of(all[lo].Start), b = hi < all.Count ? Utf16Of(all[hi].Start - 1) : full.Length;      // the break before the next paragraph is not in the window
                SetEngineText(full.Substring(a, b - a));
                skip = first - lo;
            }
            if (Label.LinesSkipped != skip) Label.LinesSkipped = skip;
            return;
        }
        windowLayout = null;
        SetEngineText(full);
        if (Label.LinesSkipped != firstLine) Label.LinesSkipped = firstLine;
    }

    void SetEngineText(string text)
    {
        if (engineText != null && string.Equals(engineText, text)) return;
        Label.Text = engineText = text;
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
        for (int i = firstLine; i < all.Count; i++)
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
        if (!(Bounds.Size.Y > 0)) return all.Count - firstLine;
        // the one sum the scrolled field's first line is chosen by (ScrolledLines.Fit), so the cap and the first line agree
        // when the text exactly fills the box; `all` is the current layout (every caller read it with Layout() just before)
        return Ms2.Ui.ScrolledLines.Fit(all.Count, lineHeightAdvance ??= LineHeightAdvance, Bounds.Size.Y - 4 + 1, firstLine);
    }

    /// <summary>
    /// The line this label draws from for a script field scrolled to the line starting at code point <paramref name="start"/>
    /// of the script's text (<paramref name="start"/> a UTF-16 index into <paramref name="text"/>). The script's line count is the AVM's wrap, the label's is Godot's: the
    /// line holding that character, but never one below which fewer lines than the field holds would show; at the end of the
    /// scroll range the first line from which the last line still fits, so the newest line is always the last one drawn.
    /// </summary>
    public int FirstLineFor(string text, int start, bool atEnd)
    {
        var all = Layout();
        if (all.Count == 0) return 0;
        // code points of the script's text before the index, and in all of it (a low surrogate is half a code point); inline, not a
        // helper: each method first called in a full chat log's churn is compiled in that window
        int total = 0, first = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLowSurrogate(text[i])) continue;
            if (i < start) first++;
            total++;
        }
        start = first;
        if (total != cpCount && total > 0) start = (int)((long)start * cpCount / total);
        double room = Bounds.Size.Y > 0 ? Bounds.Size.Y - 4 + 1 : double.MaxValue;
        return Ms2.Ui.ScrolledLines.FirstLine(all.Count, lineStart ??= LineStart, lineHeightAdvance ??= LineHeightAdvance, room, start, atEnd);
    }

    // the delegates over the current layout, made once (a method group converted per call is a new object each time)
    Func<int, (double Height, double Advance)> lineHeightAdvance;
    Func<int, int> lineStart;

    int LineStart(int i) => layout[i].Start;

    /// <summary>A layout line's client height and advance (the metrics <see cref="FirstLineFor"/> and <see cref="FitLines"/> measure
    /// lines by). Reads the layout field, not <see cref="Layout"/>: that rebuilds its key from the label's properties (a copy of
    /// the whole text) on every call, and both callers have just read it.</summary>
    (double Height, double Advance) LineHeightAdvance(int i)
    {
        var line = layout[i];
        var (_, height, advance) = ClientLine(line.Start, line.End, cpCount, sizeAt ??= SizeAt);
        return (height, advance);
    }

    /// <summary>True when the glyph is past the label's visible character count.</summary>
    public bool Hidden(in PlacedGlyph g) => Label.VisibleCharacters >= 0 && g.Index >= Label.VisibleCharacters;

    Func<int, float> sizeAt, fieldSize;

    /// <summary>
    /// The selection's highlight in label pixels, as TextLayout builds it (sfr_render_04d.cpp:625): each selected glyph's
    /// rectangle, its pen to its advance across and its line's top (the baseline less BaseLineOffset, MaxFontAscent) to the
    /// line's height plus its leading when that is not negative down, joined while the glyphs follow one another (the
    /// highlight colour is the same throughout). Shown lines only, without the glyphs past the visible characters.
    /// </summary>
    public List<Rect2> SelectionRects()
    {
        var res = new List<Rect2>();
        if (!HasSelection || string.IsNullOrEmpty(LabelText)) return res;
        sizeAt ??= SizeAt;
        foreach (var line in ShownLines())
        {
            var (asc, height, advance) = ClientLine(line.Start, line.End, cpCount, sizeAt);
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
        var placed = new PlacedGlyph[glyphs.Count];
        int n = 0;
        foreach (Dictionary g in glyphs)
        {
            using var _ = g;
            int index = first + (int)g[GlyphKeys.Start], at = Math.Min(index, cpCount - 1), repeat = (int)g[GlyphKeys.Repeat];
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
            placed[n++] = new PlacedGlyph(index, count, (Rid)g[GlyphKeys.FontRid], id, raster, labelSize / raster,
                pen, (Vector2)g[GlyphKeys.Offset] * k, advance * k, repeat, labelSize)
            {
                End = first + (int)g[GlyphKeys.End],
                Bounded = count > 0 && (image || id != 0 || (gflags & TextServer.GraphemeFlag.Space) != 0),
            };
            pen += advance * k * repeat;
            shaped += advance * repeat;
        }
        line.Shaped = placed;
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
            var (_, h, adv) = ClientLine(lines[i].Start, lines[i].End, cpCount, sizeAt ??= SizeAt);
            var (_, fh, fadv) = ClientLine(lines[i].Start, lines[i].End, cpCount, fieldSize ??= _ => FieldSize, false);
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
    int cpCount;
    List<int> utf16 = new();

    /// <summary>The text of the code points [a, b).</summary>
    string Substring(int a, int b)
    {
        string t = LabelText;
        int i = a < utf16.Count ? utf16[a] : t.Length, j = b < utf16.Count ? utf16[b] : t.Length;
        return t.Substring(i, j - i);
    }

    int CodePointAt(int cp) => cp >= 0 && cp < cpCount ? codePoints[cp] : 0;

    /// <summary>Alg::IRound.</summary>
    static float IRound(float v) => MathF.Floor(v + 0.5f);

    /// <summary>The font's ascent per pixel of font size (DefineFont layout).</summary>
    public float AscentRatio;
}
