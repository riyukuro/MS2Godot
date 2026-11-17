using System.Text;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// One code point's text format from htmlText: its FONT colour (null: the field's own), bold, italic and underline, and its
/// FONT size (null: the field's own).
/// </summary>
public readonly record struct GlyphStyle(Color? Color, bool Bold, bool Italic, bool Underline, float? Size)
{
    public bool IsPlain => Color == null && !Bold && !Italic && !Underline && Size == null;
}

/// <summary>
/// A Flash text field's htmlText as a TextNode shows it (TextNode.SetHtml), parsed the way Scaleform's StyledText::ParseHtml
/// does it (sfr_render_05i.cpp): the text without its markup and each code point's text format.
/// <list type="bullet">
/// <item>Every element but the empty ones (BR, SBR, TAB) opens a stack entry holding the format around it. B, I and U set
/// bold, italic and underline; FONT sets COLOR (#RRGGBB) and SIZE (a whole number, 0 or more). An end tag closes the top
/// entry when the names match and is ignored otherwise, except that an unmatched end P or LI closes everything up to its
/// own element.</item>
/// <item>BR is a line break in a multiline field only; TAB is a tab.</item>
/// <item>In a multiline field a P opened while another P is open starts a new line, an LI starts one unless its paragraph is
/// still empty, and the end of a P (not an empty P element) or of an LI ends the line.</item>
/// <item>Entities are decoded.</item>
/// </list>
/// The game's strings write a line break as a literal backslash-n; it is a line break here as in the strings the client
/// shows. A string with no tag and no entity is the text itself.
/// </summary>
public static class FlashHtml
{
    /// <summary>The text and, per code point, the text format it is in.</summary>
    public static (string Text, GlyphStyle[] Styles) Parse(string html, bool multiline = true)
    {
        html = (html ?? "").Replace("\\n", "\n");
        var text = new StringBuilder(html.Length);
        var styles = new List<GlyphStyle>(html.Length);
        var stack = new List<(string Name, GlyphStyle Around)>();
        var current = default(GlyphStyle);
        int openP = 0;
        void Add(char c)
        {
            if (char.IsLowSurrogate(c) && text.Length > 0 && char.IsHighSurrogate(text[^1])) { text.Append(c); return; }
            text.Append(c);
            styles.Add(current);
        }
        void NewLine() { if (multiline) Add('\n'); }
        bool ParagraphEmpty() => text.Length == 0 || text[^1] == '\n';
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
                foreach (char d in decoded) Add(d);
                i += length - 1;
                continue;
            }
            Add(c);
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
                case "TAB": Add('\t'); return;
            }
            stack.Add((name, current));
            switch (name)
            {
                case "B": current = current with { Bold = true }; break;
                case "I": current = current with { Italic = true }; break;
                case "U": current = current with { Underline = true }; break;
                case "FONT":
                    if (Attribute(rest[n..], "color") is { } hex && ParseHex(hex) is Color col) current = current with { Color = col };
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

    /// <summary>The text with its markup taken out (Parse without the formats).</summary>
    public static string Plain(string html, bool multiline = true) => Parse(html, multiline).Text;

    static string Attribute(string attrs, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(attrs, "\\b" + name + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>/]+))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }

    /// <summary>COLOR="#RRGGBB" (the client parses the colour only after a '#').</summary>
    static Color? ParseHex(string hex)
    {
        hex = hex.Trim();
        if (!hex.StartsWith('#')) return null;
        hex = hex[1..];
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) return null;
        return Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
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
            _ when name.StartsWith("#x", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out int h) && h is > 0 and <= 0x10ffff and not (>= 0xd800 and <= 0xdfff) => char.ConvertFromUtf32(h),
            _ when name.StartsWith('#') && int.TryParse(name[1..], out int d) && d is > 0 and <= 0x10ffff and not (>= 0xd800 and <= 0xdfff) => char.ConvertFromUtf32(d),
            _ => null,
        };
        if (decoded == null) return false;
        length = semi - at + 1;
        return true;
    }
}
