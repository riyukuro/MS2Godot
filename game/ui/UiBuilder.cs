using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot.Ui;

public static class UiBuilder
{
    public static Transform2D ToTransform(JsonNode m)
    {
        if (m == null) return Transform2D.Identity;
        float x = F(m["x"], 0), y = F(m["y"], 0), a = F(m["a"], 1), d = F(m["d"], 1), b = F(m["b"], 0), c = F(m["c"], 0);
        return new Transform2D(new Vector2(a, b), new Vector2(c, d), new Vector2(x, y));
    }

    public static float F(JsonNode n, float def) => n == null ? def : (float)(double)n;

    public static Rect2 Rc(JsonNode r) => r == null ? new Rect2() : new Rect2(F(r["x"], 0), F(r["y"], 0), F(r["w"], 0), F(r["h"], 0));

    public static Color ParseColor(string hex, Color def)
    {
        if (string.IsNullOrEmpty(hex)) return def;
        return Color.FromHtml(hex);
    }

    public static MovieClipNode BuildSprite(UiMovie movie, UiScope scope, int id, double sx, double sy, NineCtx nine = null)
    {
        var sym = scope.Symbol(id);
        if (sym == null) return null;
        var n = new MovieClipNode(movie, scope, sym["timeline"] as JsonObject, sym, sx, sy, nine) { SymbolId = id };
        n.Build();
        return n;
    }

    /// <summary>Builds a display object for a character id placed with the given accumulated scale.</summary>
    public static Node2D Build(UiMovie movie, UiScope scope, int id, double sx, double sy, NineCtx nine = null)
    {
        var sym = scope.Symbol(id);
        if (sym == null) return null;
        switch ((string)sym["kind"])
        {
            case "sprite": return BuildSprite(movie, scope, id, sx, sy, nine);
            case "shape": return new ShapeNode(scope, sym, nine);
            case "text": return new TextNode(scope, sym, sx, sy, nine);
            default: return null;
        }
    }
}

/// <summary>
/// Scale-9 context of a sprite placed with a non-unit scale: positions and sizes inside it are mapped piecewise so that
/// the corners and edges of the grid keep their size. <see cref="Offset"/> is where the current node's origin lies in the sprite's space.
/// </summary>
public sealed class NineCtx
{
    public Rect2 Grid, Bounds;
    public float Sx, Sy;
    public Vector2 Offset;

    public NineCtx At(Vector2 offset) => new() { Grid = Grid, Bounds = Bounds, Sx = Sx, Sy = Sy, Offset = offset };

    public float MapX(float x) => Map1(x, Bounds.Position.X, Bounds.Size.X, Grid.Position.X, Grid.End.X, Grid.Size.X, Bounds.Size.X * Sx);

    public float MapY(float y) => Map1(y, Bounds.Position.Y, Bounds.Size.Y, Grid.Position.Y, Grid.End.Y, Grid.Size.Y, Bounds.Size.Y * Sy);

    /// <summary>
    /// One axis of Scale9GridInfo's cell matrices: the bounds (b1, length bw) are drawn <paramref name="size"/> long; the
    /// margins before g1 and after g2 keep their length and the grid takes the rest. Margins that would overlap (together
    /// longer than the size) both shrink by their sum over the size plus 0.05, which leaves the grid a sliver.
    /// </summary>
    static float Map1(float x, float b1, float bw, float g1, float g2, float gw, float size)
    {
        float m1 = g1 - b1, m2 = b1 + bw - g2, s = size == 0 ? 0.001f : size, d = m1 / s + m2 / s;
        if (d <= 1)
        {
            if (x <= g1) return x;
            if (x >= g2) return x + (size - bw);
            return g1 + (x - g1) * ((size - (bw - gw)) / Math.Max(gw, 0.001f));
        }
        d += 0.05f;
        float l = m1 / d, r = m2 / d;
        // written as the unshrunk value plus the shrink's correction, so the bounds' own edges land where they did
        if (x <= g1) return m1 > 0 ? x - (x - b1) * (1 - l / m1) : x;
        if (x >= g2) return x + (size - bw) + (m2 > 0 ? (b1 + bw - x) * (1 - r / m2) : 0);
        return b1 + l + (x - g1) * (s - l - r) / Math.Max(gw, 0.001f);
    }

