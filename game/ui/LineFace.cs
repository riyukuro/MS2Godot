using Godot;
using Godot.Collections;

namespace Ms2Godot.Ui;

/// <summary>
/// The face of a text field's label drawn line by line, for a label of more than one line whose lines Scaleform places
/// each at its own exact x (TextNode.AlignLine): the label's own face is hidden and this draws the same glyphs where the
/// label lays them out, each line moved by TextNode.LineDx. A line is its text shaped with the label's font and size,
/// the pen of each glyph cluster is the label's character bounds and the baseline is the line top plus the line ascent
/// centred in the font height (as GlyphShadow places its glyphs); drawn with no move it is the label's own face, pixel
/// for pixel. A label showing htmlText colours (TextNode.SetHtml) is drawn here too, each glyph in its run's colour. It
/// draws nothing while the label shows its own face.
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
                if (index != 0)
                {
                    var o = (Vector2)g["offset"];
                    ts.FontDrawGlyph((Rid)g["font_rid"], canvas, (long)g["font_size"], new Vector2(pen + dx + o.X, baseline + o.Y), index, text.GlyphColor(at));
                }
                pen += advance * repeat;
            }
            tl.Dispose();
        }
    }
}
