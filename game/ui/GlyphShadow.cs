using Godot;
using Godot.Collections;

namespace Ms2Godot.Ui;

/// <summary>
/// A text field's blurred drop shadow or glow, made the way Scaleform's glyph cache makes it (GlyphCache::RasterizeShadow): every
/// glyph is rasterized at the shadow ramp size (SnapShadowSizeToRamp) with gamma 0.4, padded by SlotPadding (2) plus the whole
/// blur, blurred by the recursive gaussian on rows then columns with BlurY = BlurX, strengthened around bias 8
/// (strengthenImage: int((v - 8) * strength + 0.5) + 8, clamped; no bias at strength 1 or less) and drawn scaled to the screen
/// size, tinted with the shadow colour, under the face and offset by angle and distance.
/// It is a child of the field's label drawn behind it, and the glyphs sit where TextNode.Layout puts them (the label's own
/// layout for text with no FONT size): each glyph of the label's shaped line at the label's own pen for it, repeat times, the baseline is
/// the line's (line top plus the line ascent, floored as the face is), and each glyph has its run's size. A line the face
/// draws at its own exact x (LayoutLine.Dx, LineFace) moves its shadow with it.
/// The glyph rasters are cached by (font, shadow ramp size, glyph, blur, strength): the colour is a draw-time tint, not a key.
/// </summary>
public partial class GlyphShadow : Node2D
{
    readonly TextNode text;
    readonly Color color;
    readonly float blur, strength;          // blur: screen pixels (the filter's BlurX at the field's scale)
    readonly Vector2 offset;                // label pixels
    readonly List<Texture2D> drawn = new(); // the textures the last draw uses stay alive while the cache can drop them

    public GlyphShadow(TextNode text, Color color, float blur, float strength, Vector2 offset)
    {
        this.text = text; this.color = color; this.blur = blur; this.strength = strength; this.offset = offset;
        Name = NodeName;
        ShowBehindParent = true;
    }

    public GlyphShadow() { }

    static readonly StringName NodeName = "GlyphShadow";

    public override void _EnterTree() => text.Label.Draw += QueueRedraw;     // the label redraws: its text, settings or layout changed

    public override void _ExitTree() => text.Label.Draw -= QueueRedraw;

    public override void _Draw()
    {
        drawn.Clear();
        var label = text.Label;
        var ls = label.LabelSettings;
        if (ls?.Font == null || string.IsNullOrEmpty(label.Text) || color.A <= 0) return;
        var font = ls.Font;
        var lines = text.ShownLines();
        bool placed = false;
        try
        {
            foreach (var line in lines)
            {
                foreach (var g in line.Shaped)
                {
                    if (text.Hidden(g) || g.Glyph == 0) continue;
                    var (frid, shift, ramp, rb) = Raster(font, g);
                    float k = g.Size / ramp;
                    var key = Key(frid, ramp, g.Glyph, rb, strength);
                    if (!cache.TryGetValue(key, out var e))
                    {
                        // the draw's first glyph not cached yet: every glyph it draws is put in its font atlas first, so each
                        // atlas image is read once for the whole draw (Rasterize) and holds every one of them
                        if (!placed) Place(font, lines);
                        placed = true;
                        e = Get(key);
                    }
                    if (e.Texture == null) continue;
                    float pen = line.LabelX + g.Pen + line.Dx;
                    for (int r = 0; r < g.Repeat; r++)
                    {
                        var at = new Vector2(pen + r * g.Advance + g.Offset.X, MathF.Floor(line.Baseline + g.Offset.Y)) + shift + e.Origin * k + offset;
                        DrawTextureRect(e.Texture, new Rect2(at, e.Size * k), false, color);
                    }
                    drawn.Add(e.Texture);
                }
            }
        }
        finally
        {
            foreach (var a in atlases.Values) a.Dispose();
            atlases.Clear();
        }
    }