    /// <summary>A rect given in the current node's space, mapped, in the current node's space.</summary>
    public Rect2 Map(Rect2 local)
    {
        var r = new Rect2(local.Position + Offset, local.Size);
        float x0 = MapX(r.Position.X), x1 = MapX(r.End.X), y0 = MapY(r.Position.Y), y1 = MapY(r.End.Y);
        return new Rect2(x0 - Offset.X, y0 - Offset.Y, x1 - x0, y1 - y0);
    }
}

/// <summary>A shape with bitmap fills: each fill is a region of a DDS atlas drawn at the fill's position and scale.</summary>
public partial class ShapeNode : Node2D
{
    public ShapeNode(UiScope scope, JsonObject sym)
    {
        if (sym["bitmaps"] is not JsonArray fills) return;
        foreach (var f in fills)
        {
            var bm = scope.Bitmap((int)f["id"]);
            if (bm == null) continue;
            var m = f["matrix"];
            float sx = UiBuilder.F(m?["a"], 1) / 20f, sy = UiBuilder.F(m?["d"], 1) / 20f;
            var spr = new Sprite2D
            {
                Texture = bm.Value.Tex, RegionEnabled = true, RegionRect = bm.Value.Region, Centered = false,
                Position = new Vector2(UiBuilder.F(m?["x"], 0), UiBuilder.F(m?["y"], 0)), Scale = new Vector2(sx, sy),
                TextureFilter = (bool)f["smooth"] ? TextureFilterEnum.Linear : TextureFilterEnum.Nearest,
            };
            AddChild(spr);
        }
    }
}

/// <summary>A shape with bitmap fills: each fill is a region of a DDS atlas drawn at the fill's position and scale (nine-sliced inside a scale-9 sprite).</summary>
public partial class ShapeNode : Node2D
{
    readonly List<(Texture2D Tex, Rect2 Dest, Rect2 Src)> pieces = new();
    readonly NineCtx nine;
    readonly List<(Color Color, Vector2[] Pts)> paths = new();

    public ShapeNode(UiScope scope, JsonObject sym, NineCtx nine = null)
    {
        this.nine = nine;
        if (sym["paths"] is JsonArray ps)
            foreach (var p in ps)
            {
                var a = (JsonArray)p["pts"];
                var pts = new Vector2[a.Count / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((float)(double)a[2 * i], (float)(double)a[2 * i + 1]);
                paths.Add((UiBuilder.ParseColor((string)p["fill"], Colors.White), pts));
            }
        if (sym["bitmaps"] is not JsonArray fills) return;
        foreach (var f in fills)
        {
            var bm = scope.Bitmap((int)f["id"]);
            if (bm == null) continue;
            var m = f["matrix"];
            float sx = UiBuilder.F(m?["a"], 1) / 20f, sy = UiBuilder.F(m?["d"], 1) / 20f;
            var pos = new Vector2(UiBuilder.F(m?["x"], 0), UiBuilder.F(m?["y"], 0));
            if (nine != null)
            {
                pieces.Add((bm.Value.Tex, new Rect2(pos, new Vector2(bm.Value.Region.Size.X * sx, bm.Value.Region.Size.Y * sy)), bm.Value.Region));
                continue;
            }
            AddChild(new Sprite2D
            {
                Texture = bm.Value.Tex, RegionEnabled = true, RegionRect = bm.Value.Region, Centered = false, Position = pos, Scale = new Vector2(sx, sy),
                TextureFilter = (bool)f["smooth"] ? TextureFilterEnum.Linear : TextureFilterEnum.Nearest,
            });
        }
    }

    public override void _Draw()
    {
        foreach (var (color, pts) in paths)
        {
            var poly = nine == null ? pts : pts.Select(q => new Vector2(nine.MapX(q.X + nine.Offset.X) - nine.Offset.X, nine.MapY(q.Y + nine.Offset.Y) - nine.Offset.Y)).ToArray();
            if (Geometry2D.TriangulatePolygon(poly).Length > 0) DrawColoredPolygon(poly, color);
        }
        if (nine == null || pieces.Count == 0) return;
        var g = nine.Grid;
        float[] xs = { float.NegativeInfinity, g.Position.X, g.End.X, float.PositiveInfinity };
        float[] ys = { float.NegativeInfinity, g.Position.Y, g.End.Y, float.PositiveInfinity };
        foreach (var (tex, dest0, src) in pieces)
        {
            var dest = new Rect2(dest0.Position + nine.Offset, dest0.Size);    // sprite space
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    float x0 = Math.Max(dest.Position.X, xs[i]), x1 = Math.Min(dest.End.X, xs[i + 1]);
                    float y0 = Math.Max(dest.Position.Y, ys[j]), y1 = Math.Min(dest.End.Y, ys[j + 1]);
                    if (x1 <= x0 || y1 <= y0) continue;
                    float fx = src.Size.X / dest.Size.X, fy = src.Size.Y / dest.Size.Y;
                    var s = new Rect2(src.Position.X + (x0 - dest.Position.X) * fx, src.Position.Y + (y0 - dest.Position.Y) * fy, (x1 - x0) * fx, (y1 - y0) * fy);
                    var d = new Rect2(nine.MapX(x0) - nine.Offset.X, nine.MapY(y0) - nine.Offset.Y, nine.MapX(x1) - nine.MapX(x0), nine.MapY(y1) - nine.MapY(y0));
                    DrawTextureRectRegion(tex, d, s);
                }
        }
    }
}

