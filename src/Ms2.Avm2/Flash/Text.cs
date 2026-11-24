using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ms2.Avm2.Flash;

/// <summary>Measures text for autoSize, textWidth and line metrics. The host can plug in its real font metrics.</summary>
public static class TextMeasure
{
    /// <summary>(font class or name, size, line) → width in pixels.</summary>
    public static Func<string, double, string, double> Width = (font, size, s) => s.Sum(ch => ch < 0x2e80 ? (ch == ' ' ? 0.3 : char.IsUpper(ch) ? 0.62 : 0.52) : 1.0) * size;
    /// <summary>
    /// (font class or name) → the font's ascent + descent per pixel of font size, when the host knows the font's layout.
    /// A line is then IRound(ascent + descent) twips tall (Scaleform's FinalizeLine); without it, a whole-pixel 1.2 em.
    /// </summary>
    public static Func<string, double> HeightRatio;
    /// <summary>(font class or name) → the font's ascent per pixel of font size (with <see cref="HeightRatio"/>, the line
    /// metrics of a line with an IMG char).</summary>
    public static Func<string, double> AscentRatio;
    public static double LineHeight(string font, double size, double leading) =>
        (HeightRatio == null ? Math.Round(size * 1.2) : Math.Round(HeightRatio(font) * size * 20) / 20) + leading;
}

[AsClass("flash.text.TextFormat")]
public class AsTextFormat : AsObject
{
    [As] public object font { get; set; }
    [As] public object size { get; set; }
    [As] public object color { get; set; }
    [As] public object bold { get; set; }
    [As] public object italic { get; set; }
    [As] public object underline { get; set; }
    [As] public object url { get; set; }
    [As] public object target { get; set; }
    [As] public object align { get; set; }
    [As] public object leftMargin { get; set; }
    [As] public object rightMargin { get; set; }
    [As] public object indent { get; set; }
    [As] public object leading { get; set; }
    [As] public object blockIndent { get; set; }
    [As] public object bullet { get; set; }
    [As] public object kerning { get; set; }
    [As] public object letterSpacing { get; set; }
    [As] public object tabStops { get; set; }
    [As] public object display { get; set; }
    [AsCtor]
    void Ctor(object[] a)
    {
        object A(int i) => a.Length > i && !Rt.IsNullish(a[i]) ? a[i] : null;
        font = A(0); size = A(1); color = A(2); bold = A(3); italic = A(4); underline = A(5); url = A(6); target = A(7); align = A(8);
        leftMargin = A(9); rightMargin = A(10); indent = A(11); leading = A(12);
    }
    public AsTextFormat Copy(Runtime rt)
    {
        var f = (AsTextFormat)rt.GetClass("flash.text.TextFormat").Construct();
        f.MergeFrom(this); return f;
    }
    public void MergeFrom(AsTextFormat o)
    {
        if (o == null) return;
        font = o.font ?? font; size = o.size ?? size; color = o.color ?? color; bold = o.bold ?? bold; italic = o.italic ?? italic;
        underline = o.underline ?? underline; url = o.url ?? url; target = o.target ?? target; align = o.align ?? align;
        leftMargin = o.leftMargin ?? leftMargin; rightMargin = o.rightMargin ?? rightMargin; indent = o.indent ?? indent; leading = o.leading ?? leading;
        letterSpacing = o.letterSpacing ?? letterSpacing; kerning = o.kerning ?? kerning; blockIndent = o.blockIndent ?? blockIndent;
    }
}

[AsClass("flash.text.TextLineMetrics")]
public class AsTextLineMetrics : AsObject
{
    [As] public double x { get; set; }
    [As] public double width { get; set; }
    [As] public double height { get; set; }
    [As] public double ascent { get; set; }
    [As] public double descent { get; set; }
    [As] public double leading { get; set; }
}

/// <summary>
/// A text field: plain or simple HTML text, autoSize, input (typed by the player's key handling), selection and scroll state.
/// The box comes from the symbol's bounds (or 100x100 for one made by code); width/height resize the box, not the scale.
/// </summary>
[AsClass("flash.text.TextField", "flash.display.InteractiveObject")]
public class AsTextField : AsInteractiveObject
{
    // what the renderer reads is set through marking properties (AsDisplayObject.MarkChanged)
    string plainText = "", html, verticalAlign = "none", textAutoSize = "none";
    bool isHtml;
    RectD box = new(0, 0, 100, 100);
    AsTextFormat format;
    int version;
    public string Text { get => plainText; set { if (plainText != value) { plainText = value; MarkChanged(); } } }
    /// <summary>The last htmlText assigned (kept for the renderer).</summary>
    public string Html { get => html; set { if (html != value) { html = value; MarkChanged(); } } }
    public bool IsHtml { get => isHtml; set { if (isHtml != value) { isHtml = value; MarkChanged(); } } }
    /// <summary>The view rectangle. A pending autoSize / wordWrap change resizes it at the first read, as Scaleform's
    /// setters only set the document's AutoSizeX / AutoSizeY flags and ask for a reformat (autoSizeSet, wordWrapSet:
    /// UpdateAutosizeSettings, sfr_as3_13.cpp:2640 / :2249; AutoSizeX only without wordWrap): "autoSize = LEFT" then
    /// "wordWrap = true" on an empty field keeps its width.</summary>
    public RectD Box
    {
        get { if (autoSizePending) { autoSizePending = false; if (autoSizeMode != "none") AutoSize(); } return box; }
        set { autoSizePending = false; if (!box.Equals(value)) { box = value; MarkChanged(); WatchScroll(); } }
    }
    bool autoSizePending;
    public AsTextFormat Format { get => format; set { if (format != value) { format = value; MarkChanged(); } } }
    public string Variable;
    public int SelBegin, SelEnd;
    /// <summary>Bumped on each change (renderer refresh).</summary>
    public int Version { get => version; set { version = value; MarkChanged(); WatchScroll(); } }
    int boxBumps;
    /// <summary><see cref="Version"/> less the bumps of a width / height set: what changes when the text or its format does, not
    /// when only the box does. A renderer re-sets its text from this one; a new box is placed over the text it holds (the client's
    /// TextField::SetViewRect formats the document again, it does not parse it again).</summary>
    public int TextVersion => version - boxBumps;
    public string VerticalAlign { get => verticalAlign; set { if (verticalAlign != value) { verticalAlign = value; MarkChanged(); } } }
    public string TextAutoSize { get => textAutoSize; set { if (textAutoSize != value) { textAutoSize = value; MarkChanged(); } } }

    Runtime R => Class.Runtime;

    [AsCtor] void Ctor(object[] a) { }