    /// <summary>Puts the glyphs of <paramref name="lines"/> that have no cached raster in their font atlas.</summary>
    void Place(Font font, System.Collections.Generic.List<TextNode.LayoutLine> lines)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        foreach (var line in lines)
            foreach (var g in line.Shaped)
            {
                if (text.Hidden(g) || g.Glyph == 0) continue;
                var (frid, _, ramp, rb) = Raster(font, g);
                if (!cache.ContainsKey(Key(frid, ramp, g.Glyph, rb, strength))) ts.FontGetGlyphTextureIdx(frid, new Vector2I(ramp, 0), g.Glyph);
            }
    }

    /// <summary>
    /// A glyph's raster font and its faux style shift, its shadow ramp (from the run size on the screen) and the raster blur
    /// in shadow pixels.
    /// </summary>
    (Rid Font, Vector2 Shift, int Ramp, float Blur) Raster(Font font, in TextNode.PlacedGlyph g)
    {
        float screen = g.Size * text.RasterScale;
        int ramp = (int)TextNode.ShadowRampSize(screen);
        var style = text.StyleAt(g.Index);
        var (frid, shift) = FauxStyle.Font(font, g.Font, style.Bold, style.Italic, g.Size, text.DescentRatio * g.Size);
        return (frid, shift, ramp, blur * ramp / screen);
    }

    /// <summary>
    /// The font atlas images read in the current draw, by (font, size, texture index): the atlas is copied once per draw
    /// instead of once per glyph. A draw puts its new glyphs in the atlas before it reads any (_Draw), and a glyph once there
    /// keeps its place, so the image read holds every glyph the draw rasterizes.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<(ulong, int, long), Image> atlases = new();

    readonly struct Entry
    {
        public readonly Texture2D Texture;
        public readonly Vector2 Origin, Size;   // shadow pixels, from the pen position on the baseline
        public Entry(Texture2D texture, Vector2 origin, Vector2 size) { Texture = texture; Origin = origin; Size = size; }
    }

    /// <summary>
    /// One raster per (font, shadow ramp size, glyph, blur, strength). That set is bounded by the glyphs the HUD's fonts can
    /// draw times the shadow parameters the movies use, not by the strings: dynamic text (counts, timers, chat) reuses the
    /// glyphs. Free text could still reach many distinct glyphs (a font's whole Hangul set), so the cache is dropped whole
    /// past CacheLimit entries; a node keeps the textures its last draw used until it draws again.
    /// It lives for the session as the client's does: the shadow rasters sit in the renderer's GlyphCache
    /// (Renderer2D::pGlyphCache, render_renderer2d.h:171), which outlives the movies and is only emptied by ClearCache or
    /// SetParams (render_glyphcache.h:207-208). So a dialog's first close keeps
    /// its new glyphs' rasters, about two engine objects each: myinfo's first open adds 67 (of its 143 kept objects), the world
    /// map's names 765 (of 1,978).
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<(Rid, int, long, float, float), Entry> cache = new();
    static Image scratch;
    const int CacheLimit = 4096;
    const int SlotPadding = 2;              // GlyphCacheParams::SlotPadding

    static (Rid, int, long, float, float) Key(Rid font, int ramp, long glyph, float rb, float strength) =>
        (font, ramp, glyph, MathF.Round(rb, 3), MathF.Round(strength, 3));

    static Entry Get((Rid, int, long, float, float) key)
    {
        if (cache.TryGetValue(key, out var e)) return e;
        if (cache.Count >= CacheLimit) cache.Clear();
        return cache[key] = Rasterize(key.Item1, key.Item2, key.Item3, key.Item4, key.Item5);
    }

    static Entry Rasterize(Rid font, int ramp, long glyph, float rb, float strength)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        var size = new Vector2I(ramp, 0);
        long ti = ts.FontGetGlyphTextureIdx(font, size, glyph);
        if (ti < 0) return default;
        var uv = ts.FontGetGlyphUVRect(font, size, glyph);
        var gofs = ts.FontGetGlyphOffset(font, size, glyph);
        if (!atlases.TryGetValue((font.Id, ramp, ti), out var atlas))
        {
            atlas = ts.FontGetTextureImage(font, size, ti);
            if (atlas == null) return default;
            atlases[(font.Id, ramp, ti)] = atlas;
        }
        int x0 = (int)uv.Position.X, y0 = (int)uv.Position.Y, w = (int)uv.Size.X, h = (int)uv.Size.Y;
        if (w <= 0 || h <= 0) return default;

        // the glyph's coverage, cut to its inked box (the rasterizer's cell bounds)
        var cov = new byte[w * h];
        int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte a = (byte)Math.Clamp((int)MathF.Round(atlas.GetPixel(x0 + x, y0 + y).A * 255), 0, 255);
                cov[y * w + x] = a;
                if (a == 0) continue;
                bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y); by1 = Math.Max(by1, y);
            }
        if (bx1 < 0) return default;

        int pad = SlotPadding + (int)MathF.Ceiling(rb);
        int W = bx1 - bx0 + 1 + 2 * pad, H = by1 - by0 + 1 + 2 * pad;
        var buf = new float[W * H];
        for (int y = by0; y <= by1; y++)
            for (int x = bx0; x <= bx1; x++)
            {
                int a = cov[y * w + x];
                if (a > 0) buf[(y - by0 + pad) * W + (x - bx0 + pad)] = (int)(MathF.Pow(a / 255f, 0.4f) * 255 + 0.5f);   // Ras.SetGamma2(0.4)
            }

        // recursiveBlur: rows, then columns, BlurY = BlurX
        var line = new float[W];
        for (int y = 0; y < H; y++)
        {
            System.Array.Copy(buf, y * W, line, 0, W);
            System.Array.Copy(TextNode.Blur(line, rb), 0, buf, y * W, W);
        }
        var col = new float[H];
        for (int x = 0; x < W; x++)
        {
            for (int y = 0; y < H; y++) col[y] = buf[y * W + x];
            var o = TextNode.Blur(col, rb);
            for (int y = 0; y < H; y++) buf[y * W + x] = o[y];
        }

        // strengthenImage
        int bias = strength <= 1 ? 0 : 8;
        var data = new byte[W * H * 2];
        for (int p = 0; p < W * H; p++)
        {
            int v = (int)buf[p];
            if (strength != 1) v = (int)((v - bias) * strength + 0.5f) + bias;
            data[2 * p] = 255;
            data[2 * p + 1] = (byte)Math.Clamp(v, 0, 255);
        }
        // one scratch image for every raster: the texture copies its data
        scratch ??= new Image();
        scratch.SetData(W, H, false, Image.Format.La8, data);
        var tex = ImageTexture.CreateFromImage(scratch);
        return new Entry(tex, gofs + new Vector2(bx0 - pad, by0 - pad), new Vector2(W, H));
    }
}