/// <summary>A flash text field drawn with a Godot Label. The game font is not loadable here, so a system font stands in.</summary>
public partial class TextNode : Node2D
{
    public static readonly string[] FontNames = { "Noto Sans CJK KR", "Noto Sans CJK JP", "Noto Sans", "DejaVu Sans", "sans-serif" };
    public Label Label;
    public Rect2 Bounds;
    public JsonNode Def;

    public TextNode(UiScope scope, JsonObject sym, double sx, double sy, NineCtx nine = null)
    {
        Def = sym["text"];
        Bounds = UiBuilder.Rc(sym["bounds"]);
        if (nine != null) Bounds = nine.Map(Bounds);
        var font = scope.Assets.FontFor((string)Def["fontClass"]);
        lineRatios = scope.Assets.LineRatios((string)Def["fontClass"]);
        AscentRatio = scope.Assets.AscentRatio((string)Def["fontClass"]);
        DescentRatio = lineRatios.Height - AscentRatio;
        float size = UiBuilder.F(Def["size"], 12);
        float asx = (float)sx, asy = (float)sy;
        // Scaleform's glyph cache rasterizes plain text (no readability AA) once at a ramp size and draws that raster scaled
        // to the screen size with linear filtering, unsnapped; the label does the same: font size = ramp, scale = screen / ramp
        float screen = size * asy, ramp = RampSize(screen);
        RasterScale = screen / ramp;
        ScreenScale = asy;
        Label = new Label
        {
            Position = new Vector2(Bounds.Position.X * asx, Bounds.Position.Y * asy),
            Size = new Vector2(Bounds.Size.X * asx, Bounds.Size.Y * asy) / RasterScale,
            HorizontalAlignment = (string)Def["align"] switch { "center" => HorizontalAlignment.Center, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left },
            VerticalAlignment = VerticalAlignment.Top, ClipText = false, MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = (bool)(Def["wordWrap"] ?? false) ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            LabelSettings = new LabelSettings { Font = font, FontSize = (int)ramp, FontColor = UiBuilder.ParseColor((string)Def["color"], Colors.Black) },
            Scale = new Vector2(RasterScale / asx, RasterScale / asy), Text = scope.Assets.Str((string)Def["initial"] ?? ""),
        };
        // label sits in unscaled units: position uses the scaled rect, the node scale cancels the accumulated scale
        // Scaleform puts the first baseline at the text rect top (field less its 2 px gutter) plus the line's font ascent
        // (DocView line format: BaseLineOffset = MaxFontAscent = font ascent * size / EM). Godot puts it at the label top plus
        // its line box: the line is as tall as the whole fallback chain and the primary font's line sits centred in it (whole
        // pixels of the label's raster size; measured on rendered labels). The label is moved by the difference.
        BaselineShift = 2 + scope.Assets.AscentRatio((string)Def["fontClass"]) * size - GodotBaseline(font, (int)ramp) * RasterScale / asy;
        Label.Position = new Vector2(Bounds.Position.X, Bounds.Position.Y + BaselineShift);
        // Scaleform lays every line out in the field less its 2 px gutters (DocView::SetViewRect / Format:
        // textRect.Contract(GFX_TEXT_GUTTER, GFX_TEXT_GUTTER)): a word-wrapped field wraps inside them, a left-aligned line
        // starts 2 px in and a right-aligned one ends 2 px in. A centred unwrapped line has the same centre either way, so
        // it keeps the whole field.
        if (Label.AutowrapMode != TextServer.AutowrapMode.Off || Label.HorizontalAlignment != HorizontalAlignment.Center)
        {
            Label.Position += new Vector2(2, 0);
            Label.Size = new Vector2(Math.Max(1, Bounds.Size.X - 4) * asx, Bounds.Size.Y * asy) / RasterScale;
        }
        else Label.Size = new Vector2(Bounds.Size.X * asx, Bounds.Size.Y * asy) / RasterScale;
        Label.PivotOffset = Vector2.Zero;
        AddChild(lineShift);
        lineShift.AddChild(Label);
        Label.Draw += AlignLine;
        face = new LineFace(this);
        Label.AddChild(face);
    }

