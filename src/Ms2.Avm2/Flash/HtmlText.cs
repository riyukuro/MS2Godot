using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ms2.Avm2.Flash;

/// <summary>
/// An IMG tag as StyledText::ParseHtml reads it into its HTMLImageTagDesc (sfr_render_05i.cpp:946): SRC (the url), ID,
/// ALIGN (left, right or baseline; Baseline when none), WIDTH and HEIGHT (whole pixels, 0 or more; 0: the image's own) and
/// VSPACE and HSPACE (whole pixels).
/// </summary>
public sealed record HtmlImage(string Src, string Id, HtmlImageAlign Align, int Width, int Height, int VSpace, int HSpace);

/// <summary>HTMLImageTagInfo alignment (Align_BaseLine is the default).</summary>
public enum HtmlImageAlign { BaseLine, Left, Right }

/// <summary>
/// One code point's text format from htmlText: its FONT COLOR (0xRRGGBB; null: the field's own), bold, italic and
/// underline, its FONT SIZE (null: the field's own), the IMG tag it stands for (null for text) and the A HREF it is
/// inside (TextFormat's Url; null: none).
/// </summary>
public readonly record struct HtmlFormat(uint? Color, bool Bold, bool Italic, bool Underline, int? Size)
{
    public HtmlImage Image { get; init; }
    public string Url { get; init; }

    public bool IsPlain => Color == null && !Bold && !Italic && !Underline && Size == null && Image == null && Url == null;
}

/// <summary>
/// A Flash text field's htmlText read and written the way Scaleform's StyledText does it: <see cref="Parse"/> is
/// StyledText::ParseHtml (sfr_render_05i.cpp), <see cref="Build"/> is StyledText::GetHtml (sfr_render_01c.cpp:1308).
/// </summary>
public static class HtmlText
{
    /// <summary>
    /// StyledText::ParseHtml: the text without its markup and each code point's text format.
    /// <list type="bullet">
    /// <item>Every element but the empty ones (BR, SBR, TAB, IMG) opens a stack entry holding the format around it. B, I
    /// and U set bold, italic and underline; FONT sets COLOR (#RRGGBB) and SIZE (a whole number, 0 or more). An end tag
    /// closes the top entry when the names match and is ignored otherwise, except that an unmatched end P or LI closes
    /// everything up to its own element.</item>
    /// <item>BR is a line break in a multiline field only; TAB is a tab.</item>
    /// <item>IMG is a space in the field's default text format (not the one of the elements around it) carrying the tag's
    /// image, and the URL of an A around it (sfr_render_05i.cpp:1060).</item>
    /// <item>A sets the URL from HREF (HTML_A, :1107); the text after the end A has none (the default format's URL is
    /// cleared, :912). No style sheet is modelled, so no a / a:link CSS style.</item>
    /// <item>In a multiline field a P opened while another P is open starts a new line, an LI starts one unless its
    /// paragraph is still empty, and the end of a P (not an empty P element) or of an LI ends the line.</item>
    /// <item>Entities are decoded.</item>
    /// <item>A line break is in lastFormat: the format of the last element opened or text added (an end tag leaves it).</item>
    /// </list>
    /// A line break is <paramref name="newLine"/>. With <paramref name="literalNewLines"/> a backslash-n (the game strings'
    /// line break) is one too. The formats are per code point, or with <paramref name="perChar"/> per UTF-16 char.
    /// </summary>
    public static (string Text, HtmlFormat[] Formats) Parse(string html, bool multiline = true, char newLine = '\n', bool literalNewLines = false,
        bool perChar = false)
    {
        html ??= "";
        var p = new Parser(multiline, newLine, literalNewLines, perChar, html.Length);
        p.Feed(html);
        var formats = new HtmlFormat[p.Count];
        Array.Copy(p.Formats, formats, p.Count);
        return (p.Text(0), formats);
    }