    /// <summary>Set up from a text symbol of the layout.</summary>
    public void FromSymbol(JsonObject sym)
    {
        if (sym?["bounds"] is JsonObject b) Box = SymbolScope.Rect(b);
        var t = sym?["text"] as JsonObject;
        Format = (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct();
        if (t == null) { LoadGeom(); return; }
        Format.font = (string)t["fontClass"] ?? (string)t["font"];
        Format.size = Rt.Num(J.D(t["size"]) ?? 12);
        if ((string)t["color"] is string c) Format.color = Rt.Uint(Convert.ToUInt32(c.TrimStart('#')[..6], 16));
        Format.align = (string)t["align"] ?? "left";
        antiAliasType = (bool?)t["readable"] == true ? "advanced" : "normal";
        Format.leftMargin = Rt.Num(J.D(t["leftMargin"]) ?? 0);
        Format.rightMargin = Rt.Num(J.D(t["rightMargin"]) ?? 0);
        Format.indent = Rt.Num(J.D(t["indent"]) ?? 0);
        Format.leading = Rt.Num(J.D(t["leading"]) ?? 0);
        wordWrap = (bool?)t["wordWrap"] ?? false;
        multiline = (bool?)t["multiline"] ?? false;
        displayAsPassword = (bool?)t["password"] ?? false;
        type = (bool?)t["readOnly"] ?? true ? "dynamic" : "input";
        selectable = (bool?)t["selectable"] ?? true;
        maxChars = J.I(t["maxLength"]) ?? 0;
        border = (bool?)t["border"] ?? false;
        autoSizeMode = (bool?)t["autoSize"] ?? false ? "left" : "none";
        IsHtml = (bool?)t["html"] ?? false;
        Variable = (string)t["variable"];
        var init = (string)t["initial"] ?? "";
        if (IsHtml || Tr(init) != init) SetHtml(Tr(init)); else Text = init;
        LoadGeom();
    }

    /// <summary>A placed field takes its position once its text is in: TextField::OnEventLoad forces the geometry data
    /// after SetTextValue and Format when the field has none (sfr_gfx_03e.cpp:1000-1010). The placement matrix is
    /// applied before this runs (ConstructInto).</summary>
    void LoadGeom() { if (geomX == null) UpdateGeom(force: true); }

    /// <summary>Scaleform's translator: a text that is a string-table key ("$s_...") shows the table's value.</summary>
    string Tr(string s) => s.Length > 1 && s[0] == '$' && Class.Runtime.Player?.Translate is { } t ? t(s) ?? s : s;

    double Size => Format?.size is { } s && !Rt.IsNullish(s) ? Rt.ToNumber(s) : 12;
    string FontName => Format?.font is string f ? f : "";
    public double Leading => Format?.leading is { } l && !Rt.IsNullish(l) ? Rt.ToNumber(l) : 0;

    void Changed() { Version++; if (autoSizeMode != "none") AutoSize(); }

    /// <summary>The line texts. Kept while the spans are: the AS-visible line calls (getLineText, getLineLength, getLineOffset ...)
    /// ask for them one line at a time, so a log of 1000 lines was copied into a new list for each of ~150 calls per appended line.
    /// Callers read the list only.</summary>
    public List<string> Lines()
    {
        var sp = Spans();
        if (!ReferenceEquals(sp, linesOf) || lineTexts == null)
        {
            var l = new List<string>(sp.Count);
            foreach (var x in sp) l.Add(x.Line);
            lineTexts = l; linesOf = sp;
        }
        return lineTexts;
    }
    List<string> lineTexts;
    List<(string Line, int Start)> linesOf;

    /// <summary>The lines and the index in <see cref="Text"/> of each line's first character. Kept for what they were made from
    /// (the text, the width, the font and the wrap): the AS-visible metrics (numLines, maxScrollV, scrollV) ask for
    /// them again and again, a wheel notch several times. A field with IMG chars is laid out each time (an image's size can
    /// arrive after the text did). Callers read the list only.</summary>
    List<(string Line, int Start)> Spans()
    {
        var key = (Text, Box.W, wordWrapFlag, displayAsPassword, FontName, Size, Leading);
        if (spans != null && key == spansKey && !HasImages) return spans;
        spansKey = key;
        return spans = MakeSpans();
    }

    List<(string Line, int Start)> spans;
    (string, double, bool, bool, string, double, double) spansKey;

    List<(string Line, int Start)> MakeSpans()
    {
        // CR LF and LF are paragraph ends as CR; map: index in that text -> index in Text
        string raw = displayAsPassword ? new string('*', Text.Length) : Text;
        // a text with no LF has none to fold: it is its own paragraph text and every index maps to itself (no map, no copy)
        List<int> map = null;
        string src = raw;
        if (raw.Contains('\n'))
        {
            var sb = new StringBuilder(raw.Length);
            map = new List<int>(raw.Length + 1);
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\n' && i > 0 && raw[i - 1] == '\r') continue;
                sb.Append(raw[i] == '\n' ? '\r' : raw[i]);
                map.Add(i);
            }
            map.Add(raw.Length);
            src = sb.ToString();
        }
        int Map(int i) => map == null ? i : map[i];
        var lines = new List<(string, int)>();
        double max = Box.W - 4;
        int pstart = 0;
        // a paragraph the last text held, at the same width, font, size and leading, breaks into the same lines (DocView::Format
        // keeps the lines of a paragraph that did not change and lays out only the new and the changed ones, sfr_render_03b.cpp:2282):
        // its lines are kept with their offsets from the paragraph's start. A field with IMG chars is laid out each time (the
        // width of a char depends on where it is).
        var wrapKey = (Box.W, wordWrap, FontName, Size, Leading);
        bool keep = wordWrap && max > 0 && !HasImages;
        if (!keep || wrapKey != paragraphKey) { laidParagraphs.Clear(); paragraphKey = wrapKey; }
        laidParagraphsNext.Clear();
        foreach (var p in src.Split('\r'))
        {
            if (!wordWrap || max <= 0) { lines.Add((p, Map(pstart))); pstart += p.Length + 1; continue; }
            int first = lines.Count, origin = Map(pstart);
            if (keep && p.Length > 0 && (laidParagraphs.TryGetValue(p, out var laid) || laidParagraphsNext.TryGetValue(p, out laid)))
            {
                foreach (var (line, rel) in laid) lines.Add((line, origin + rel));
                laidParagraphsNext[p] = laid;
                pstart += p.Length + 1;
                continue;
            }
            var cur = new StringBuilder();
            int curStart = pstart;
            // Breaks after a space or a hyphen, and around CJK chars. ParagraphFormatter::CheckWordWrap
            // (sfr_render_03b.cpp:1918 @0x14260e120): a non-space char that overflows the line goes back to the last
            // break point; with none on the line, the line ends before that char, so a word wider than the box breaks
            // inside the word ("Characte/r"), keeping at least one char per line.
            foreach (var part in Regex.Split(p, @"(?<=[\s\-])|(?<=[⺀-￿])(?=.)|(?<=.)(?=[⺀-￿])"))
            {
                var word = part;
                if (cur.Length > 0 && LineWidth(cur + word.TrimEnd(), Map(curStart)) > max)
                {
                    lines.Add((cur.ToString(), Map(curStart)));
                    curStart += cur.Length;
                    cur.Clear();
                }
                while (cur.Length == 0 && word.TrimEnd().Length > 1 && LineWidth(word.TrimEnd(), Map(curStart)) > max)
                {
                    int k = 1;
                    while (k < word.Length && !char.IsWhiteSpace(word[k]) && LineWidth(word[..(k + 1)], Map(curStart)) <= max) k++;
                    lines.Add((word[..k], Map(curStart)));
                    curStart += k;
                    word = word[k..];
                }
                cur.Append(word);
            }
            lines.Add((cur.ToString(), Map(curStart)));
            if (keep && p.Length > 0)
            {
                var made = new List<(string Line, int Rel)>(lines.Count - first);
                for (int i = first; i < lines.Count; i++) made.Add((lines[i].Item1, lines[i].Item2 - origin));
                laidParagraphsNext[p] = made;
            }
            pstart += p.Length + 1;
        }
        (laidParagraphs, laidParagraphsNext) = (laidParagraphsNext, laidParagraphs);
        laidParagraphsNext.Clear();
        return lines;
    }

    /// <summary>The lines of each paragraph of the last text (by its text), with their offsets from the paragraph's start;
    /// <see cref="paragraphKey"/>: the width, wrap, font, size and leading they were broken at.</summary>
    Dictionary<string, List<(string Line, int Rel)>> laidParagraphs = new(), laidParagraphsNext = new();
    (double, bool, string, double, double) paragraphKey;

    /// <summary>
    /// An IMG char's width, height and ascent in pixels as the client lays it out: TextField::ProcessImageTags
    /// (sfr_gfx_03e.cpp:1441) sizes it by its WIDTH / HEIGHT, else the exported image's (looked up from the field's movie,
    /// <see cref="Player.ExportedImageSize"/>), and moves it up by the image's height + VSPACE scaled to that height; a url
    /// that names no image keeps the tag's size and no move. Null for a text char.
    /// </summary>
    (double W, double H, double Ascent)? ImageAt(int textIndex)
    {
        if (Formats is not { } f || textIndex < 0 || textIndex >= f.Length || f[textIndex].Image is not HtmlImage t) return null;
        var player = Class.Runtime.Player;
        if (player?.ExportedImageSize?.Invoke(Scope?.Key ?? player.Main?.Key, t.Src) is { } size && size.W > 0 && size.H > 0)
        {
            double w = t.Width > 0 ? t.Width : size.W, h = t.Height > 0 ? t.Height : size.H;
            return (w, h, (size.H + t.VSpace) * (h / size.H));
        }
        return (t.Width, t.Height, 0);
    }

    bool HasImages => Formats is { } f && Array.Exists(f, x => x.Image != null);

    /// <summary>
    /// A line's width: the font's advances of its text chars and, for each IMG char, its width plus 40 twips
    /// (ParagraphFormatter, sfr_render_03b.cpp:2124: GlyphAdvance = GlyphWidth + 40).
    /// </summary>
    double LineWidth(string line, int textStart)
    {
        if (!HasImages) return TextMeasure.Width(FontName, Size, line);
        var text = new StringBuilder(line.Length);
        double images = 0;
        for (int i = 0; i < line.Length; i++)
            if (ImageAt(textStart + i) is { } img) images += img.W + 2;
            else text.Append(line[i]);
        return TextMeasure.Width(FontName, Size, text.ToString()) + images;
    }

    /// <summary>Minimum font scale a single-line caption shrinks to before it is cut with an ellipsis.</summary>
    public const double MinFitScale = 0.8;

    /// <summary>
    /// What the renderer draws: the lines and a font scale. A single-line caption (no wordWrap, no multiline, no autoSize)
    /// whose text is wider than its box shrinks to fit down to <see cref="MinFitScale"/>, then is cut with an ellipsis.
    /// The AS-visible metrics (textWidth, numLines, ...) are not affected.
    /// </summary>
    public (List<string> Lines, double Scale) Fitted()
    {
        var lines = Lines();
        if (wordWrap || multiline || autoSizeMode != "none" || lines.Count != 1 || type == "input") return (lines, 1);
        double avail = Box.W - 4, w = TextMeasure.Width(FontName, Size, lines[0]);
        if (avail <= 0 || w <= avail) return (lines, 1);
        double sc = Math.Max(MinFitScale, avail / w);
        if (w * sc <= avail + 0.01) return (lines, sc);
        var t = lines[0];
        while (t.Length > 0 && TextMeasure.Width(FontName, Size * sc, t + "…") > avail) t = t[..^1].TrimEnd();
        return (new List<string> { t + "…" }, sc);
    }

    /// <summary>
    /// The top of the first drawn line of <paramref name="lineCount"/> lines, in field units: DocView::Format's vertical
    /// alignment (sfr_render_03b.cpp:2453-2556). With autoSize the view follows the text, so the text starts at the top
    /// gutter. Otherwise a single-line field with a text auto size (TextFieldEx.setTextAutoSize) and no vertical alignment
    /// is centred, and a centred or bottom-aligned text is placed in the view less its 2 px gutters: the text's height
    /// (whole pixels) when it fits, else the lines that fit counted up from the bottom scroll line.
    /// </summary>
    /// <summary>
    /// DocView::Format's text auto size (sfr_render_03b.cpp:2450-2493): the scale applied to the laid out lines (LineBuffer::Scale)
    /// of a text <paramref name="textW"/> x <paramref name="textH"/> in a view (the field less its 2 px gutters) of
    /// <paramref name="viewW"/> x <paramref name="viewH"/>. "shrink": the smaller of view / text on each axis where the text
    /// is larger; "fit": the smaller of view / text on each axis where the text is at least 20 wide or tall. A scale that
    /// puts the shortest line (<paramref name="minLineH"/>) under 6 px is raised to 6 px (GFX_MIN_LINE_HEIGHT_FOR_AUTOSIZE, :1207).
    /// </summary>
    public static double TextAutoSizeScale(string tas, double textW, double textH, double viewW, double viewH, double minLineH)
    {
        double scale = 1;
        if (tas == "shrink")
        {
            double hs = textW > viewW ? viewW / textW : 1, vs = textH > viewH ? viewH / textH : 1;
            scale = Math.Min(vs, hs);
        }
        else if (tas == "fit")
        {
            double hs = textW >= 20 ? viewW / textW : double.MaxValue, vs = textH >= 20 ? viewH / textH : double.MaxValue;
            scale = Math.Min(vs, hs);
            if (scale == double.MaxValue) scale = 1;
        }
        if (scale != 1 && minLineH > 0 && minLineH * scale < 6) scale = 6 / minLineH;
        return scale;
    }

