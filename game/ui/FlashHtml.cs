global using HtmlImage = Ms2.Avm2.Flash.HtmlImage;
using Godot;
using Ms2.Avm2.Flash;

namespace Ms2Godot.Ui;

/// <summary>
/// One code point's text format from htmlText: its FONT colour (null: the field's own), bold, italic and underline, and its
/// FONT size (null: the field's own).
/// </summary>
public readonly record struct GlyphStyle(Color? Color, bool Bold, bool Italic, bool Underline, float? Size)
{
    /// <summary>The IMG tag this code point stands for (the space ParseHtml appends for it), null for text.</summary>
    public HtmlImage Image { get; init; }

    public bool IsPlain => Color == null && !Bold && !Italic && !Underline && Size == null && Image == null;
}

/// <summary>
/// A Flash text field's htmlText as a TextNode shows it (TextNode.SetHtml): Scaleform's StyledText::ParseHtml
/// (<see cref="HtmlText.Parse"/>) with the formats per code point. The game's strings write a line break as a literal
/// backslash-n; it is a line break here as in the strings the client shows.
/// </summary>
public static class FlashHtml
{
    /// <summary>The text and, per code point, the text format it is in (<see cref="HtmlText.Parse"/>).</summary>
    public static (string Text, GlyphStyle[] Styles) Parse(string html, bool multiline = true)
    {
        var (text, formats) = HtmlText.Parse(html, multiline, '\n', literalNewLines: true);
        var styles = new GlyphStyle[formats.Length];
        for (int i = 0; i < formats.Length; i++)
        {
            var f = formats[i];
            Color? color = f.Color is uint rgb ? Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : null;
            styles[i] = new GlyphStyle(color, f.Bold, f.Italic, f.Underline, f.Size) { Image = f.Image };
        }
        return (text, styles);
    }

    /// <summary>The text with its markup taken out (Parse without the formats).</summary>
    public static string Plain(string html, bool multiline = true) => Parse(html, multiline).Text;
}