    public TextNode() { }

    readonly Node2D lineShift = new();
    LineFace face;

    /// <summary>
    /// Scaleform places every centred or right-aligned line by its exact width: the sum of the glyph advances, each rounded
    /// to a twip (ParagraphFormatter, advance = IRound(advance * Scale), width = the running sum), at
    /// Max(0, IRound(width / 2 - lineWidth / 2)) or Max(0, IRound(width) - lineWidth) in the text rect (FinalizeLine, once
    /// per line: at each word wrap and at the paragraph end). A Godot label aligns by the line's size rounded up to a whole
    /// pixel (TextServer shaped_text_get_size) and truncates the offset to a whole pixel (Label::_get_line_rect:
    /// int(width - lineWidth) / 2, int(width - lineWidth)). A one-line label is shifted by the difference. A label of more
    /// lines hides its own face and LineFace draws each line's glyphs where the label puts them plus that line's
    /// difference; GlyphShadow follows the same per-line difference. A label that draws a plain shadow or an outline of its
    /// own keeps Godot's placement past one line (hiding its face would hide those too).
    /// </summary>
    void AlignLine()
    {
        float dx = 0;
        var align = Label.HorizontalAlignment;
        var ls = Label.LabelSettings;
        bool exact = align == HorizontalAlignment.Center || align == HorizontalAlignment.Right;
        var all = Layout();
        int lines = exact ? all.Count : 0;
        bool plainFace = ls != null && ls.ShadowColor.A <= 0 && ls.OutlineSize <= 0;
        bool perLine = lines > 1 && plainFace;
        // a hard drop shadow (ApplyFilters with no blur) on text with FONT-sized runs: the label could only draw the field's
        // size, so the face is drawn by line, its shadow pass first (LineFace)
        faceShadow = runStyles != null && ls != null && !plainFace && HasSizedRuns && ls.OutlineSize <= 0 && ls.ShadowSize <= 0;
        colored = runStyles != null && (plainFace || faceShadow);
        perLineDx = false;
        foreach (var l in all) l.Dx = 0;
        if (exact && lines == 1)
        {
            if (CharX(all[0], 0) is float first && ClientLineX(all[0], align) is float at)
                dx = (at - first) * Label.Scale.X;   // field units
        }
        else if (perLine)
            foreach (var l in ShownLines())
                if ((Label.VisibleCharacters < 0 || l.Start < Label.VisibleCharacters) && CharX(l) is float first && ClientLineX(l, align, true) is float at)
                {
                    l.Dx = at - first;   // label pixels
                    perLineDx = true;
                }
        if (lineShift.Position.X != dx) Callable.From(() => lineShift.Position = new Vector2(dx, 0)).CallDeferred();
        var self = new Color(1, 1, 1, perLine || colored ? 0 : 1);
        if (Label.SelfModulate != self) Callable.From(() => Label.SelfModulate = self).CallDeferred();
        face?.QueueRedraw();
    }

    /// <summary>
    /// The client's x of a line's first glyph in label pixels (FinalizeLine), null for an empty line: the line's shown
    /// characters less its trailing spaces, each run shaped at its own size, give LineWidthWithoutTrailingSpaces.
    /// </summary>
    float? ClientLineX(LayoutLine line, HorizontalAlignment align, bool visible = false)
    {
        int end = visible && Label.VisibleCharacters >= 0 ? Math.Min(line.End, Label.VisibleCharacters) : line.End;
        while (end > line.Start && CodePointAt(end - 1) is ' ' or '\t' or '\n' or '\r') end--;
        if (end <= line.Start) return null;
        double sum = 0;
        foreach (var (a, b) in Runs(line.Start, end))
        {
            float size = SizeAt(a);
            var tl = new TextLine();
            tl.AddString(Substring(a, b), Label.LabelSettings.Font, ShapeSize(size));
            double run = 0;
            foreach (Godot.Collections.Dictionary g in TextServerManager.GetPrimaryInterface().ShapedTextGetGlyphs(tl.GetRid()))
                run += (float)g["advance"] * (int)g["repeat"];
            tl.Dispose();
            sum += run * (LabelSize(size) / ShapeSize(size));
        }
        float twip = 1 / (20 * Label.Scale.X);          // a twip of the field in label pixels
        float room = Label.Size.X - (float)sum;
        return Math.Max(0, MathF.Round((align == HorizontalAlignment.Center ? room / 2 : room) / twip) * twip);
    }