    public double FirstLineTop(int lineCount, double scale = 1)
    {
        double top = Box.Y + 2, maxH = Box.H - 4;
        if (autoSizeMode != "none") return top;
        string valign = VerticalAlign;
        if (TextAutoSize != "none" && !multiline && valign == "none") valign = "center";
        if (valign is not ("center" or "bottom")) return top;
        // the lines as laid out, times the text auto size's scale (LineBuffer::Scale)
        double line = TextMeasure.LineHeight(FontName, Size, 0) * scale, lead = Leading * scale;
        double textH = lineCount == 0 ? 0 : Math.Floor(lineCount * (line + lead) - lead);
        // lines with IMG chars have their own heights (ImageTextH)
        if (lineCount > 0 && HasImages && TextMeasure.HeightRatio != null && TextMeasure.AscentRatio != null && lineCount == Spans().Count)
            textH = Math.Floor(ImageTextH() * scale);
        if (textH < maxH) return valign == "center" ? top + (maxH / 2 - textH / 2) : top + maxH - textH;
        double vis = 0;
        for (int i = 0; i < lineCount; i++)
        {
            double lh = line + (i != 0 ? lead : 0);
            if (vis + lh <= maxH) vis += lh; else break;
        }
        return valign == "bottom" ? top + maxH - vis : top + (maxH / 2 - vis / 2);
    }

    public string FontClass => FontName;
    public double FontSize => Size;

    public double TextW() => Spans().Select(l => LineWidth(l.Line, l.Start)).DefaultIfEmpty(0).Max();
    public double TextH()
    {
        if (Text.Length == 0) return 0;
        if (HasImages && TextMeasure.HeightRatio != null && TextMeasure.AscentRatio != null) return ImageTextH();
        var n = Lines().Count;
        return n * TextMeasure.LineHeight(FontName, Size, Leading) - Leading;
    }

    /// <summary>
    /// textHeight of a text with IMG chars (DocView::Format: the last line's offset plus its height), each line by
    /// FinalizeLine (sfr_render_03b.cpp:1743): IRound(MaxFontAscent + MaxFontDescent) twips tall, the next IRound(height +
    /// IRound(leading)) below. A text char tracks the font's ascent and descent; an IMG char (:2150) raises the ascent to
    /// its own and the descent to its height less that; an empty line takes the font's.
    /// </summary>
    double ImageTextH()
    {
        var l = ImageLines();
        return l.Count == 0 ? 0 : (l[^1].Top + l[^1].H) / 20;
    }

    /// <summary>Each line's top and height in twips by FinalizeLine (see ImageTextH).</summary>
    List<(double Top, double H)> ImageLines()
    {
        double asc = TextMeasure.AscentRatio(FontName) * Size * 20, desc = (TextMeasure.HeightRatio(FontName) - TextMeasure.AscentRatio(FontName)) * Size * 20;
        static double IRound(double v) => Math.Floor(v + 0.5);
        double leading = IRound(Leading * 20), offset = 0, height = 0;
        bool first = true;
        var res = new List<(double Top, double H)>();
        foreach (var (line, start) in Spans())
        {
            double maxAsc = 0, maxDesc = 0;
            bool font = line.Length == 0;
            for (int i = 0; i < line.Length; i++)
                if (ImageAt(start + i) is { } img) { maxAsc = Math.Max(maxAsc, img.Ascent * 20); maxDesc = Math.Max(maxDesc, (img.H - img.Ascent) * 20); }
                else font = true;
            if (font) { maxAsc = Math.Max(maxAsc, asc); maxDesc = Math.Max(maxDesc, desc); }
            if (!first) offset += IRound(height + leading);
            first = false;
            height = IRound(maxAsc + maxDesc);
            res.Add((offset, height));
        }
        return res;
    }

    void AutoSize()
    {
        double w = wordWrap ? Box.W : TextW() + 4, h = TextH() + 4;
        double x = Box.X;
        if (!wordWrap)
        {
            if (autoSizeMode == "center") x = Box.X + (Box.W - w) / 2;
            else if (autoSizeMode == "right") x = Box.X + Box.W - w;
        }
        Box = new RectD(x, Box.Y, w, h);
    }

    /// <summary>
    /// The htmlText setter's document (TextField::htmlTextSet, sfr_as3_13.cpp:3659: the html flag on, then the text parsed
    /// with StyledText::ParseHtml, <see cref="HtmlText.Parse"/>): <see cref="Text"/> and each char's format. A line break
    /// the end of the html's last P makes is not kept in Text; <see cref="GetHtml"/> still writes its paragraph.
    /// </summary>
    public void SetHtml(string html) => SetHtml(html, true);

    /// <summary><paramref name="translate"/>: TextField::SetTextValue's translation (sfr_gfx_03f.cpp:601). The client's
    /// FontTranslator has no GetCaps override (caps 0, no Cap_ReceiveHtml), so an html text is parsed and its plain text
    /// is the key (the movie's "&lt;p&gt;&lt;font ...&gt;$s_key&lt;/font&gt;&lt;/p&gt;" initial texts); the result is
    /// html parsed over the field's initial formats, not the key's markup (GetInitialFormats + ParseHtml).</summary>
    void SetHtml(string html, bool translate)
    {
        Html = html ?? "";
        IsHtml = true;
        var src = translate ? Tr(Html) : Html;
        var parser = new HtmlText.Parser(multiline, '\r', false, true, src.Length);
        parser.Feed(src);
        bool endBreak = parser.Length > 0 && parser.At(parser.Length - 1) == '\r' && EndsParagraph(Html);
        string s = parser.Text(endBreak ? 1 : 0);
        if (translate && src == Html && s != Html && Tr(s) is var t && t != s) { SetHtml(t, false); return; }
        Text = s;
        SetFormats(parser.Formats, parser.Styled, endBreak);
        htmlParser = translate && src == Html ? parser : null;      // what appendHtml goes on from
        htmlParsed = Html;
    }

    /// <summary>The html ends with an end P and white space only (the line break its end makes is not kept in the text).</summary>
    static bool EndsParagraph(string html)
    {
        var h = html.AsSpan().TrimEnd();
        return h.EndsWith("</p>", StringComparison.OrdinalIgnoreCase);
    }

    HtmlText.Parser htmlParser;
    string htmlParsed;      // the html the parser holds: Html while nothing else has set the text since

    /// <summary>
    /// TextFieldEx.appendHtml: the html is parsed on to the document (DocView::AppendHtml, sfr_render_03b.cpp:277 / :287, calls
    /// StyledText::ParseHtml on the appended string only), so the cost does not grow with the text already there. The elements
    /// the text left open and the format around them carry over, as they do when the whole html is parsed again (what this does
    /// when the field's text did not come from an html text set here, or the end of it could join the appended html).
    /// </summary>
    public void AppendHtml(string add)
    {
        add ??= "";
        var p = htmlParser;
        if (p != null && p.Continuable && IsHtml && ReferenceEquals(htmlParsed, Html) && ReferenceEquals(formatsText, Text) && Html.Length > 0 && Html[0] != '$')
        {
            var whole = Html + add;
            p.Feed(add);
            bool endBreak = p.Length > 0 && p.At(p.Length - 1) == '\r' && EndsParagraph(whole);
            if (p.Length - (endBreak ? 1 : 0) is var n && (n < 2 || p.At(0) != '$'))
            {
                string s = p.Text(endBreak ? 1 : 0);
                Html = whole;
                Text = s;
                SetFormats(p.Formats, p.Styled, endBreak);
                htmlParsed = Html;
                return;
            }
            htmlParser = null;
            SetHtml(whole);
            return;
        }
        SetHtml((Html ?? (Formats != null ? GetHtml() : System.Net.WebUtility.HtmlEncode(Text))) + add);
    }

    /// <summary>The chars' html formats (<see cref="SetHtml"/>), for <see cref="Text"/> while it is the html's text;
    /// <see cref="formatsBreak"/>: the dropped line break the last format is for. The array may be longer than the text (the
    /// parser's, which grows in place as html is appended): what it holds past the text is the field's own format.</summary>
    HtmlFormat[] formats;
    string formatsText;
    bool formatsBreak;

    void SetFormats(HtmlFormat[] f, bool endBreak) => SetFormats(f, f != null && Array.Exists(f, x => !x.IsPlain), endBreak);

    void SetFormats(HtmlFormat[] f, bool styled, bool endBreak)
    {
        formats = f != null && styled ? f : null;
        formatsText = Text;
        formatsBreak = endBreak;
    }

    HtmlFormat[] Formats => formats != null && ReferenceEquals(formatsText, Text) ? formats : null;

