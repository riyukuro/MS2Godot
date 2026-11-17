using System.Text;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// A Flash text field's htmlText as a TextNode shows it (TextNode.SetHtml): the text without its markup and the colour of
/// each code point. The game's strings mark colour with FONT color, which nests (a closing FONT goes back to the colour
/// around it), and break lines with BR. Entities are decoded. The other tags (B, I, U, FONT size, P, A, IMG, LI,
/// TEXTFORMAT) are dropped and their text is kept, in the field's own face. A string with no tag and no entity is the
/// text itself.
/// </summary>
public static class FlashHtml
{
    /// <summary>
    /// The text and, per code point, the FONT colour it is in (null: the field's own colour). The game's strings write a
    /// line break as a literal backslash-n; it is a line break here as in the strings the client shows.
    /// </summary>
    public static (string Text, Color?[] Colors) Parse(string html)
    {
        html = (html ?? "").Replace("\\n", "\n");
        var text = new StringBuilder(html.Length);
        var colors = new List<Color?>(html.Length);
        var fonts = new Stack<Color?>();
        Color? current = null;
        void Add(char c)
        {
            if (char.IsLowSurrogate(c) && text.Length > 0 && char.IsHighSurrogate(text[^1])) { text.Append(c); return; }
            text.Append(c);
            colors.Add(current);
        }
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
        return (text.ToString(), colors.ToArray());

        void Tag(string body)
        {
            bool closing = body.StartsWith('/');
            string rest = closing ? body[1..] : body;
            int n = 0;
            while (n < rest.Length && char.IsLetter(rest[n])) n++;
            string name = rest[..n].ToUpperInvariant();
            if (name == "FONT")
            {
                if (closing) current = fonts.Count > 0 ? fonts.Pop() : null;
                else { fonts.Push(current); current = Attribute(rest[n..], "color") is { } hex && ParseHex(hex) is Color col ? col : current; }
            }
            else if (!closing && (name == "BR" || name == "SBR")) Add('\n');
        }
    }

    /// <summary>The text with its markup taken out (Parse without the colours).</summary>
    public static string Plain(string html) => Parse(html).Text;

    static string Attribute(string attrs, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(attrs, "\\b" + name + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }

    static Color? ParseHex(string hex)
    {
        hex = hex.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
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
