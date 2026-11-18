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
    public string Text = "";
    public string Html;            // last htmlText assigned (kept for the renderer)
    public bool IsHtml;
    public RectD Box = new(0, 0, 100, 100);
    public AsTextFormat Format;
    public string Variable;
    public int SelBegin, SelEnd;
    public int Version;            // bumped on each change (renderer refresh)
    public string VerticalAlign = "none", TextAutoSize = "none";

    Runtime R => Class.Runtime;

    [AsCtor] void Ctor(object[] a) { }

    /// <summary>Set up from a text symbol of the layout.</summary>
    public void FromSymbol(JsonObject sym)
    {
        if (sym?["bounds"] is JsonObject b) Box = SymbolScope.Rect(b);
        var t = sym?["text"] as JsonObject;
        Format = (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct();
        if (t == null) return;
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
    }

    /// <summary>Scaleform's translator: a text that is a string-table key ("$s_...") shows the table's value.</summary>
    string Tr(string s) => s.Length > 1 && s[0] == '$' && Class.Runtime.Player?.Translate is { } t ? t(s) ?? s : s;

    double Size => Format?.size is { } s && !Rt.IsNullish(s) ? Rt.ToNumber(s) : 12;
    string FontName => Format?.font is string f ? f : "";
    public double Leading => Format?.leading is { } l && !Rt.IsNullish(l) ? Rt.ToNumber(l) : 0;

    void Changed() { Version++; if (autoSizeMode != "none") AutoSize(); }

    public List<string> Lines()
    {
        var src = (displayAsPassword ? new string('*', Text.Length) : Text).Replace("\r\n", "\r").Replace('\n', '\r');
        var paras = src.Split('\r');
        var lines = new List<string>();
        double max = Box.W - 4;
        foreach (var p in paras)
        {
            if (!wordWrap || max <= 0) { lines.Add(p); continue; }
            var cur = new StringBuilder();
            // English breaking: only after a space or a hyphen, never inside a word (a word wider than the box stays
            // whole); CJK text keeps its own per-character breaking. Unlike Scaleform's per-character wrap of the
            // Korean-sized fields ("Characte/r"), which the English text rule of this port does not copy.
            foreach (var word in Regex.Split(p, @"(?<=[\s\-])|(?<=[⺀-￿])(?=.)|(?<=.)(?=[⺀-￿])"))
            {
                if (cur.Length > 0 && TextMeasure.Width(FontName, Size, cur + word.TrimEnd()) > max) { lines.Add(cur.ToString()); cur.Clear(); }
                cur.Append(word);
            }
            lines.Add(cur.ToString());
        }
        return lines;
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

    public double TextW() => Lines().Select(l => TextMeasure.Width(FontName, Size, l)).DefaultIfEmpty(0).Max();
    public double TextH() { var n = Lines().Count; return Text.Length == 0 ? 0 : n * TextMeasure.LineHeight(FontName, Size, Leading) - Leading; }

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

    public void SetHtml(string html)
    {
        Html = html ?? "";
        IsHtml = true;
        var s = Regex.Replace(Tr(Html), @"<br\s*/?>|</p>", "\r", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]*>", "");
        s = s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&nbsp;", " ").Replace("&amp;", "&");
        if (s.EndsWith('\r') && Regex.IsMatch(Html, "</p>\\s*$", RegexOptions.IgnoreCase)) s = s[..^1];
        Text = s;
    }

    public override RectD LocalBounds() => Box;
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
            else { Text = v; Html = null; }
            Changed();
        }
    }
    [As] public string htmlText
    {
        get => Html ?? "<P ALIGN=\"" + (Format?.align ?? "left") + "\">" + System.Net.WebUtility.HtmlEncode(Text) + "</P>";
        set { SetHtml(value); Changed(); }
    }
    [As] public void appendText(string s) { Text += s ?? ""; Html = null; Changed(); }
    [As] public void replaceText(int b, int e, string s) { b = Math.Clamp(b, 0, Text.Length); e = Math.Clamp(e, b, Text.Length); Text = Text[..b] + (s ?? "") + Text[e..]; Html = null; Changed(); }
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
    [As] public int getLineIndexOfChar(int c) { var l = Lines(); int o = 0; for (int k = 0; k < l.Count; k++) { o += l[k].Length; if (c < o) return k; } return -1; }
    [As] public int getLineIndexAtPoint(double x, double y) => Math.Clamp((int)((y - 2) / TextMeasure.LineHeight(FontName, Size, Leading)), 0, numLines - 1);
    [As] public int getCharIndexAtPoint(double x, double y) => -1;
    [As] public AsRectangle getCharBoundaries(int i)
    {
        if (i < 0 || i >= Text.Length) return null;
        double x = 2 + TextMeasure.Width(FontName, Size, Text[..i]);
        return AsRectangle.New(R, x, 2, TextMeasure.Width(FontName, Size, Text[i].ToString()), TextMeasure.LineHeight(FontName, Size, 0));
    }
    [As] public AsTextLineMetrics getLineMetrics(int i)
    {
        var m = (AsTextLineMetrics)R.GetClass("flash.text.TextLineMetrics").Construct();
        var l = Lines(); var s = i >= 0 && i < l.Count ? l[i] : "";
        m.width = TextMeasure.Width(FontName, Size, s); m.height = TextMeasure.LineHeight(FontName, Size, Leading);
        m.ascent = Size * 0.9; m.descent = Size * 0.25; m.leading = Leading; m.x = 2;
        return m;
    }
    [As] public int getFirstCharInParagraph(int c) => 0;
    [As] public int getParagraphLength(int c) => Text.Length;

    // DisplayObject.widthSet / heightSet call the object's SetWidth / SetHeight; TextField's (sfr_gfx_03f.cpp:792 / :364)
    // set the view rectangle's width / height to the value from its left / top, in the field's own pixels: the field's
    // scale is not divided out (CLIK's Constraints.update relies on it: a counter-scaled TextField gets the width unadjusted)
    [As] public new double width { get => Box.W * scaleX; set { if (double.IsNaN(value)) return; Box = new RectD(Box.X, Box.Y, value, Box.H); Version++; } }
    [As] public new double height { get => Box.H * scaleY; set { if (double.IsNaN(value)) return; Box = new RectD(Box.X, Box.Y, Box.W, value); Version++; } }

    public string autoSizeMode = "none";
    [As] public string autoSize { get => autoSizeMode; set { autoSizeMode = value is "left" or "right" or "center" ? value : "none"; Changed(); } }
    [As] public string type { get; set; } = "dynamic";
    [As] public bool multiline { get; set; }
    [As] public bool wordWrap { get; set; }
    [As] public bool selectable { get; set; } = true;
    [As] public bool displayAsPassword { get; set; }
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
    [As] public string antiAliasType { get; set; } = "advanced";
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
    [As] public void setTextFormat(AsTextFormat f, int b = -1, int e = -1) { Format ??= (AsTextFormat)R.GetClass("flash.text.TextFormat").Construct(); Format.MergeFrom(f); Changed(); }
    [As] public AsTextFormat getTextFormat(int b = -1, int e = -1) => defaultTextFormat;
    [As] public void setSelection(int b, int e) { SelBegin = Math.Clamp(b, 0, Text.Length); SelEnd = Math.Clamp(e, SelBegin, Text.Length); }
    [As] public int selectionBeginIndex => SelBegin;
    [As] public int selectionEndIndex => SelEnd;
    [As] public int caretIndex => SelEnd;
    [As] public int scrollV { get; set; } = 1;
    [As] public int scrollH { get; set; }
    [As] public int maxScrollV { get { int vis = Math.Max(1, (int)((Box.H - 4) / TextMeasure.LineHeight(FontName, Size, Leading))); return Math.Max(1, numLines - vis + 1); } }
    [As] public int maxScrollH => 0;
    [As] public int bottomScrollV { get { int vis = Math.Max(1, (int)((Box.H - 4) / TextMeasure.LineHeight(FontName, Size, Leading))); return Math.Min(numLines, scrollV + vis - 1); } }
    [As] public AsObject getImageReference(string id) => null;

    /// <summary>Typed input from the player: inserts at the caret, honours maxChars and restrict.</summary>
    public void TypeText(string s)
    {
        if (type != "input") return;
        if (!string.IsNullOrEmpty(restrict)) s = new string(s.Where(ch => Allowed(ch)).ToArray());
        if (maxChars > 0) s = s[..Math.Min(s.Length, Math.Max(0, maxChars - (Text.Length - (SelEnd - SelBegin))))];
        if (s.Length == 0) return;
        var b = Math.Min(SelBegin, Text.Length); var e = Math.Min(SelEnd, Text.Length);
        Text = Text[..b] + s + Text[e..]; Html = null;
        SelBegin = SelEnd = b + s.Length;
        Changed();
        Send("flash.events.Event", "change", true);
    }

    public void Backspace()
    {
        if (type != "input") return;
        int b = SelBegin, e = SelEnd;
        if (b == e) { if (b == 0) return; b--; }
        Text = Text[..b] + Text[e..]; SelBegin = SelEnd = b; Html = null; Changed();
        Send("flash.events.Event", "change", true);
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
