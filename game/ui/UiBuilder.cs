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

    /// <summary>
    /// When set, the shape's matrices up to the grid sprite (Scale9GridData::ShapeMtx, TreeCacheShapeLayer::calcMeshKey,
    /// sfr_render_03f.cpp:534) and the shape and the grid sprite keep their own matrices: Scale9GridInfo::Transform
    /// (sfr_render_05e.cpp:162) takes a point through ShapeMtx, the grid cell and back through the inverse of the grid
    /// sprite's matrix and ShapeMtx, so only the mesh changes. Unset, the shape sits <see cref="Offset"/> into a grid
    /// sprite drawn without its scale.
    /// </summary>
    public Transform2D? Chain;

    public NineCtx At(Vector2 offset) => new() { Grid = Grid, Bounds = Bounds, Sx = Sx, Sy = Sy, Offset = offset };
    public NineCtx With(Transform2D chain) => new() { Grid = Grid, Bounds = Bounds, Sx = Sx, Sy = Sy, Chain = chain };

    /// <summary>A point of the shape in the grid sprite's own space.</summary>
    public Vector2 ToGrid(Vector2 q) => Chain is { } c ? c * q : q + Offset;

    /// <summary>
    /// A point given by <see cref="MapX"/>/<see cref="MapY"/> back in the shape's space. Those keep the bounds' left and
    /// top edges where they are; the cell matrices put them through the grid sprite's scale (Bounds.x1 * Sx), so with
    /// <see cref="Chain"/> the difference is added before the scale is taken off.
    /// </summary>
    public Vector2 FromGrid(Vector2 p) => Chain is { } c
        ? c.AffineInverse() * new Vector2((p.X + Bounds.Position.X * (Sx - 1)) / Sx, (p.Y + Bounds.Position.Y * (Sy - 1)) / Sy)
        : p - Offset;

    public Vector2 MapPoint(Vector2 q)
    {
        var p = ToGrid(q);
        return FromGrid(new Vector2(MapX(p.X), MapY(p.Y)));
    }

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

    /// <summary>The scale-9 context the shapes' pieces are mapped through at draw time; the gauge's in-place resize
    /// mutates its Sx, as the client's SetWidthInt re-draws the clip at a new width.</summary>
    public NineCtx Nine => nine;

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
            var poly = nine == null ? pts : pts.Select(nine.MapPoint).ToArray();
            if (Geometry2D.TriangulatePolygon(poly).Length > 0) DrawColoredPolygon(poly, color);
        }
        if (nine == null || pieces.Count == 0) return;
        var g = nine.Grid;
        float[] xs = { float.NegativeInfinity, g.Position.X, g.End.X, float.PositiveInfinity };
        float[] ys = { float.NegativeInfinity, g.Position.Y, g.End.Y, float.PositiveInfinity };
        foreach (var (tex, dest0, src) in pieces)
        {
            // grid sprite space (an axis-aligned chain keeps the piece a rectangle)
            Vector2 c0 = nine.ToGrid(dest0.Position), c1 = nine.ToGrid(dest0.End);
            var dest = new Rect2(Math.Min(c0.X, c1.X), Math.Min(c0.Y, c1.Y), Math.Abs(c1.X - c0.X), Math.Abs(c1.Y - c0.Y));
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    float x0 = Math.Max(dest.Position.X, xs[i]), x1 = Math.Min(dest.End.X, xs[i + 1]);
                    float y0 = Math.Max(dest.Position.Y, ys[j]), y1 = Math.Min(dest.End.Y, ys[j + 1]);
                    if (x1 <= x0 || y1 <= y0) continue;
                    float fx = src.Size.X / dest.Size.X, fy = src.Size.Y / dest.Size.Y;
                    var s = new Rect2(src.Position.X + (x0 - dest.Position.X) * fx, src.Position.Y + (y0 - dest.Position.Y) * fy, (x1 - x0) * fx, (y1 - y0) * fy);
                    Vector2 d0 = nine.FromGrid(new Vector2(nine.MapX(x0), nine.MapY(y0))), d1 = nine.FromGrid(new Vector2(nine.MapX(x1), nine.MapY(y1)));
                    var d = new Rect2(d0, d1 - d0);
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
        ExportedImage = src => scope.Assets.ExportedImage(scope.MovieKey, scope.RootKey, src);
        Readable = (bool?)Def["readable"] == true;
        Bounds = UiBuilder.Rc(sym["bounds"]);
        if (nine != null) Bounds = nine.Map(Bounds);
        lineRatios = scope.Assets.LineRatios((string)Def["fontClass"]);
        AscentRatio = scope.Assets.AscentRatio((string)Def["fontClass"]);
        DescentRatio = lineRatios.Height - AscentRatio;
        float size = UiBuilder.F(Def["size"], 12);
        float asx = (float)sx, asy = (float)sy;
        // Scaleform's glyph cache rasterizes plain text (no readability AA) once at a ramp size and draws that raster scaled
        // to the screen size with linear filtering, unsnapped; the label does the same: font size = ramp, scale = screen / ramp
        // Readability text draws from a raster of its quarter-pixel screen size (CachedSize); the label keeps a whole font
        // size and the font's oversampling makes up the quarter.
        float screen = size * asy, cached = CachedSize(screen, Readable), ramp = Readable ? Math.Max(1, MathF.Round(cached)) : cached;
        RasterScale = screen / ramp;
        var font = scope.Assets.FontFor((string)Def["fontClass"], Readable, Readable ? cached / ramp : 0);
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
        ApplyLineAdvance();
        // a timeline field's static text is never clipped; an auto-sized field grows to its text (TextClip)
        Clips = (string)sym["kind"] != "staticText" && (bool?)Def["autoSize"] != true;
        ReadOnly = (bool?)Def["readOnly"] != false;
        AddChild(clip);
        clip.Inner.AddChild(snap);
        snap.AddChild(lineShift);
        lineShift.AddChild(Label);
        if (Readable) SetNotifyTransform(true);
        Label.Draw += AlignLine;
        face = new LineFace(this);
        Label.AddChild(face);
    }

    public TextNode() { }

    readonly Node2D lineShift = new();
    readonly Node2D snap = new();
    readonly TextClip clip = new();

    /// <summary>A dynamic field (DefineEditText, not static text) that is not auto-sized: its glyphs are cut at its text rect
    /// when the text does not fit (<see cref="TextClip"/>).</summary>
    public bool Clips;

    /// <summary>The field is not an input field (DocView::IsReadOnly): only then does text that fits go unclipped.</summary>
    public bool ReadOnly = true;

    /// <summary>
    /// The node's own transform is not the field's: a script field's node (FieldText) is moved and scaled inside the field
    /// for the vertical alignment and the text auto size, so its text rect is the field's taken back through that, and the
    /// text's size in the field is its size here times the scale.
    /// </summary>
    public bool PlacedInField;

    /// <summary>
    /// The first shown line (DocView::SetVScrollOffset, TextField.scrollV - 1): the label draws from its first unskipped line,
    /// at most the lines that fit the field (FitLines), and the lines above it are neither drawn nor laid out again.
    /// </summary>
    public int FirstLine
    {
        get => Label.LinesSkipped;
        set
        {
            int v = Math.Clamp(value, 0, Math.Max(0, Layout().Count - 1));
            if (v == Label.LinesSkipped) return;
            Label.LinesSkipped = v;
            AlignLine();
        }
    }

    /// <summary><see cref="FirstLine"/> for a script field scrolled to the line that starts at a character of a text of
    /// <paramref name="total"/> code points (<see cref="FirstLineFor"/>).</summary>
    public void ShowFrom(int start, int total, bool atEnd) => FirstLine = FirstLineFor(start, total, atEnd);

    /// <summary>The clip again for the node's place: a script field's node is moved without its text changing.</summary>
    public void RefreshClip() => UpdateClip(Layout());

    /// <summary>
    /// The clip of <see cref="TextClip"/> for the lines as laid out: Scaleform's text width (the widest line) and text height
    /// (<see cref="FlashTextHeight"/>'s, without setting the line advance) in field units, against the field's box.
    /// </summary>
    void UpdateClip(List<LayoutLine> all)
    {
        bool on = false;
        var rect = TextClip.TextRect(Bounds);
        if (Clips)
        {
            float scale = PlacedInField ? Math.Abs(Scale.X) : 1, width = 0;
            foreach (var l in all) width = Math.Max(width, l.Width.Width * Label.Scale.X);
            var (height, advance) = LineMetrics();
            float textHeight = height + (Math.Max(1, all.Count) - 1) * advance + SizedHeightMove();
            on = !TextClip.Fits(ReadOnly, width * scale, textHeight * scale, Bounds.Size);
            if (PlacedInField) rect = Transform.AffineInverse() * rect;
        }
        if (on != clip.ClipContents || (on && (clip.Position != rect.Position || clip.Size != rect.Size)))
            Callable.From(() => clip.Set(on, rect)).CallDeferred();
    }

    /// <summary>
    /// Readability text (GlyphParam::OptRead: a CSMTextSettings tag, a device font or a script-made field). Its glyphs
    /// are drawn from a raster at the exact screen size (<see cref="CachedSize"/>), fitted to the pixel grid vertically
    /// (GlyphCache::RasterizeGlyph addShapeAutoFit, sfr_render_06d.cpp:949: the readable font is hinted, see
    /// UiAssets.FontFor), and the field's screen position is rounded to whole pixels (<see cref="SnapToPixel"/>).
    /// </summary>
    public bool Readable;

    /// <summary>
    /// GlyphCache::GetCachedFontSize (sfr_render_06d.cpp:1937): readability text is rasterized at its screen size in
    /// quarter pixels up to the cache's slot height, other text at <see cref="RampSize"/>.
    /// </summary>
    public static float CachedSize(float screenSize, bool readable)
        => readable ? Math.Clamp(MathF.Floor(screenSize * 4 + 0.5f) * 0.25f, 0.25f, MaxCachedSize) : RampSize(screenSize);

    /// <summary>GlyphCache maxHeight: MaxSlotHeight 48 less 2 * SlotPadding 2 (the client keeps the defaults, gfxrendersinglethread.cpp:95).</summary>
    const float MaxCachedSize = 44;

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged || what == NotificationEnterTree) SnapToPixel();
    }

    /// <summary>
    /// TreeCacheText::UpdateTransform (sfr_render_07n.cpp:192): for readability text without 3D or free rotation, the
    /// field's final matrix has its translation rounded half away from zero to whole pixels. A timeline field's matrix is
    /// this node's (MovieClipNode places it); a script field's is its FieldText's. The text is moved by the difference.
    /// </summary>
    void SnapToPixel()
    {
        if (!Readable || !IsInsideTree()) return;
        var field = (GetParent() as FieldText ?? (Node2D)this).GetGlobalTransformWithCanvas();
        if (field.X.Y != 0 || field.Y.X != 0) { snap.Position = Vector2.Zero; return; }
        static float Round(float v) => MathF.Floor(v + (v < 0 ? -0.5f : 0.5f));
        var d = new Vector2(Round(field.Origin.X), Round(field.Origin.Y)) - field.Origin;
        var to = GetGlobalTransformWithCanvas().BasisXformInv(d);
        if (snap.Position != to) snap.Position = to;
    }
    LineFace face;

    /// <summary>The lines <see cref="AlignBottom"/> shows, -1 when it was not called.</summary>
    int bottomCap = -1;

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
        UpdateClip(all);
        // the label draws the lines that fit the field (FitLines), the bottom alignment's count at most
        int fit = FitLines(all), cap = fit < all.Count - Label.LinesSkipped ? fit : -1;
        if (bottomCap >= 0) cap = cap < 0 ? bottomCap : Math.Min(cap, bottomCap);
        if (Label.MaxLinesVisible != cap) Callable.From(() => Label.MaxLinesVisible = cap).CallDeferred();
        int lines = exact ? all.Count : 0;
        bool plainFace = ls != null && ls.ShadowColor.A <= 0 && ls.OutlineSize <= 0;
        bool perLine = lines > 1 && plainFace;
        // a hard drop shadow (ApplyFilters with no blur) on text with FONT-sized runs: the label could only draw the field's
        // size, so the face is drawn by line, its shadow pass first (LineFace)
        faceShadow = runStyles != null && ls != null && !plainFace && (HasSizedRuns || HasImages) && ls.OutlineSize <= 0 && ls.ShadowSize <= 0;
        colored = (runStyles != null || HasSelection) && (plainFace || faceShadow);
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
        // the highlight's rects follow the relaid lines (a text change under a kept range, a resize)
        if (HasSelection) selection?.QueueRedraw();
    }

    /// <summary>
    /// The client's x of a line's first glyph in label pixels (FinalizeLine), null for an empty line: the line's shown
    /// characters less its trailing spaces, each run shaped at its own size, give LineWidthWithoutTrailingSpaces.
    /// </summary>
    float? ClientLineX(LayoutLine line, HorizontalAlignment align, bool visible = false)
    {
        int end = visible && Label.VisibleCharacters >= 0 ? Math.Min(line.End, Label.VisibleCharacters) : line.End;
        // an IMG char is a space code point but not a space (ParagraphFormatter: isSpace false, NumOfTrailingSpaces = 0,
        // sfr_render_03b.cpp:2128 / :2155): the trailing spaces end at it
        while (end > line.Start && ImageAt(end - 1) == null && CodePointAt(end - 1) is ' ' or '\t' or '\n' or '\r') end--;
        if (end <= line.Start) return null;
        double sum = 0;
        foreach (var (a, b) in Runs(line.Start, end))
        {
            // an IMG char: its advance, or its width when it is the line's last (LineWidthWithoutTrailingSpaces = NewLineWidth + AdjLineWidth)
            if (ImageAt(a) is ImageBox img) { sum += (b == end ? img.W : img.Advance) / Label.Scale.X; continue; }
            float size = SizeAt(a);
            var tl = new TextLine();
            tl.AddString(Substring(a, b), Label.LabelSettings.Font, ShapeSize(size));
            double run = 0;
            var glyphs = TextServerManager.GetPrimaryInterface().ShapedTextGetGlyphs(tl.GetRid());
            using (var glyphsOwner = (Godot.Collections.Array)glyphs)
                foreach (Godot.Collections.Dictionary g in glyphs)
                {
                    using var _ = g;
                    run += (float)g[GlyphKeys.Advance] * (int)g[GlyphKeys.Repeat];
                }
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

    /// <summary>
    /// True while a selection shows on a label that draws its own outline or soft shadow, so its face stays the label's:
    /// LineFace then draws only the selected glyphs, in the selection's text colour, over the label's face. The client draws
    /// the shadow and outline glyphs in their own colour (TextLayer_Shadow, sfr_render_07o.cpp:1005-1019) and the text in the
    /// highlighter's colour over them, so a selected outlined glyph is white inside its outline.
    /// </summary>
    public bool SelectionOverFace => HasSelection && !FaceByLine;

    bool colored, faceShadow;

    /// <summary>LineFace draws the label's hard drop shadow too (FONT-sized text under a no-blur shadow filter).</summary>
    public bool FaceDrawsShadow => faceShadow;

    /// <summary>Screen pixels per label pixel: the glyphs are rasterized at the ramp size and drawn scaled by this.</summary>
    public float RasterScale = 1;

    /// <summary>Distance from a Godot label's top to its first baseline, for text in the font's primary face.</summary>
    static float GodotBaseline(Font font, int fontSize)
    {
        var rids = font.GetRids();
        using var ridsOwner = (Godot.Collections.Array)rids;
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

    public string Text { get => Label.Text; set { runStyles = null; HasSizedRuns = false; images = null; Label.Text = value ?? ""; } }

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
        images = null;
        foreach (var st in styles)
            if (st.Image is HtmlImage im && (images ??= new()).ContainsKey(im) == false) images[im] = Box(im);
        Label.Text = text;
        Label.QueueRedraw();
    }

    GlyphStyle[] runStyles;

    /// <summary>
    /// An IMG char as the client lays it out and draws it, in field units: its texture and region (none when its url
    /// names no image), its size W x H, its ascent above the baseline (its height less its descent) and its advance.
    /// TextField::ProcessImageTags (sfr_gfx_03e.cpp:1441): the url is an export name (UiAssets.ExportedImage); the size is
    /// the tag's WIDTH / HEIGHT, else the image's; BaseLineY = the image's height + VSPACE, and the desc's matrix moves the
    /// image up by it and scales it to the size, so the ascent is BaseLineY scaled by H over the image's height. An image
    /// that is not found keeps the tag's size and an identity matrix (ascent 0). ParagraphFormatter (sfr_render_03b.cpp:2124):
    /// the glyph is IRound(ScreenWidth) wide and advances 40 twips (2 px) more.
    /// </summary>
    public readonly record struct ImageBox(Texture2D Tex, Rect2 Region, float W, float H, float Ascent)
    {
        public float Advance => W + 2;
    }

    Dictionary<HtmlImage, ImageBox> images;

    /// <summary>The IMG url's image: (texture or null, region) when an exported name, null when none.</summary>
    public Func<string, (Texture2D Tex, Rect2 Region)?> ExportedImage;

    ImageBox Box(HtmlImage im)
    {
        if (ExportedImage?.Invoke(im.Src) is { } found && found.Region.Size.X > 0 && found.Region.Size.Y > 0)
        {
            float ow = found.Region.Size.X, oh = found.Region.Size.Y;
            float w = im.Width > 0 ? im.Width : ow, h = im.Height > 0 ? im.Height : oh;
            return new ImageBox(found.Tex, found.Region, MathF.Floor(w * 20 + 0.5f) / 20, h, (oh + im.VSpace) * (h / oh));
        }
        return new ImageBox(null, default, im.Width, im.Height, 0);
    }

    /// <summary>True when the text has IMG chars.</summary>
    public bool HasImages => images != null;

    /// <summary>The IMG char at <paramref name="index"/>, null for text.</summary>
    public ImageBox? ImageAt(int index) => images != null && StyleAt(index).Image is HtmlImage im ? images[im] : null;

    /// <summary>The text format of the code point at <paramref name="index"/> (the field's own when it has none).</summary>
    public GlyphStyle StyleAt(int index) =>
        runStyles != null && index >= 0 && index < runStyles.Length ? runStyles[index] : default;

    /// <summary>The colour LineFace draws the code point at <paramref name="index"/> in: the selection's text colour when it is
    /// selected, else its FONT colour, else the field's.</summary>
    public Color GlyphColor(int index) => Selected(index) ? SelectionText : StyleAt(index).Color ?? Label.LabelSettings.FontColor;

    SelectionHighlight selection;   // made by the first selection shown: most fields never show one
    int selBegin, selEnd;

    /// <summary>The selection's highlight colour behind its glyphs and its glyphs' colour.</summary>
    public Color SelectionFill { get; private set; }
    public Color SelectionText { get; private set; }

    /// <summary>True while the field shows a selection: its label's face is drawn by line (LineFace) to colour the selected glyphs.</summary>
    public bool HasSelection => selEnd > selBegin;

    /// <summary>
    /// The selection the field shows (EditorKit::SetSelection @0x14261a4f0 into DocView::SetSelection): the text from
    /// <paramref name="begin"/> to <paramref name="end"/> in UTF-16 units (the runtime's indices, either order), highlighted in
    /// <paramref name="back"/> with its glyphs in <paramref name="text"/>; an empty range shows none. The highlight is drawn
    /// behind the label (<see cref="SelectionHighlight"/>), the glyphs by LineFace.
    /// </summary>
    public void SetSelection(int begin, int end, Color back, Color text)
    {
        if (end < begin) (begin, end) = (end, begin);
        if (begin == end) begin = end = 0;
        if (begin == selBegin && end == selEnd && (begin == end || (back == SelectionFill && text == SelectionText))) return;
        selBegin = begin; selEnd = end;
        SelectionFill = back; SelectionText = text;
        Label.QueueRedraw();     // AlignLine: the face by line while a selection shows
        if (selection == null && HasSelection) Label.AddChild(selection = new SelectionHighlight(this));
        selection?.QueueRedraw();
    }

    /// <summary>True when the code point at <paramref name="index"/> is in the selection.</summary>
    public bool Selected(int index)
    {
        if (selEnd <= selBegin) return false;
        int at = index < utf16.Count ? utf16[index] : Label.Text.Length;
        return at >= selBegin && at < selEnd;
    }

    /// <summary>True when the UTF-16 index <paramref name="at"/> is in the selection.</summary>
    public bool SelectedUtf16(int at) => at >= selBegin && at < selEnd;

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
            if (Label.IsInsideTree() || HasSizedRuns || HasImages) return Math.Max(1, Layout().Count);   // the lines as drawn (the label's own wrap at the field's size)
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

    /// <summary>
    /// The label's line pitch is Scaleform's line advance: ParagraphFormatter::FinalizeLine (sfr_render_03b.cpp:1743) starts
    /// the next line IRound(lineHeight + leading) below, lineHeight = IRound(ascent + descent). Godot's own pitch is the
    /// font chain's line box, taller than that.
    /// </summary>
    void ApplyLineAdvance()
    {
        var (_, advance) = LineMetrics();
        var ls = Label.LabelSettings;
        ls.LineSpacing = advance / Label.Scale.Y - ls.Font.GetHeight(ls.FontSize);
    }

    /// <summary>Sets the label's line spacing to the Scaleform line advance and returns AS3 textHeight.</summary>
    public float FlashTextHeight()
    {
        ApplyLineAdvance();
        var (height, advance) = LineMetrics();
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
        Label.MaxLinesVisible = bottomCap = shown;
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

    /// <summary>
    /// The text auto size scale (<paramref name="tas"/> = TextFieldEx's "shrink" or "fit") of the text as laid out: its widest
    /// line and Scaleform text height in field units, in the field less its 2 px gutters (AsTextField.TextAutoSizeScale).
    /// </summary>
    public float TextAutoSizeScale(string tas)
    {
        float textH = FlashTextHeight(), textW = 0;
        foreach (var line in Layout()) textW = Math.Max(textW, line.Width.Width * Label.Scale.X);
        if (Label.AutowrapMode == TextServer.AutowrapMode.Off) textW = Math.Max(textW, TextWidth);
        return (float)Ms2.Avm2.Flash.AsTextField.TextAutoSizeScale(tas, textW, textH, Bounds.Size.X - 4, Bounds.Size.Y - 4, LineMetrics().Height);
    }

    /// <summary>
    /// After a text auto size scale, centred and right-aligned lines are placed again in the whole view (DocView::Format,
    /// NeedRecenterLines, sfr_render_03b.cpp:1720-1731, :2558): the label of an unwrapped field is widened by 1 / scale so it
    /// spans the field once scaled. A word-wrapped label keeps its width (a wider one would break its lines elsewhere).
    /// </summary>
    public void RecenterScaled(float scale)
    {
        if (scaledFrom is { } w0) Label.Size = new Vector2(w0, Label.Size.Y);
        scaledFrom = null;
        if (scale == 1 || Label.AutowrapMode != TextServer.AutowrapMode.Off || Label.HorizontalAlignment == HorizontalAlignment.Left) return;
        scaledFrom = Label.Size.X;
        // a centred unwrapped label spans the whole field (no gutters) and the scale pivots on the gutter: its width puts
        // the centre back at the view's; a right-aligned one starts on the gutter
        bool centre = Label.HorizontalAlignment == HorizontalAlignment.Center;
        float field = centre ? Bounds.Size.X : Math.Max(1, Bounds.Size.X - 4), w = (Bounds.Size.X - 4) / scale + (centre ? 4 : 0);
        Label.Size = new Vector2(Label.Size.X * w / field, Label.Size.Y);       // label units per field unit as built
    }
    float? scaledFrom;

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
        var ls = Label.LabelSettings;       // the label's own (made with it, never shared): set in place
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
