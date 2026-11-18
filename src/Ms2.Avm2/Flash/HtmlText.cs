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
/// underline, its FONT SIZE (null: the field's own) and the IMG tag it stands for (null for text).
/// </summary>
public readonly record struct HtmlFormat(uint? Color, bool Bold, bool Italic, bool Underline, int? Size)
{
    public HtmlImage Image { get; init; }

    public bool IsPlain => Color == null && !Bold && !Italic && !Underline && Size == null && Image == null;
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
    /// image.</item>
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
        if (literalNewLines) html = html.Replace("\\n", "\n");
        var text = new StringBuilder(html.Length);
        var styles = new List<HtmlFormat>(html.Length);
        var stack = new List<(string Name, HtmlFormat Around)>();
        var current = default(HtmlFormat);
        var last = current;           // lastFormat: the last opened element's or content's format, which line breaks take
        int openP = 0;
        void Add(char c, HtmlFormat f)
        {
            if (!perChar && char.IsLowSurrogate(c) && text.Length > 0 && char.IsHighSurrogate(text[^1])) { text.Append(c); return; }
            text.Append(c);
            styles.Add(f);
        }
        void Content(char c) { Add(c, current); last = current; }
        void NewLine() { if (multiline) Add(newLine, last); }
        bool ParagraphEmpty() => text.Length == 0 || text[^1] == newLine;
        for (int i = 0; i < html.Length; i++)
        {
            char c = html[i];
            if (c == '<' && html.IndexOf('>', i + 1) is int end and > 0)
            {
                Tag(html.Substring(i + 1, end - i - 1));
                i = end;
                continue;
            }
            if (c == '&' && Entity(html, i, out string decoded, out int length))
            {
                foreach (char d in decoded) Content(d);
                i += length - 1;
                continue;
            }
            Content(c);
        }
        return (text.ToString(), styles.ToArray());

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
                    Add(' ', new HtmlFormat { Image = Image(rest[n..]) });
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
    /// char, and the text escaped. <paramref name="formats"/> (per code point, null: all the defaults) is merged over
    /// <paramref name="d"/>. Not modelled: FACE per run, LETTERSPACING, ALPHA, A HREF, LI paragraphs, BLOCKINDENT and
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
                    for (int k = i; k < j; k++) Img(im);
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