    /// <summary>
    /// StyledText::GetHtml (sfr_render_01c.cpp:1308, <see cref="HtmlText.Build"/>) of the field's document: the text in its
    /// html formats over the field's default text and paragraph format (TextField::SetTextFieldDef, sfr_gfx_03e.cpp:1413).
    /// </summary>
    public string GetHtml()
    {
        bool own = ReferenceEquals(formatsText, Text);
        var doc = own && formatsBreak ? Text + "\r" : Text;
        double? Opt(object v) => v is { } x && !Rt.IsNullish(x) && Rt.ToNumber(x) != 0 ? Rt.ToNumber(x) : null;
        var d = new HtmlText.Defaults(FontName, Size, textColor, Format?.align as string ?? "left",
            Opt(Format?.leftMargin), Opt(Format?.rightMargin), Opt(Format?.indent), Opt(Format?.leading));
        return HtmlText.Build(doc, Formats, d);
    }

    /// <summary>The html the renderer draws the text from: the last html set, or, once the chars' formats were edited
    /// (setTextFormat on a range, replaceText), the html those formats make; null for text with no formats.</summary>
    public string RenderHtml => Html ?? (Formats != null ? GetHtml() : null);

    public override RectD LocalBounds() => Box;

    /// <summary>The geometry data's position, in twips: null until the field has geometry data
    /// (DisplayObjectBase::pGeomData). needGeom is Flags_NeedUpdateGeomData (0x2000). A field the script makes
    /// starts with it set (TextField::InitInstance calls SetWidth / SetHeight and SetTextValue, sfr_as3_13.cpp:1217).
    /// Setting text or htmlText sets it (TextField::SetTextValue, sfr_gfx_03f.cpp:771), and so do width, height, scaleX,
    /// scaleY, rotation (:797 / :369 / :882 / :911 / :422), autoSize and wordWrap (sfr_as3_13.cpp:2158 / :2653).
    /// appendText, replaceText, the formats and a timeline move do not, so x / y keep their last value after those.
    /// STILL-OPEN: SetAcceptAnimMoves(true) with the movie's continueAnimation flag drops the geometry data
    /// (sfr_gfx_06.cpp:1001), so x / y would read the matrix origin again. Not modelled.</summary>
    int? geomX, geomY;
    bool needGeom = true;
    internal bool NeedGeom { get => needGeom; set => needGeom = value; }

    /// <summary>TextField::UpdateAndGetGeomData (@0x1425beb10, sfr_gfx_03f.cpp:949) gets the stored geometry data, or,
    /// with none, the matrix origin truncated to twips (DisplayObjectBase::GetGeomData, sfr_gfx_06.cpp:447). Forced, or
    /// with Flags_NeedUpdateGeomData set, it stores the matrix applied to the view rect's top-left, rounded to twips,
    /// and clears the flag. Reading the view rect first runs a pending reformat (DocView::GetViewRect).</summary>
    (int X, int Y) UpdateGeom(bool force = false)
    {
        if (force || needGeom)
        {
            var b = Box; var m = Matrix;
            geomX = IRound((m.A * b.X + m.C * b.Y + m.Tx) * 20);
            geomY = IRound((m.B * b.X + m.D * b.Y + m.Ty) * 20);
            needGeom = false;
        }
        return geomX is int x ? (x, geomY.Value) : ((int)(float)(Matrix.Tx * 20), (int)(float)(Matrix.Ty * 20));
    }
    /// <summary>Alg::IRound (sf_alg.h:20): halves round away from zero.</summary>
    static int IRound(double a) => a > 0.0 ? (int)(a + 0.5) : (int)(a - 0.5);

    /// <summary>The matrix or matrix3D setter stores the new origin, truncated to twips, as the position and leaves
    /// the flag as it was (Transform::matrixSet / matrix3DSet, sfr_as3_11.cpp:1815 / :2006).</summary>
    internal void StoreGeomOrigin() { geomX = (int)(float)(Matrix.Tx * 20); geomY = (int)(float)(Matrix.Ty * 20); }
    /// <summary>DisplayObjectBase::SetX / SetY call SetAcceptAnimMoves(0), which makes geometry data from the matrix
    /// when there is none (sfr_gfx_06.cpp:1192, :978).</summary>
    void EnsureGeom() { if (geomX == null) StoreGeomOrigin(); }

    /// <summary>x / y are the geometry data's position: the text rectangle's top-left corner in the parent when it was
    /// last computed, not the matrix origin (TextField::GetX / GetY @0x1425b95f0 / @0x1425b9690, sfr_gfx_03e.cpp:760).</summary>
    protected override double GetX() => UpdateGeom().X / 20.0;
    protected override double GetY() => UpdateGeom().Y / 20.0;

    /// <summary>Puts the text rectangle's left side at x: the parent point (x, x) (Point(T v) sets both,
    /// render_types2d.h:32) is taken into the field's space, moved left by the view rect's x and taken back to the
    /// parent, which gives the new matrix origin's x. An infinite x is 0. The geometry data keeps x rounded to twips,
    /// and the flag is left as it was (TextField::SetX @0x1425be380, sfr_gfx_03f.cpp:861).</summary>
    protected override void SetX(double v)
    {
        if (double.IsInfinity(v)) v = 0;
        var m = Matrix;
        var (px, py) = m.Inverse().Apply(v, v);
        EnsureGeom();
        base.SetX(m.Apply(px - Box.X, py).X);
        geomX = IRound(v * 20);
    }

    /// <summary>Puts the text rectangle's top at y, from the point (0, y) (TextField::SetY @0x1425be670, sfr_gfx_03f.cpp:890).</summary>
    protected override void SetY(double v)
    {
        if (double.IsInfinity(v)) v = 0;
        var m = Matrix;
        var (px, py) = m.Inverse().Apply(0, v);
        EnsureGeom();
        base.SetY(m.Apply(px, py - Box.Y).Y);
        geomY = IRound(v * 20);
    }
    protected override void ScaledOrRotated() => needGeom = true;
    public override bool HitsContent(double gx, double gy) { var (lx, ly) = GlobalToLocal(gx, gy); return Box.Contains(lx, ly); }
    protected override bool DefaultTabEnabled => type == "input";

    /// <summary>A text that is a string-table key shows the table's value as HTML: the client's translator answers through
    /// TranslateInfo::SetResultHtml (FontTranslator::Translate @0x1422f9d10, fontconfig.cpp:315), whichever of text /
    /// htmlText was set.</summary>
    [As] public string text
    {
        get => Text;
        set
        {
            var v = value ?? "";
            var t = Tr(v);
            if (!ReferenceEquals(t, v) && t != v) SetHtml(t);
            else { Text = v; Html = null; IsHtml = false; }       // textSet (sfr_as3_13.cpp:2233): the html flag off
            Changed();
            needGeom = true;
        }
    }
    [As] public string htmlText
    {
        // htmlTextGet (sfr_as3_13.cpp:2187) -> TextField::GetText(true) (sfr_gfx_03e.cpp:814): an html field's document
        // as GetHtml writes it, else the text
        get => IsHtml ? GetHtml() : Text;
        set { SetHtml(value); Changed(); needGeom = true; }
    }
    /// <summary>TextField::appendText: the new chars take the format of the last char, the document keeps the rest's.</summary>
    [As] public void appendText(string s) => replaceText(Text.Length, Text.Length, s);
    /// <summary>TextField::replaceText (sfr_as3_13.cpp): the chars between b and e become s. The chars the document keeps
    /// keep their html formats and the new ones take the format of the char before them (the heading of a CommonTooltip
    /// status text stays yellow after its "gauge:" marker is cut out).</summary>
    [As] public void replaceText(int b, int e, string s)
    {
        b = Math.Clamp(b, 0, Text.Length); e = Math.Clamp(e, b, Text.Length); s ??= "";
        var f = Formats;
        var old = Text;
        Text = old[..b] + s + old[e..];
        if (f != null && f.Length >= old.Length)
        {
            var fill = b > 0 ? f[b - 1] : f.Length > 0 && b < f.Length ? f[b] : default;
            if (fill.Image != null) fill = fill with { Image = null };
            var nf = new HtmlFormat[Text.Length + (f.Length - old.Length)];
            Array.Copy(f, 0, nf, 0, b);
            for (int i = 0; i < s.Length; i++) nf[b + i] = fill;
            Array.Copy(f, e, nf, b + s.Length, f.Length - e);
            SetFormats(nf, formatsBreak);
        }
        Html = null; Changed();
    }
    [As] public void replaceSelectedText(string s) => replaceText(SelBegin, SelEnd, s);
    [As] public int length => Text.Length;
    [As] public double textWidth => TextW();
    [As] public double textHeight => TextH();
    [As] public int numLines => Math.Max(1, Lines().Count);
    [As] public string getLineText(int i) { var l = Lines(); return i >= 0 && i < l.Count ? l[i] : throw R.RangeError("Error #2006: The supplied index is out of bounds."); }
    // Scaleform's TextField::getLineLength / getLineOffset (sfr_as3_13.cpp:1347, :1363; DocView::GetLineLength / GetLineOffset,
    // sfr_render_03b.cpp:619, :609): a negative line or one past the last gives -1, never a RangeError
    [As] public int getLineLength(int i) { var l = Lines(); return i >= 0 && i < l.Count ? l[i].Length : -1; }
    [As] public int getLineOffset(int i) { var l = Lines(); if (i < 0 || i >= l.Count) return -1; int o = 0; for (int k = 0; k < i; k++) o += l[k].Length; return o; }
    [As] public int getLineIndexOfChar(int c) => c < 0 || c >= Text.Length ? -1 : LineOfChar(Spans(), c);
    [As] public int getLineIndexAtPoint(double x, double y) => Math.Clamp((int)((y - 2) / TextMeasure.LineHeight(FontName, Size, Leading)), 0, numLines - 1);
    [As] public int getCharIndexAtPoint(double x, double y) => -1;
    // DocView::GetCharBoundaries: the char's box in the field, on the line it is laid out on (the line's top is the lines
    // above it, each LineHeight + leading tall, below the 2 px gutter; its x the width of the line's text before it).
    [As] public AsRectangle getCharBoundaries(int i)
    {
        if (i < 0 || i >= Text.Length) return null;
        var spans = Spans();
        int k = LineOfChar(spans, i);
        double lh = TextMeasure.LineHeight(FontName, Size, 0);
        var (line, start) = spans[k];
        int col = Math.Clamp(i - start, 0, line.Length);
        double lineW = LineWidth(line, start);
        double x = 2 + LineWidth(line[..col], start);
        x += AlignOffset(lineW);
        double w = col < line.Length ? TextMeasure.Width(FontName, Size, line[col].ToString()) : TextMeasure.Width(FontName, Size, Text[i].ToString());
        // the first line sits at the vertical align's top, a line with IMG chars has its own height (ImageTextH)
        double top = FirstLineTop(spans.Count) - Box.Y, y = top + k * (lh + Leading);
        if (HasImages && TextMeasure.HeightRatio != null && TextMeasure.AscentRatio != null)
        {
            var il = ImageLines();
            y = top + il[k].Top / 20;
            lh = il[k].H / 20;
        }
        return AsRectangle.New(R, x, y, w, lh);
    }