    bool perLineDx;

    /// <summary>True while the label's own face is hidden and LineFace draws it (lines at their own x, or html colours and sizes).</summary>
    public bool FaceByLine => perLineDx || colored;

    bool colored, faceShadow;

    /// <summary>LineFace draws the label's hard drop shadow too (FONT-sized text under a no-blur shadow filter).</summary>
    public bool FaceDrawsShadow => faceShadow;

    /// <summary>Screen pixels per label pixel: the glyphs are rasterized at the ramp size and drawn scaled by this.</summary>
    public float RasterScale = 1;

    /// <summary>Distance from a Godot label's top to its first baseline, for text in the font's primary face.</summary>
    static float GodotBaseline(Font font, int fontSize)
    {
        var rids = font.GetRids();
        if (rids.Count == 0) return font.GetAscent(fontSize);
        var ts = TextServerManager.GetPrimaryInterface();
        float asc = (float)ts.FontGetAscent(rids[0], fontSize), desc = (float)ts.FontGetDescent(rids[0], fontSize);
        return asc + MathF.Floor((font.GetHeight(fontSize) - asc - desc) / 2);
    }

    /// <summary>Label top relative to the field top, in field units, that puts Godot's first baseline on Scaleform's.</summary>
    public float BaselineShift;

    static readonly int[] FontSizeRamp = { 0, 0, 2, 4, 6, 8, 12, 16, 18, 22, 26, 32, 36, 40, 45, 50, 56, 64, 72, 80, 90, 108, 128, 147, 168, 194, 222, 256 };

    /// <summary>
    /// Scaleform GlyphCache::SnapFontSizeToRamp: the whole screen size goes up a quarter (rounded up), then up to the next
    /// ramp step (255 cap). The cache rasterizes non-readability text at this size (GetCachedFontSize without OptRead).
    /// </summary>
    public static float RampSize(float screenSize)
    {
        int size = (int)Math.Floor(screenSize);
        size += (size + 3) >> 2;
        if (size > 255) return 255;
        foreach (int step in FontSizeRamp) if (step >= size) return Math.Max(1, step);
        return 255;
    }

    public string Text { get => Label.Text; set { runStyles = null; HasSizedRuns = false; Label.Text = value ?? ""; } }

    /// <summary>
    /// AS3 htmlText = <paramref name="html"/>: the text without its markup (FlashHtml, with the field's multiline flag), each
    /// run in its text format. Each run is laid out at its FONT size (TextNode.Layout, ParagraphFormatter's per-run layout);
    /// LineFace draws its glyphs in their colours, sizes, bold, italic and underline.
    /// </summary>
    public void SetHtml(string html)
    {
        var (text, styles) = FlashHtml.Parse(html, Def?["multiline"] is JsonNode ml ? (bool)ml : true);
        runStyles = Array.Exists(styles, s => !s.IsPlain) ? styles : null;
        HasSizedRuns = Array.Exists(styles, s => s.Size is float z && z != FieldSize);
        Label.Text = text;
        Label.QueueRedraw();
    }

    GlyphStyle[] runStyles;

    /// <summary>The text format of the code point at <paramref name="index"/> (the field's own when it has none).</summary>
    public GlyphStyle StyleAt(int index) =>
        runStyles != null && index >= 0 && index < runStyles.Length ? runStyles[index] : default;

    /// <summary>The colour LineFace draws the code point at <paramref name="index"/> in: its FONT colour, else the field's.</summary>
    public Color GlyphColor(int index) => StyleAt(index).Color ?? Label.LabelSettings.FontColor;

    /// <summary>
    /// The font's descent per pixel of font size (DefineFont layout: the line height less the ascent), the faux italic pivot and
    /// the underline offset (LineFace).
    /// </summary>
    public float DescentRatio;