    /// <summary>
    /// <see cref="Parse"/> as a state that html can be fed to a piece at a time: what DocView::AppendHtml does
    /// (sfr_render_03b.cpp:277 / :287 parse only the appended string into the document, StyledText::ParseHtml,
    /// sfr_render_05i.cpp:1712 / :1721, then notify the change). Feeding the html of a text one piece after another gives
    /// the text and formats parsing the pieces' concatenation gives, as long as <see cref="Continuable"/> held after each:
    /// the elements still open, the format around them and the last element's format (what a line break takes) carry over.
    /// </summary>
    public sealed class Parser
    {
        readonly bool multiline, literalNewLines, perChar;
        readonly char newLine;
        readonly StringBuilder text;
        HtmlFormat[] styles;
        readonly List<(string Name, HtmlFormat Around)> stack = new();
        HtmlFormat current;
        HtmlFormat last;           // lastFormat: the last opened element's or content's format, which line breaks take
        int openP;

        public Parser(bool multiline, char newLine, bool literalNewLines, bool perChar, int capacity = 0)
        {
            (this.multiline, this.newLine, this.literalNewLines, this.perChar) = (multiline, newLine, literalNewLines, perChar);
            text = new StringBuilder(capacity);
            styles = new HtmlFormat[Math.Max(16, capacity)];
        }

        /// <summary>The formats per code point (per UTF-16 char with perChar); the array is longer than <see cref="Count"/>, the rest
        /// is default (the field's own format).</summary>
        public HtmlFormat[] Formats => styles;
        public int Count { get; private set; }
        /// <summary>The length of the text so far, line break at its end included.</summary>
        public int Length => text.Length;
        /// <summary>True when some format is not plain.</summary>
        public bool Styled { get; private set; }
        /// <summary>False when the html fed last ended where the next piece could change what it read: inside a tag with no closing
        /// bracket, an entity that a semicolon could end, or a backslash a literal line break could follow. Parsing the pieces
        /// as one text is then not the same as feeding them one after another.</summary>
        public bool Continuable { get; private set; } = true;

        /// <summary>The text so far without its last <paramref name="drop"/> chars.</summary>
        public string Text(int drop) => text.ToString(0, text.Length - drop);
        /// <summary>The char at <paramref name="i"/> of the text so far.</summary>
        public char At(int i) => text[i];

        void Add(char c, HtmlFormat f)
        {
            if (!perChar && char.IsLowSurrogate(c) && text.Length > 0 && char.IsHighSurrogate(text[^1])) { text.Append(c); return; }
            text.Append(c);
            if (Count == styles.Length)
            {
                var grown = new HtmlFormat[styles.Length * 2];
                Array.Copy(styles, grown, Count);
                styles = grown;
            }
            styles[Count++] = f;
            if (!f.IsPlain) Styled = true;
        }
        void Content(char c) { Add(c, current); last = current; }
        void NewLine() { if (multiline) Add(newLine, last); }
        bool ParagraphEmpty() => text.Length == 0 || text[^1] == newLine;

        /// <summary>Parses <paramref name="html"/> onto the end of the text.</summary>
        public void Feed(string html)
        {
            if (literalNewLines)
            {
                html = html.Replace("\\n", "\n");
                if (html.EndsWith('\\')) Continuable = false;
            }
            for (int i = 0; i < html.Length; i++)
            {
                char c = html[i];
                if (c == '<')
                {
                    if (html.IndexOf('>', i + 1) is int end and > 0)
                    {
                        Tag(html.Substring(i + 1, end - i - 1));
                        i = end;
                        continue;
                    }
                    Continuable = false;
                }
                if (c == '&')
                {
                    if (Entity(html, i, out string decoded, out int length))
                    {
                        foreach (char d in decoded) Content(d);
                        i += length - 1;
                        continue;
                    }
                    if (html.Length - i <= 11) Continuable = false;
                }
                Content(c);
            }
        }