    /// <summary>The field's paragraph align moves a line inside the box (the 2 px gutter either side); HtmlText.Build maps
    /// right / center / justify, anything else is left.</summary>
    double AlignOffset(double lineW)
    {
        double free = Math.Max(0, Box.W - 4 - lineW);
        return (Format?.align as string)?.ToLowerInvariant() switch { "center" => free / 2, "right" => free, _ => 0 };
    }

    static int LineOfChar(List<(string Line, int Start)> spans, int i)
    {
        int k = 0;
        for (int j = 0; j < spans.Count; j++) { if (spans[j].Start <= i) k = j; else break; }
        return k;
    }
    // TextField::getLineMetrics (sfr_as3_13.cpp:2025) from DocView::GetLineMetrics (sfr_render_03b.cpp): the line's height is
    // IRound(max ascent + max descent) (the formatter, sfr_render_03b.cpp:1665), its leading kept apart (SetLeading :1712)
    [As] public AsTextLineMetrics getLineMetrics(int i)
    {
        var m = (AsTextLineMetrics)R.GetClass("flash.text.TextLineMetrics").Construct();
        var l = Spans(); var (s, start) = i >= 0 && i < l.Count ? l[i] : ("", 0);
        double w = LineWidth(s, start);
        m.width = w; m.height = TextMeasure.LineHeight(FontName, Size, 0);
        m.ascent = Size * 0.9; m.descent = Size * 0.25; m.leading = Leading;
        m.x = 2 + AlignOffset(w);       // FirstCharXOff: the gutter and the align's shift (the same x getCharBoundaries starts a line at)
        return m;
    }