    /// <summary>Width of the current text in the field's own units (AS3 textWidth).</summary>
    public float TextWidth => Label.LabelSettings.Font.GetStringSize(Label.Text, HorizontalAlignment.Left, -1, Label.LabelSettings.FontSize).X * Label.Scale.X;

    /// <summary>Lines the text wraps to at the field width (AS3 numLines).</summary>
    public int NumLines
    {
        get
        {
            if (Label.AutowrapMode == TextServer.AutowrapMode.Off) return Math.Max(1, Label.Text.Split('\n').Length);
            if (Label.IsInsideTree() || HasSizedRuns) return Math.Max(1, Layout().Count);   // the lines as drawn (the label's own wrap at the field's size)
            var ls = Label.LabelSettings;
            var p = new TextParagraph { Width = Label.Size.X, BreakFlags = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive };
            p.AddString(Label.Text, ls.Font, ls.FontSize);
            return Math.Max(1, p.GetLineCount());
        }
    }

    /// <summary>
    /// Scaleform line metrics in the field's own units (ParagraphFormatter::FinalizeLine): a line is
    /// IRound(ascent + descent) tall in twips, the next starts IRound(lineHeight + leading) below it, the leading being the
    /// field's own when its format sets one and the font's otherwise, and the text is as tall as the last line's offset
    /// plus its height (DocView::Format). Ascent, descent and leading are the font's DefineFont layout at the field's size.
    /// </summary>
    (float Height, float Advance) LineMetrics()
    {
        float size = UiBuilder.F(Def["size"], 12);
        float leading = Def["leading"] != null ? UiBuilder.F(Def["leading"], 0) : size * lineRatios.Leading;
        float height = MathF.Round(size * lineRatios.Height * 20) / 20;
        return (height, height + MathF.Round(leading * 20) / 20);
    }

    (float Height, float Leading) lineRatios = (1, 0);

    /// <summary>Sets the label's line spacing to the Scaleform line advance and returns AS3 textHeight.</summary>
    public float FlashTextHeight()
    {
        var (height, advance) = LineMetrics();
        var ls = Label.LabelSettings;
        ls.LineSpacing = advance / Label.Scale.Y - ls.Font.GetHeight(ls.FontSize);
        return height + (NumLines - 1) * advance + SizedHeightMove();
    }

    /// <summary>
    /// TextFieldEx.setVerticalAlign(VALIGN_BOTTOM): the lines that fit the text rect (the field less its 2 px gutters) end on
    /// its bottom edge, in flash line metrics; the lines past them are not shown (a 3-line caption in a 2-line field shows
    /// its first 2 lines where a 2-line caption sits).
    /// </summary>
    public void AlignBottom()
    {
        var (lineHeight, advance) = LineMetrics();
        float room = Bounds.Size.Y - 4;
        FlashTextHeight();
        int lines = NumLines, fit = Math.Max(1, 1 + (int)Math.Floor((room - lineHeight) / advance));
        int shown = Math.Min(lines, fit);
        float height = lineHeight + (shown - 1) * advance;
        Label.MaxLinesVisible = shown;
        Label.Position = new Vector2(Label.Position.X, Bounds.Position.Y + BaselineShift + Math.Max(0, room - height));
    }

    /// <summary>
    /// TextFieldEx.setVerticalAlign(VALIGN_CENTER): the lines sit centred in the text rect (the field less its 2 px gutters),
    /// in flash line metrics; text taller than the rect starts at its top.
    /// </summary>
    public void AlignCenter()
    {
        float room = Bounds.Size.Y - 4, height = FlashTextHeight();
        Label.Position = new Vector2(Label.Position.X, Bounds.Position.Y + BaselineShift + Math.Max(0, (room - height) / 2));
    }

    /// <summary>Scaleform TEXTAUTOSZ_SHRINK: lowers the font size until the text fits the field width (2 px gutters).</summary>
    public void ShrinkToFit()
    {
        var ls = Label.LabelSettings;
        float room = (Bounds.Size.X - 4) / Label.Scale.X;
        while (ls.FontSize > 1 && ls.Font.GetStringSize(Label.Text, HorizontalAlignment.Left, -1, ls.FontSize).X > room) ls.FontSize--;
    }