        void Tag(string body)
        {
            bool closing = body.StartsWith('/');
            string rest = closing ? body[1..] : body;
            int n = 0;
            while (n < rest.Length && char.IsLetter(rest[n])) n++;
            string name = rest[..n].ToUpperInvariant();
            bool selfClosing = !closing && rest.TrimEnd().EndsWith('/');
            if (closing) { End(name, false); return; }
            switch (name)
            {
                case "BR": case "SBR": NewLine(); return;
                case "TAB": Content('\t'); return;
                case "IMG":
                {
                    // HTML_IMG: the image char's format is the default text format with the image desc
                    Add(' ', new HtmlFormat { Image = Image(rest[n..]), Url = current.Url });
                    last = current;
                    return;
                }
            }
            stack.Add((name, current));
            switch (name)
            {
                case "B": current = current with { Bold = true }; break;
                case "I": current = current with { Italic = true }; break;
                case "U": current = current with { Underline = true }; break;
                case "A":
                    if (Attribute(rest[n..], "href") is { } href) current = current with { Url = href.Length > 0 ? href : null };
                    break;
                case "FONT":
                    if (Attribute(rest[n..], "color") is { } hex && ParseHex(hex) is uint col) current = current with { Color = col };
                    if (Attribute(rest[n..], "size") is { } sz && int.TryParse(sz.Trim(), out int size) && size >= 0) current = current with { Size = size };
                    break;
                case "P":
                    if (openP > 0) NewLine();                               // a P inside an open P starts a paragraph
                    openP++;
                    break;
                case "LI":
                    if (!ParagraphEmpty()) NewLine();
                    break;
            }
            last = current;
            if (selfClosing) End(name, true);
        }