    /// <summary>The paragraph of char <paramref name="c"/>: where it starts and how long it is, its end mark kept in the length
    /// (StyledText paragraphs hold their closing line break, so the lengths add up to <see cref="Text"/>'s). A char index
    /// one past the text is the last paragraph's; further out there is none (DocView::GetParagraphByIndex finishes).</summary>
    (int Start, int Length)? ParagraphOf(int c)
    {
        var t = Text;
        if (c < 0 || c > t.Length) return null;
        int start = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] != '\r' && t[i] != '\n') continue;
            int end = i + 1;
            if (t[i] == '\r' && end < t.Length && t[end] == '\n') { end++; i++; }
            if (c < end) return (start, end - start);
            start = end;
        }
        return (start, t.Length - start);
    }
    // TextField::getFirstCharInParagraph (sfr_as3_13.cpp:1303: -1 for a negative index or none) from
    // DocView::GetFirstCharInParagraph (sfr_render_03b.cpp:688); getParagraphLength (:1394) from DocView::GetParagraphLength
    // (:656, -1 when no paragraph holds the index)
    [As] public int getFirstCharInParagraph(int c) => c >= 0 && ParagraphOf(c) is { } p ? p.Start : -1;
    [As] public int getParagraphLength(int c) => c >= 0 && ParagraphOf(c) is { } p ? p.Length : -1;

    // DisplayObject.widthSet / heightSet call the object's SetWidth / SetHeight; TextField's (sfr_gfx_03f.cpp:792 / :364)
    // set the view rectangle's width / height to the value from its left / top, in the field's own pixels: the field's
    // scale is not divided out (CLIK's Constraints.update relies on it: a counter-scaled TextField gets the width unadjusted)
    [As] public new double width { get => Box.W * scaleX; set { if (double.IsNaN(value)) return; Box = new RectD(Box.X, Box.Y, value, Box.H); needGeom = true; boxBumps++; Version++; } }
    [As] public new double height { get => Box.H * scaleY; set { if (double.IsNaN(value)) return; Box = new RectD(Box.X, Box.Y, Box.W, value); needGeom = true; boxBumps++; Version++; } }

    public string autoSizeMode = "none";
    [As] public string autoSize { get => autoSizeMode; set { autoSizeMode = value is "left" or "right" or "center" ? value : "none"; Version++; autoSizePending = true; needGeom = true; } }
    [As] public string type { get; set; } = "dynamic";
    bool multilineFlag;
    [As] public bool multiline { get => multilineFlag; set { if (multilineFlag != value) { multilineFlag = value; MarkChanged(); } } }
    bool wordWrapFlag;
    [As] public bool wordWrap { get => wordWrapFlag; set { if (wordWrapFlag != value) { wordWrapFlag = value; autoSizePending = true; MarkChanged(); } needGeom = true; } }
    [As] public bool selectable { get; set; } = true;
    [As] public bool displayAsPassword { get; set; }
    /// <summary>TextField's Flags_IMEDisabled (gfx_textfield.h:281), set by TextFieldEx.setIMEEnabled (@0x1424cbab0, sfr_as3_15.cpp:585).</summary>
    public bool ImeDisabled;
    [As] public int maxChars { get; set; }
    [As] public string restrict { get; set; }
    [As] public bool embedFonts { get; set; } = true;
    [As] public bool border { get; set; }
    [As] public uint borderColor { get; set; }
    [As] public bool background { get; set; }
    [As] public uint backgroundColor { get; set; } = 0xffffff;
    [As] public bool condenseWhite { get; set; }
    [As] public bool alwaysShowSelection { get; set; }
    [As] public bool mouseWheelEnabled { get; set; } = true;
    [As] public bool useRichTextClipboard { get; set; }
    /// <summary>Readability text: a script-made field gets the empty text def, which is readability text (TextFieldDef::InitEmptyTextDef, sfr_gfx_01g.cpp:75); a timeline field takes its symbol's.</summary>
    string antiAlias = "advanced";
    [As] public string antiAliasType { get => antiAlias; set { if (antiAlias != value) { antiAlias = value; MarkChanged(); } } }
    [As] public string gridFitType { get; set; } = "pixel";
    [As] public double sharpness { get; set; }
    [As] public double thickness { get; set; }
    [As] public AsObject styleSheet { get; set; }
    [As] public uint textColor
    {
        get => Format?.color is { } c && !Rt.IsNullish(c) ? Rt.ToUint32(c) : 0;
        set { Format ??= (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct(); Format.color = Rt.Uint(value); Version++; }
    }
    [As] public AsTextFormat defaultTextFormat
    {
        get => (Format ?? (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct()).Copy(R);
        set { Format ??= (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct(); Format.MergeFrom(value); Version++; }
    }

    /// <summary>
    /// TextField::setTextFormat (sfr_as3_13.cpp:2847): the format goes on the chars from b (a missing one is 0) to e (missing: the
    /// end), an empty or reversed range nothing; a missing range is the whole text, which here also becomes the field's own format.
    /// What the format sets (color, size, bold, italic, underline, url) is kept per char, over the chars' html formats.
    /// </summary>
    [As] public void setTextFormat(AsTextFormat f, int b = -1, int e = -1)
    {
        if (f == null) return;
        int begin = b != -1 ? b : 0; long end = e != -1 ? e : int.MaxValue;
        if (begin > end) return;
        bool whole = b == -1 && e == -1;
        if (whole) { Format ??= (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct(); Format.MergeFrom(f); }
        if (!whole || Formats != null) ApplyCharFormat(f, begin, (int)Math.Min(end, Text.Length));
        Changed();
    }

    void ApplyCharFormat(AsTextFormat f, int begin, int end)
    {
        int n = Text.Length; begin = Math.Max(0, begin);
        if (begin >= end || begin >= n) return;
        bool Set(object v) => v != null && !Rt.IsNullish(v);
        if (!(Set(f.color) || Set(f.size) || Set(f.bold) || Set(f.italic) || Set(f.underline) || Set(f.url))) return;
        var src = Formats;
        bool own = src != null;
        var nf = new HtmlFormat[Math.Max(n, src?.Length ?? 0)];
        if (own) Array.Copy(src, nf, src.Length);
        for (int i = begin; i < end; i++)
        {
            var c = nf[i];
            if (Set(f.color)) c = c with { Color = Rt.ToUint32(f.color) };
            if (Set(f.size)) c = c with { Size = (int)Rt.ToNumber(f.size) };
            if (Set(f.bold)) c = c with { Bold = Rt.ToBoolean(f.bold) };
            if (Set(f.italic)) c = c with { Italic = Rt.ToBoolean(f.italic) };
            if (Set(f.underline)) c = c with { Underline = Rt.ToBoolean(f.underline) };
            if (Set(f.url)) c = c with { Url = Rt.ToString(f.url) is { Length: > 0 } u ? u : null };
            nf[i] = c;
        }
        SetFormats(nf, own && formatsBreak);
        // the html the formats were parsed from no longer says what the chars look like: the renderer and htmlText build it from them
        Html = null; htmlParser = null;
    }

    /// <summary>
    /// TextField::getTextFormat (sfr_as3_13.cpp:2828): the format of the chars from b to e, a property they do not all share
    /// null. With no per-char formats (plain text) every char has the field's own format.
    /// </summary>
    [As] public AsTextFormat getTextFormat(int b = -1, int e = -1)
    {
        int begin = b != -1 ? b : 0; long end = e != -1 ? e : int.MaxValue;
        if (begin > end) return (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct();
        var f = defaultTextFormat;
        var fm = Formats;
        int last = (int)Math.Min(end, Text.Length);
        if (fm == null || begin < 0 || begin >= last) return f;
        bool Same<T>(Func<HtmlFormat, T> get) { var first = get(fm[begin]); for (int i = begin + 1; i < last; i++) if (!EqualityComparer<T>.Default.Equals(get(fm[i]), first)) return false; return true; }
        uint dc = textColor; double ds = Size;
        bool db = f.bold != null && Rt.ToBoolean(f.bold), di = f.italic != null && Rt.ToBoolean(f.italic), du = f.underline != null && Rt.ToBoolean(f.underline);
        var c0 = fm[begin];
        f.color = Same(c => c.Color ?? dc) ? Rt.Uint(c0.Color ?? dc) : null;
        f.size = Same(c => c.Size ?? ds) ? Rt.Num(c0.Size ?? ds) : null;
        f.bold = Same(c => c.Bold || db) ? c0.Bold || db : null;
        f.italic = Same(c => c.Italic || di) ? c0.Italic || di : null;
        f.underline = Same(c => c.Underline || du) ? c0.Underline || du : null;
        if (!Same(c => c.Url)) f.url = null; else if (c0.Url != null) f.url = c0.Url;
        return f;
    }
    /// <summary>Both ends clamped into the text, the order kept: the caret goes to <paramref name="e"/> and
    /// <paramref name="b"/> is the anchor a Shift key extends from (TextField::SetSelection @0x1425bcfc0, which calls
    /// EditorKit::SetSelection: SetCursorPos(e), then DocView::SetSelection(b, e) as given).</summary>
    [As] public void setSelection(int b, int e) => Select(Math.Clamp(b, 0, Text.Length), Math.Clamp(e, 0, Text.Length));
    [As] public int selectionBeginIndex => SelBegin;
    [As] public int selectionEndIndex => SelEnd;
    [As] public int caretIndex => CaretAtBegin ? SelBegin : SelEnd;
    int scrollTop = 1;
    /// <summary>
    /// The first shown line, 1-based. TextField::scrollVSet (sfr_as3_13.cpp:1446): below 1 is 1, and DocView::SetVScrollOffset
    /// (sfr_render_03b.cpp:572) keeps it within the last scroll position (maxScrollV) and, only when the line really changes,
    /// tells the listener (OnScroll, sfr_gfx_03e.cpp:895), which sends the field's scroll event (AvmTextField::OnScroll,
    /// sfr_as3_18.cpp:5131). A view that shrank below the line keeps the last position (DocView::ForceReformat, :1020).
    /// </summary>
    [As] public int scrollV
    {
        get { if (scrollTop <= 1) return 1; return Math.Min(scrollTop, maxScrollV); }
        set
        {
            int v = Math.Clamp(value, 1, maxScrollV);
            if (v == scrollV) return;
            scrollTop = v;
            MarkChanged();      // the view moves the text (FieldText.Sync)
            Send("flash.events.Event", "scroll");
        }
    }

    int lastMaxScroll = 1;
    bool scrollWatched;

    /// <summary>Called when the text, its size or its format changed: a field somebody can see scroll (a scroll listener, or
    /// scrolled down already) is checked after the frame's scripts (<see cref="CheckScroll"/>), where Scaleform reformats.</summary>
    void WatchScroll()
    {
        if (scrollWatched || (scrollTop <= 1 && !hasEventListener("scroll"))) return;
        if (Class?.Runtime?.Player is { } p) { scrollWatched = true; p.WatchScroll(this); }
    }

    /// <summary>The reformat's end (DocView::Format, sfr_render_03b.cpp:2497-2502, :2579-2583): a first line beyond the new last
    /// scroll position is moved to it through SetVScrollOffset, which sends the field's scroll event; else a changed
    /// maxScrollV is told to the listener (View_OnMaxScrollChanged, sfr_gfx_03f.cpp:1050), which sends the same event
    /// (OnScroll sfr_gfx_03e.cpp:895), so a scroll bar follows the new range.</summary>
    internal void CheckScroll()
    {
        scrollWatched = false;
        int max = maxScrollV;
        bool clamped = scrollTop > max;
        if (clamped) { scrollTop = max; MarkChanged(); }
        bool maxChanged = max != lastMaxScroll;
        lastMaxScroll = max;
        if (clamped || maxChanged) Send("flash.events.Event", "scroll");
    }

    /// <summary>The 0-based first shown line the view draws from (scrollV - 1). 0 for a field that was never scrolled, which is the
    /// common case and costs no layout.</summary>
    public int FirstShownLine => scrollTop <= 1 ? 0 : scrollV - 1;

    /// <summary>The index in <see cref="Text"/> of the first shown line's first character (0 for a field never scrolled).</summary>
    public int FirstShownChar { get { if (scrollTop <= 1) return 0; var sp = Spans(); return sp[Math.Min(FirstShownLine, sp.Count - 1)].Start; } }

    /// <summary>The field is scrolled down and shows its last scroll position (a view that follows the newest line).</summary>
    public bool AtScrollEnd => scrollTop > 1 && scrollV >= maxScrollV;

    /// <summary>TextField::OnMouseWheelEvent (@0x1425ba680, sfr_gfx_03e.cpp:915): when mouseWheelEnabled the wheel moves the first
    /// shown line up by the delta.</summary>
    public void OnMouseWheel(int delta)
    {
        if (mouseWheelEnabled) scrollV = scrollV - delta;
    }
    [As] public int scrollH { get; set; }
    [As] public int maxScrollV { get { int vis = Math.Max(1, (int)((Box.H - 4) / TextMeasure.LineHeight(FontName, Size, Leading))); return Math.Max(1, numLines - vis + 1); } }
    [As] public int maxScrollH => 0;
    [As] public int bottomScrollV { get { int vis = Math.Max(1, (int)((Box.H - 4) / TextMeasure.LineHeight(FontName, Size, Leading))); return Math.Min(numLines, scrollV + vis - 1); } }
    [As] public AsObject getImageReference(string id) => null;

    /// <summary>Typed input from the player: inserts at the caret, honours maxChars and restrict. A character the
    /// restrict set refuses is retried in the other case and dropped only if that is refused too (EditorKit::OnChar
    /// @0x142618120: SFtowupper, else SFtowlower, before the restrict ranges are asked).</summary>
    public void TypeText(string s)
    {
        if (type != "input") return;
        if (restrict != null)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) if (Restricted(ch) is char c) sb.Append(c);
            s = sb.ToString();
        }
        if (maxChars > 0) s = s[..Math.Min(s.Length, Math.Max(0, maxChars - (Text.Length - (SelEnd - SelBegin))))];
        if (s.Length == 0) return;
        var b = Math.Min(SelBegin, Text.Length); var e = Math.Min(SelEnd, Text.Length);
        Text = Text[..b] + s + Text[e..]; Html = null;
        SelBegin = SelEnd = b + s.Length;
        Changed();
        Send("flash.events.Event", "change", true);
    }

    /// <summary>
    /// Backspace (KeyAct_Backspace, EditorKit's key handler, sfr_gfx_07.cpp:780), input fields only: the selection goes, or
    /// with none the character before the caret. The field reports a change every time, also when nothing went (at the
    /// start of the text or in an empty field): the handler sets needChangeNotify unconditionally.
    /// </summary>
    public void Backspace()
    {
        if (type != "input") return;
        int b = SelBegin, e = SelEnd;
        if (b == e && b > 0) b--;
        if (b != e) { Text = Text[..b] + Text[e..]; SelBegin = SelEnd = b; CaretAtBegin = false; Html = null; Changed(); }
        Send("flash.events.Event", "change", true);
    }

    /// <summary>
    /// Delete (KeyAct_Delete, EditorKit's key handler, sfr_gfx_07.cpp:798), input fields only. The selection goes, or with
    /// none the character after the caret, and the caret stays at the start. The field reports a change only when
    /// something went: unlike Backspace, Delete at the end of the text sends no change event.
    /// </summary>
    public void Delete()
    {
        if (type != "input") return;
        var (b, e) = SelRange();
        if (b == e) { if (b >= Text.Length) return; e = b + 1; }
        Text = Text[..b] + Text[e..]; SelBegin = SelEnd = b; Html = null; Changed();
        Send("flash.events.Event", "change", true);
    }

    /// <summary>Ctrl+A (KeyAct_SelectAll, EditorKit's key handler, sfr_gfx_07.cpp:821): the whole text is selected, nothing changes.</summary>
    public void SelectAll() => Select(0, Text.Length);

    /// <summary>
    /// EditorKit::ClearSelection on a lost focus: DocView::SetSelection(0, 0), the cursor kept. Here the selection collapses
    /// to the caret instead, so caretIndex keeps its value (the client's selectionBeginIndex and selectionEndIndex then
    /// read 0, ours read the caret).
    /// </summary>
    public void ClearSelection()
    {
        int at = caretIndex;
        SelBegin = SelEnd = at;
        CaretAtBegin = false;
    }

    /// <summary>The caret is at <see cref="SelBegin"/> (a selection made leftwards, or setSelection(b, e) with e &lt; b),
    /// else at <see cref="SelEnd"/>. The other end is the anchor a Shift key extends from.</summary>
    public bool CaretAtBegin;

    void Select(int anchor, int caret) { SelBegin = Math.Min(anchor, caret); SelEnd = Math.Max(anchor, caret); CaretAtBegin = caret < anchor; }

    // EditorKit::LastHorizCursorPos: the x an Up / Down / PageUp / PageDown move aimed at, kept for the next such move
    // while the caret stays where that move put it (any other cursor move resets it, EditorKit::SetCursorPos @0x142619b80)
    double keptCaretX = -1;
    int keptCaretPos = -1;
    string keptCaretText;

    /// <summary>
    /// A caret key (EditorKit's key handler, sfr_gfx_07.cpp:646-776, then its final block :864). <paramref name="shift"/>
    /// extends the selection from its anchor, only in a selectable field (KeyAct_EnterSelectionMode sets the editor's
    /// shift state only when IsSelectable). Without it the caret moves and the selection collapses; Left / Right / Up /
    /// Down on a selection go to its start or end. A key that leaves the caret where it is still collapses a selection.
    /// Up / Down / PageUp / PageDown keep the caret's x: the char nearest it on the target line
    /// (DocView::GetCursorPosInLineByOffset @0x1426125b0), the start of the text before the first line, its end past the
    /// last. The x is measured from the line start. Returns whether the caret or selection changed.
    /// </summary>
    public bool MoveCaret(CaretKey key, bool shift)
    {
        shift &= selectable;
        int len = Text.Length, cur = Math.Min(caretIndex, len), pos = cur;
        int b = Math.Min(SelBegin, len), e = Math.Min(SelEnd, len);
        bool sel = b != e;
        double x = -1;
        var lines = Spans();
        int line = 0;
        while (line + 1 < lines.Count && lines[line + 1].Start <= cur) line++;
        int End(int i) => lines[i].Start + lines[i].Line.Length;   // before the line's new line
        switch (key)
        {
            case CaretKey.Left: if (pos > 0) pos--; if (!shift && sel) pos = b; break;
            case CaretKey.Right: if (pos < len) pos++; if (!shift && sel) pos = e; break;
            case CaretKey.LineHome: pos = lines[line].Start; break;
            case CaretKey.LineEnd: pos = End(line); break;
            case CaretKey.DocHome: pos = 0; break;
            case CaretKey.DocEnd: pos = len; break;
            case CaretKey.PageHome: pos = lines[Math.Clamp(scrollV - 1, 0, lines.Count - 1)].Start; break;
            case CaretKey.PageEnd: pos = End(Math.Clamp(bottomScrollV - 1, 0, lines.Count - 1)); break;
            case CaretKey.Up or CaretKey.Down when !shift && sel:
                pos = key == CaretKey.Up ? b : e;
                break;
            default:
            {
                bool page = key is CaretKey.PageUp or CaretKey.PageDown;
                int step = page ? bottomScrollV - scrollV + 1 : 1;
                int to = key is CaretKey.Up or CaretKey.PageUp ? line - step : line + step;
                // Up on the first line and Down on the last do not take the x; the page keys always do
                if (page || (to >= 0 && to < lines.Count))
                    x = keptCaretX >= 0 && keptCaretPos == cur && ReferenceEquals(keptCaretText, Text)
                        ? keptCaretX : LineWidth(lines[line].Line[..Math.Min(cur - lines[line].Start, lines[line].Line.Length)], lines[line].Start);
                pos = to < 0 ? 0 : to >= lines.Count ? len : PosAtX(lines[to], x);
                break;
            }
        }
        if (pos != cur)
        {
            if (shift) Select(CaretAtBegin ? e : b, pos);
            else { SelBegin = SelEnd = pos; CaretAtBegin = false; }
            keptCaretX = x; keptCaretPos = pos; keptCaretText = Text;
            return true;
        }
        if (!shift && sel) { SelBegin = SelEnd = pos; CaretAtBegin = false; keptCaretX = -1; return true; }
        return false;
    }

    /// <summary>EditorKit's shift state (IsShiftPressed): set by a Shift key down while the field has the focus and is
    /// selectable (KeyAct_EnterSelectionMode), cleared by its key up (OnKeyUp) and by a lost focus (OnKillFocus). A press
    /// with it set extends the selection.</summary>
    public bool ShiftPressed;

    /// <summary>EditorKit's mouse capture (IsMouseCaptured): set by a left press on a selectable field, cleared by the
    /// release (OnMouseUp) and by a lost focus (OnKillFocus). While it is set the mouse's moves go to the field wherever
    /// the mouse is, and move the selection's end.</summary>
    public bool MouseCaptured { get; private set; }

    // EditorKit::LastMousePos (twips from the view's corner, floored) and LastClickTime (ms): a press at the same point
    // within MS2_DOUBLE_CLICK_TIME_MS of the last one is a double click, the next one a triple click
    double lastPressX = double.NaN, lastPressY = double.NaN;
    uint lastPressTime;
    bool lastPressDouble;

    /// <summary>The window of <see cref="MouseDown"/>'s double and triple click (MS2_DOUBLE_CLICK_TIME_MS,
    /// sfr_gfx_07.cpp:43).</summary>
    public const uint DoubleClickMs = 300;

    /// <summary>Whether the field has an editor (TextField's EditorKit): an input field or a selectable one.</summary>
    public bool HasEditor => type == "input" || selectable;

    /// <summary>
    /// A left press on the field (EditorKit::OnMouseDown @0x142618bf0, called from TextField's mouse handler with the press
    /// in field units): the caret goes to the char boundary nearest the press (<see cref="CursorPosAtPoint"/>); with the
    /// shift state set the selection's end goes there, else the selection collapses there. A selectable field then takes
    /// the mouse capture, unless it has it. A second press at the same point (in twips) within <see cref="DoubleClickMs"/>
    /// selects the word there (the run of letters and digits around it), a third the paragraph with its new line; the caret
    /// goes to the selection's end. <paramref name="timeMs"/> is the press's time in ms (Timer::GetTicks()/1000).
    /// </summary>
    public void MouseDown(double x, double y, uint timeMs)
    {
        double ax = Math.Floor((x - Box.X) * 20), ay = Math.Floor((y - Box.Y) * 20);
        bool doubleClick = false, tripleClick = false;
        if (ax == lastPressX && ay == lastPressY && timeMs <= unchecked(lastPressTime + DoubleClickMs))
        {
            if (lastPressDouble) tripleClick = true; else doubleClick = true;
            lastPressDouble = true;
        }
        else lastPressDouble = false;
        lastPressX = ax; lastPressY = ay; lastPressTime = timeMs;

        // the lookup takes the floored point (GetCursorPosAtPoint(ax, ay))
        int pos = CursorPosAtPoint(Box.X + ax / 20, Box.Y + ay / 20);
        SetCursorPos(pos);
        if (!selectable || MouseCaptured) return;
        MouseCaptured = true;
        if (doubleClick || tripleClick)
        {
            var (ps, pe) = ParagraphAt(pos);
            int st = pos, en = pos;
            if (doubleClick)
            {
                static bool Word(char c) => !char.IsWhiteSpace(c) && char.IsLetterOrDigit(c);
                while (st > ps && Word(Text[st - 1])) st--;
                while (en < pe && Word(Text[en])) en++;
            }
            else { st = ps; en = pe; }
            SetCursorPos(en);
            Select(st, en);
        }
        else if (!ShiftPressed) Select(pos, pos);
    }

    /// <summary>A mouse move while the field has the mouse capture (EditorKit::OnMouseMove @0x142618f60): the caret and the
    /// selection's end go to the char boundary nearest the mouse. Without the capture it does nothing.</summary>
    public void MouseMove(double x, double y)
    {
        if (!MouseCaptured) return;
        // LastMousePos takes the floored point, which also ends a double click at the press point
        double ax = Math.Floor((x - Box.X) * 20), ay = Math.Floor((y - Box.Y) * 20);
        lastPressX = ax; lastPressY = ay;
        int pos = CursorPosAtPoint(Box.X + ax / 20, Box.Y + ay / 20);
        SetCursorPos(pos);
        if (selectable) Select(Anchor, pos);
    }

    /// <summary>The left button's release (EditorKit::OnMouseUp @0x142619040): the mouse capture ends.</summary>
    public void MouseUp() { if (selectable) MouseCaptured = false; }

    /// <summary>A lost focus (EditorKit::OnKillFocus @0x142618bb0): the shift state and the mouse capture end.</summary>
    public void KillEditorFocus() { ShiftPressed = false; MouseCaptured = false; }

    // the selection's fixed end, the one a Shift key or a drag extends from (DocView's begin selection)
    int Anchor => CaretAtBegin ? SelEnd : SelBegin;

    // EditorKit::SetCursorPos (sfr_gfx_07.cpp:85) from the mouse: the caret moves; in a selectable field with the shift
    // state or the mouse capture the selection's end follows it, else the selection collapses there. The x an Up / Down
    // key aimed at is dropped (LastHorizCursorPos = -1).
    void SetCursorPos(int pos)
    {
        pos = Math.Clamp(pos, 0, Text.Length);
        keptCaretX = -1;
        if (selectable && (ShiftPressed || MouseCaptured)) Select(Anchor, pos);
        else { SelBegin = SelEnd = pos; CaretAtBegin = false; }
    }

    // the paragraph holding text index i: its first index and the index past its new line (StyledText's paragraph, whose
    // length counts the new line; CR LF is one new line)
    (int Start, int End) ParagraphAt(int i)
    {
        int s = Math.Clamp(i, 0, Text.Length);
        while (s > 0 && Text[s - 1] is not ('\r' or '\n')) s--;
        int e = Math.Clamp(i, 0, Text.Length);
        while (e < Text.Length && Text[e] is not ('\r' or '\n')) e++;
        if (e < Text.Length) e += Text[e] == '\r' && e + 1 < Text.Length && Text[e + 1] == '\n' ? 2 : 1;
        return (s, e);
    }

    /// <summary>
    /// The char boundary nearest a point in field units (DocView::GetCursorPosAtPoint @0x142612410): the line at the point's
    /// height, counted from the first shown line (scrollV) with each line its height plus the leading below the 2 px gutter;
    /// above the lines the first, below them the last. On the line, the boundary nearest the x from the line's start (the
    /// gutter and the paragraph align's offset): past a char's middle the caret goes after it
    /// (GetCursorPosInLineByOffset @0x1426125b0). 0 in a field without lines.
    /// </summary>
    public int CursorPosAtPoint(double x, double y)
    {
        var lines = Spans();
        if (lines.Count == 0) return 0;
        double step = TextMeasure.LineHeight(FontName, Size, 0) + Leading;
        double top = FirstLineTop(lines.Count);
        int k = (int)Math.Floor((y - top) / step) + (scrollV - 1);
        k = Math.Clamp(k, 0, lines.Count - 1);
        var l = lines[k];
        double lineX = Box.X + 2;
        double free = Math.Max(0, Box.W - 4 - LineWidth(l.Line, l.Start));
        switch ((Format?.align as string)?.ToLowerInvariant()) { case "center": lineX += free / 2; break; case "right": lineX += free; break; }
        return PosAtX(l, x - lineX);
    }

    // the char boundary nearest x on the line: past a char's middle the caret goes after it
    int PosAtX((string Line, int Start) l, double x)
    {
        double w0 = 0;
        int i = 0;
        for (; i < l.Line.Length; i++)
        {
            double w1 = LineWidth(l.Line[..(i + 1)], l.Start);
            if (w1 > x) { if (x - w0 > (w1 - w0) / 2) i++; break; }
            w0 = w1;
        }
        return l.Start + i;
    }

    /// <summary>
    /// Copies the selection to <paramref name="clipboard"/> (EditorKit::CopyToClipboard @0x142617440). A password field never
    /// reaches the clipboard. An empty selection stores an empty text, as the client does.
    /// </summary>
    public void Copy(ITextClipboard clipboard)
    {
        if (clipboard == null || displayAsPassword) return;
        var (b, e) = SelRange();
        clipboard.SetText(Text[b..e]);
    }

    /// <summary>
    /// Ctrl+X / Shift+Delete (KeyAct_Cut, sfr_gfx_07.cpp:829). A read-only field copies instead. An input field copies
    /// (EditorKit::CutToClipboard @0x1426175b0, which calls CopyToClipboard, so a password field stores nothing) and
    /// then removes the selection, and the field reports a change.
    /// </summary>
    public void Cut(ITextClipboard clipboard)
    {
        if (clipboard == null) return;
        if (type != "input") { Copy(clipboard); return; }
        Copy(clipboard);
        var (b, e) = SelRange();
        Text = Text[..b] + Text[e..]; Html = null;
        SelBegin = SelEnd = b;
        Changed();
        Send("flash.events.Event", "change", true);
    }

    /// <summary>
    /// Ctrl+V / Shift+Insert (KeyAct_Paste, sfr_gfx_07.cpp:847; EditorKit::PasteFromClipboard @0x142619270). Only an input
    /// field takes a paste, and only a non-empty clipboard text. The text replaces the selection, cut first to what
    /// maxChars leaves (DocView::EditCommand Cmd_InsertPlainText / Cmd_ReplaceTextByPlainText, sfr_render_03b.cpp:2668);
    /// a text already longer than maxChars takes the whole paste, as the client's unsigned subtraction wraps.
    /// Each CR LF is then one new line, and a CR or LF becomes the field's new line (StyledText::InsertString with
    /// NLP_CompressCRLF), also in a single-line field. Then the restrict set is applied to the whole text: a refused
    /// character is retried in the other case and otherwise removed. The caret goes to the selection start plus the
    /// characters inserted, not moved back for the ones removed.
    /// </summary>
    public bool Paste(ITextClipboard clipboard)
    {
        if (type != "input" || clipboard == null) return false;
        var src = clipboard.GetText() ?? "";
        int nul = src.IndexOf('\0');
        if (nul >= 0) src = src[..nul];
        var (b, e) = SelRange();
        var t = Text;
        string ins = "";
        if (src.Length > 0)
        {
            int len = src.Length;
            if (maxChars > 0)
            {
                // the client cuts to GetMaxLength() - dstLen in unsigned arithmetic: when a script left the text without the
                // selection longer than maxChars that wraps to a huge length, and InsertString takes the whole text (up to
                // its NUL, already cut above)
                int left = Text.Length - (e - b);
                if (left + len > maxChars && left <= maxChars) len = maxChars - left;
            }
            ins = NewLines(src[..len]);
            t = Text[..b] + ins + Text[e..];
        }
        // the restrict pass runs on every paste, also one that inserted nothing
        if (restrict != null)
        {
            var sb = new StringBuilder(t.Length);
            foreach (var ch in t) if (Restricted(ch) is char c) sb.Append(c);
            t = sb.ToString();
        }
        if (t == Text && src.Length == 0) return false;
        Text = t; Html = null;
        if (src.Length > 0) SelBegin = SelEnd = Math.Min(b + ins.Length, Text.Length);
        else { SelBegin = Math.Min(SelBegin, Text.Length); SelEnd = Math.Min(SelEnd, Text.Length); }
        Changed();
        if (src.Length == 0) return false;
        Send("flash.events.Event", "change", true);
        return true;
    }

    (int B, int E) SelRange()
    {
        int b = Math.Clamp(Math.Min(SelBegin, SelEnd), 0, Text.Length), e = Math.Clamp(Math.Max(SelBegin, SelEnd), 0, Text.Length);
        return (b, e);
    }

    // CR LF -> CR, LF -> CR: StyledText::InsertString with NLP_CompressCRLF (sfr_render_01c.cpp:905) stores NewLineChar for each
    static string NewLines(string s)
    {
        if (s.IndexOf('\n') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\n' && i > 0 && s[i - 1] == '\r') continue;
            sb.Append(s[i] == '\n' ? '\r' : s[i]);
        }
        return sb.ToString();
    }

    /// <summary>The character the restrict set lets in for <paramref name="ch"/>: itself, else the other case, else null
    /// (the paste filter, sfr_gfx_07.cpp:1100, and EditorKit::OnChar @0x142618120). Callers ask only when restrict is not
    /// null; "" is a set with no range (EditorKit::SetRestrict @0x14261a490), so it lets no char in.</summary>
    char? Restricted(char ch)
    {
        if (Allowed(ch)) return ch;
        char up = char.ToUpperInvariant(ch);
        char other = ch != up ? up : char.ToLowerInvariant(ch);
        return Allowed(other) ? other : null;
    }

    bool Allowed(char ch)
    {
        var r = restrict; bool neg = false, ok = false;
        for (int i = 0; i < r.Length; i++)
        {
            if (r[i] == '^') { neg = !neg; continue; }
            char a = r[i] == '\\' && i + 1 < r.Length ? r[++i] : r[i];
            char b = a;
            if (i + 2 < r.Length && r[i + 1] == '-') { b = r[i + 2]; i += 2; }
            if (ch >= a && ch <= b) ok = !neg;
        }
        return ok;
    }
}

/// <summary>The caret actions of the client's text key map (TextKeyMap::KeyAction, gfx_textclipboard.h).</summary>
public enum CaretKey { Left, Right, Up, Down, LineHome, LineEnd, DocHome, DocEnd, PageUp, PageDown, PageHome, PageEnd }

[AsClass("flash.text.TextFieldType")] public class AsTextFieldType : AsObject { [As] public const string DYNAMIC = "dynamic", INPUT = "input"; }
[AsClass("flash.text.TextFieldAutoSize")] public class AsTextFieldAutoSize : AsObject { [As] public const string CENTER = "center", LEFT = "left", NONE = "none", RIGHT = "right"; }
[AsClass("flash.text.TextFormatAlign")] public class AsTextFormatAlign : AsObject { [As] public const string CENTER = "center", END = "end", JUSTIFY = "justify", LEFT = "left", RIGHT = "right", START = "start"; }
[AsClass("flash.text.AntiAliasType")] public class AsAntiAliasType : AsObject { [As] public const string ADVANCED = "advanced", NORMAL = "normal"; }
[AsClass("flash.text.GridFitType")] public class AsGridFitType : AsObject { [As] public const string NONE = "none", PIXEL = "pixel", SUBPIXEL = "subpixel"; }
[AsClass("flash.text.FontStyle")] public class AsFontStyle : AsObject { [As] public const string BOLD = "bold", BOLD_ITALIC = "boldItalic", ITALIC = "italic", REGULAR = "regular"; }

[AsClass("flash.text.Font")]
public class AsFont : AsObject
{
    [As] public string fontName { get; set; } = "";
    [As] public string fontStyle { get; set; } = "regular";
    [As] public string fontType { get; set; } = "embedded";
    [As] public bool hasGlyphs(string s) => true;
    [As] public static AsArray enumerateFonts(bool device = false) => new(Runtime.Current, Array.Empty<object>());
    [As] public static void registerFont(object c) { }
}

[AsClass("flash.text.StyleSheet", "flash.events.EventDispatcher", Dynamic = true)]
public class AsStyleSheet : AsEventDispatcher
{
    readonly Dictionary<string, object> styles = new();
    [As] public void setStyle(string n, object v) => styles[n.ToLowerInvariant()] = v;
    [As] public object getStyle(string n) => styles.TryGetValue(n.ToLowerInvariant(), out var v) ? v : new AsObject(RT.ObjectClass);
    [As] public void parseCSS(string s) { }
    [As] public void clear() => styles.Clear();
    [As] public AsArray styleNames => new(RT, styles.Keys.Cast<object>());
    [As] public AsTextFormat transform(object o) => (AsTextFormat)RT.GetClass("flash.text.TextFormat").Construct();
}