    /// <summary>
    /// A text field's drop shadow or glow, drawn the way Scaleform draws it: TextFilter::LoadFilterDesc takes the filter into the
    /// field's shadow (a later shadow / glow replaces one with no colour or no distance). A blurred one (BlurX &gt; 0) is the glyph
    /// cache's per-glyph shadow (GlyphCache::RasterizeShadow, see GlyphShadow), the label then draws no shadow of its own; an
    /// unblurred one is the glyph's hard copy offset by angle and distance, the label's plain shadow.
    /// </summary>
    public void ApplyFilters(JsonNode filters, float scale)
    {
        if (filters is not JsonArray fa) return;
        JsonNode shadow = null;
        foreach (var f in fa)
        {
            if ((string)f["type"] is not ("glow" or "dropShadow")) continue;
            if (shadow == null || UiBuilder.ParseColor((string)shadow["color"], Colors.Transparent).A == 0 || UiBuilder.F(shadow["distance"], 0) == 0) shadow = f;
        }
        if (shadow == null) return;
        float blur = UiBuilder.F(shadow["blurX"], 0), strength = UiBuilder.F(shadow["strength"], 1);
        float dist = UiBuilder.F(shadow["distance"], 0) * scale, angle = UiBuilder.F(shadow["angle"], 0);
        var color = UiBuilder.ParseColor((string)shadow["color"], Colors.Black);
        var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist / RasterScale;
        var ls = (LabelSettings)Label.LabelSettings.Duplicate();
        ls.ShadowSize = 0;
        Shadow?.QueueFree();
        Shadow = null;
        if (blur > 0)
        {
            ls.ShadowColor = Colors.Transparent;
            ls.ShadowOffset = Vector2.Zero;
            Shadow = new GlyphShadow(this, color, blur * scale, strength, offset);
            Label.AddChild(Shadow);
        }
        else
        {
            ls.ShadowColor = color;
            ls.ShadowOffset = offset;
        }
        Label.LabelSettings = ls;
    }

    /// <summary>The field's blurred shadow, if it has one.</summary>
    public GlyphShadow Shadow;

    /// <summary>Screen pixels per field unit (the field's accumulated scale).</summary>
    public float ScreenScale = 1;

    /// <summary>GlyphCache::SnapShadowSizeToRamp: the next ramp step at or above the whole screen size (no quarter added).</summary>
    internal static float ShadowRampSize(float screenSize)
    {
        int size = (int)Math.Floor(screenSize);
        if (size > 255) return 255;
        foreach (int step in FontSizeRamp) if (step >= size) return Math.Max(1, step);
        return 255;
    }

    /// <summary>
    /// GlyphCache::recursiveBlur on one row: the Young / van Vliet recursive gaussian, sigma = radius / 2, rounded to bytes.
    /// </summary>
    internal static float[] Blur(float[] img, float radius)
    {
        radius = Math.Max(radius, 0.62f);
        int n = img.Length, pad = (int)MathF.Ceiling(radius) + 3;
        float s = radius * 0.5f;
        float q = s < 2.5f ? 3.97156f - 4.14554f * MathF.Sqrt(1 - 0.26891f * s) : 0.98711f * s - 0.9633f, q2 = q * q, q3 = q2 * q;
        float b0 = 1 / (1.57825f + 2.44413f * q + 1.4281f * q2 + 0.422205f * q3);
        float b2 = 2.44413f * q + 2.85619f * q2 + 1.26661f * q3, b3 = -1.4281f * q2 - 1.26661f * q3, b4 = 0.422205f * q3;
        float b1 = 1 - (b2 + b3 + b4) * b0; b2 *= b0; b3 *= b0; b4 *= b0;
        var fw = new float[n + 2 * pad];
        float a1, a2 = 0, a3 = 0, a4 = 0;
        for (int x = 0; x < n + pad; x++)
        {
            a1 = b1 * (x < n ? img[x] : 0) + b2 * a2 + b3 * a3 + b4 * a4;
            fw[x + pad] = a1; a4 = a3; a3 = a2; a2 = a1;
        }
        var outp = new float[n];
        a2 = a3 = a4 = 0;
        for (int x = n + 2 * pad - 1; x >= 0; x--)
        {
            a1 = b1 * fw[x] + b2 * a2 + b3 * a3 + b4 * a4;
            a4 = a3; a3 = a2; a2 = a1;
            if (x >= pad && x < n + pad) outp[x - pad] = (int)(a1 + 0.5f);
        }
        return outp;
    }
}