        // SGMLPS_END_ELEMENT / SGMLPS_EMPTY_ELEMENT_FINISHED
        void End(string name, bool emptyElement)
        {
            if (stack.Count == 0) return;
            if (stack[^1].Name != name)
            {
                if (!((name == "P" || name == "LI") && openP > 0)) return;     // a mismatched end tag is ignored
                while (stack.Count > 0 && stack[^1].Name != name) stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0) return;
            }
            current = stack[^1].Around;
            stack.RemoveAt(stack.Count - 1);
            if (name == "P") { openP--; if (!emptyElement) NewLine(); }   // an empty P element makes no paragraph
            else if (name == "LI") NewLine();
        }
    }


    /// <summary>The field defaults <see cref="Build"/> writes: TextField::SetTextFieldDef's default text format (font
    /// name, size, colour; kerning off) and paragraph format (alignment; margins, indent and leading when set).</summary>
    public readonly record struct Defaults(string Font, double Size, uint Color, string Align,
        double? LeftMargin = null, double? RightMargin = null, double? Indent = null, double? Leading = null);

    /// <summary>
    /// StyledText::GetHtml (sfr_render_01c.cpp:1308): one TEXTFORMAT + P block per paragraph of <paramref name="text"/>
    /// (each ending at a CR or LF, as StyledText::AppendString splits them; a last paragraph that is empty is left out), a FONT tag opened at the first run
    /// and again whenever the font attributes change (IsHTMLFontTagSame), B / I / U around each run, an IMG tag per image
    /// char, an A HREF around each run or image with a URL (inside the FONT, outside B / I / U), and the text escaped. <paramref name="formats"/> (per code point, null: all the defaults) is merged over
    /// <paramref name="d"/>. Not modelled: FACE per run, LETTERSPACING, ALPHA, LI paragraphs, BLOCKINDENT and
    /// TABSTOPS.
    /// </summary>
    public static string Build(string text, HtmlFormat[] formats, Defaults d)
    {
        var html = new StringBuilder();
        text ??= "";
        var paras = text.Split('\r', '\n');
        string align = (d.Align ?? "left").ToLowerInvariant() switch
        {
            "right" => "RIGHT", "center" => "CENTER", "justify" => "JUSTIFY", _ => "LEFT",
        };
        static string N(double v) => ((int)v).ToString(CultureInfo.InvariantCulture);
        int at = 0;
        for (int np = 0; np < paras.Length; np++)
        {
            string p = paras[np];
            int start = at;
            at += p.Length + 1;
            if (np + 1 == paras.Length && p.Length == 0) continue;
            html.Append("<TEXTFORMAT");
            if (d.Indent is double ind) html.Append(" INDENT=\"").Append(N(ind)).Append('"');
            if (d.LeftMargin is double lm) html.Append(" LEFTMARGIN=\"").Append(N(lm)).Append('"');
            if (d.RightMargin is double rm) html.Append(" RIGHTMARGIN=\"").Append(N(rm)).Append('"');
            if (d.Leading is double ld) html.Append(" LEADING=\"").Append(N(ld)).Append('"');
            html.Append("><P ALIGN=\"").Append(align).Append("\">");
            // the paragraph's runs, its newline included (its format may open the FONT of an otherwise empty paragraph)
            int len = Math.Min(p.Length + 1, text.Length - start);
            bool fontOpen = false;
            (uint Color, int Size)? prev = null;
            int i = 0;
            while (i < len)
            {
                var f = At(start + i);
                int j = i + 1;
                while (j < len && At(start + j) == f) j++;
                if (f.Image is HtmlImage im)
                {
                    for (int k = i; k < j; k++)
                    {
                        if (f.Url != null) html.Append("<A HREF=\"").Append(f.Url).Append("\">");
                        Img(im);
                        if (f.Url != null) html.Append("</A>");
                    }
                }
                else
                {
                    var font = (f.Color ?? d.Color, f.Size ?? (int)d.Size);
                    if (fontOpen && prev != font) { html.Append("</FONT>"); fontOpen = false; }
                    if (!fontOpen)
                    {
                        html.Append("<FONT");
                        if (!string.IsNullOrEmpty(d.Font)) html.Append(" FACE=\"").Append(d.Font).Append('"');
                        html.Append(" SIZE=\"").Append((uint)font.Item2).Append('"');
                        html.Append(" COLOR=\"#").Append((font.Item1 & 0xFFFFFF).ToString("X6")).Append('"');
                        html.Append(" KERNING=\"0\">");
                        fontOpen = true;
                    }
                    if (f.Url != null) html.Append("<A HREF=\"").Append(f.Url).Append("\">");
                    if (f.Bold) html.Append("<B>");
                    if (f.Italic) html.Append("<I>");
                    if (f.Underline) html.Append("<U>");
                    prev = font;
                    for (int k = i; k < j; k++)
                    {
                        char c = text[start + k];
                        if (c is '\r' or '\n' or '\0') continue;            // line breaks are implied by the paragraphs
                        html.Append(c switch
                        {
                            '<' => "&lt;", '>' => "&gt;", '&' => "&amp;", '"' => "&quot;", '\'' => "&apos;", '\u00a0' => "&nbsp;",
                            _ => c.ToString(),
                        });
                    }
                    if (f.Underline) html.Append("</U>");
                    if (f.Italic) html.Append("</I>");
                    if (f.Bold) html.Append("</B>");
                    if (f.Url != null) html.Append("</A>");
                }
                i = j;
            }
            if (fontOpen) html.Append("</FONT>");
            html.Append("</P></TEXTFORMAT>");
        }
        return html.ToString();

        HtmlFormat At(int i) => formats != null && i < formats.Length ? formats[i] : default;

        void Img(HtmlImage im)
        {
            html.Append("<IMG SRC=\"").Append(im.Src).Append('"');
            if (im.Width > 0) html.Append(" WIDTH=\"").Append(im.Width).Append('"');
            if (im.Height > 0) html.Append(" HEIGHT=\"").Append(im.Height).Append('"');
            if (im.VSpace != 0) html.Append(" VSPACE=\"").Append(im.VSpace).Append('"');
            if (im.HSpace != 0) html.Append(" HSPACE=\"").Append(im.HSpace).Append('"');
            if (!string.IsNullOrEmpty(im.Id)) html.Append(" ID=\"").Append(im.Id).Append('"');
            html.Append(" ALIGN=\"").Append(im.Align switch { HtmlImageAlign.Left => "left", HtmlImageAlign.Right => "right", _ => "baseline" }).Append("\">");
        }
    }

    /// <summary>The IMG attributes (ParseHtml's HTML_SRC ... HTML_HSPACE cases; a value ParseInt rejects is skipped).</summary>
    static HtmlImage Image(string attrs)
    {
        var align = (Attribute(attrs, "align") ?? "").Trim().ToUpperInvariant() switch
        {
            "LEFT" => HtmlImageAlign.Left, "RIGHT" => HtmlImageAlign.Right, _ => HtmlImageAlign.BaseLine,
        };
        int w = ParseInt(Attribute(attrs, "width")) is int a && a >= 0 ? a : 0, h = ParseInt(Attribute(attrs, "height")) is int b && b >= 0 ? b : 0;
        return new HtmlImage(Attribute(attrs, "src") ?? "", Attribute(attrs, "id") ?? "", align, w, h,
            ParseInt(Attribute(attrs, "vspace")) ?? 0, ParseInt(Attribute(attrs, "hspace")) ?? 0);
    }

    /// <summary>SGMLParser::ParseInt (sfr_render_05i.cpp:590): an optional sign and decimal digits only (a lone sign is 0).</summary>
    public static int? ParseInt(string v)
    {
        if (string.IsNullOrEmpty(v)) return null;
        int i = v[0] is '-' or '+' ? 1 : 0, sign = v[0] == '-' ? -1 : 1, r = 0;
        for (; i < v.Length; i++)
        {
            if (v[i] is < '0' or > '9') return null;
            r = unchecked(r * 10 + (v[i] - '0'));
        }
        return r * sign;
    }

    static string Attribute(string attrs, string name)
    {
        var m = Regex.Match(attrs, "\\b" + name + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>/]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }

    /// <summary>
    /// COLOR="#..." (ParseHtml's HTML_COLOR case, sfr_render_05i.cpp:1300): after a '#', SGMLParser::ParseHexInt
    /// (text_sgmlparser.h:182) takes any number of hex digits (none, or any other character, fails), shifting each into a
    /// 32-bit value; TextFormat::SetColor32 (text_core.h:258) keeps its low 24 bits. So "#ff5ad35f", the item grade colours'
    /// 0xffRRGGBB, is 0x5ad35f.
    /// </summary>
    static uint? ParseHex(string hex)
    {
        hex = hex.Trim();
        if (!hex.StartsWith('#') || hex.Length < 2) return null;
        uint v = 0;
        foreach (char c in hex.AsSpan(1))
        {
            if (!Uri.IsHexDigit(c)) return null;
            v = (v << 4) | (uint)Convert.ToInt32(c.ToString(), 16);
        }
        return v & 0xFFFFFF;
    }

    static bool Entity(string s, int at, out string decoded, out int length)
    {
        decoded = null; length = 0;
        int semi = s.IndexOf(';', at + 1);
        if (semi < 0 || semi - at > 10) return false;
        string name = s.Substring(at + 1, semi - at - 1);
        decoded = name switch
        {
            "lt" => "<", "gt" => ">", "amp" => "&", "quot" => "\"", "apos" => "'", "nbsp" => "\u00a0",
            _ when name.StartsWith("#x", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[2..], NumberStyles.HexNumber, null, out int h) && h is > 0 and <= 0x10ffff and not (>= 0xd800 and <= 0xdfff) => char.ConvertFromUtf32(h),
            _ when name.StartsWith('#') && int.TryParse(name[1..], out int dd) && dd is > 0 and <= 0x10ffff and not (>= 0xd800 and <= 0xdfff) => char.ConvertFromUtf32(dd),
            _ => null,
        };
        if (decoded == null) return false;
        length = semi - at + 1;
        return true;
    }
}
