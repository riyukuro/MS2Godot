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
    readonly List<Texture2D> drawn = new(); // the pages the last draw uses stay alive while the cache can drop them

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
                        DrawTextureRectRegion(e.Texture, new Rect2(at, e.Size * k), e.Region, color);
                    }
                    if (drawn.Count == 0 || drawn[^1] != e.Texture) drawn.Add(e.Texture);
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
    /// The font atlas images read in the current draw, by (font, size, texture index). FontGetTextureImage hands back the
    /// font's own atlas Image (TextServerAdvanced returns its reference, no copy), so this saves one call and one C#
    /// wrapper per glyph, not a copy of the atlas. A draw puts its new glyphs in the atlas before it reads any (_Draw), and
    /// a glyph once there keeps its place, so the image read holds every glyph the draw rasterizes.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<(ulong, int, long), Image> atlases = new();

    readonly struct Entry
    {
        public readonly Texture2D Texture;      // the page the raster sits in
        public readonly Rect2 Region;           // its slot in the page
        public readonly Vector2 Origin, Size;   // shadow pixels, from the pen position on the baseline
        public Entry(Texture2D texture, Rect2 region, Vector2 origin, Vector2 size) { Texture = texture; Region = region; Origin = origin; Size = size; }
    }

    /// <summary>
    /// The shadow rasters share texture pages the way the client's glyph cache keeps its glyphs: GlyphCacheParams makes
    /// 1024 x 1024 textures with the slots packed in them (render_glyphcacheconfig.h, the client's copy at 0x1422f5320).
    /// A field's shadow glyphs then draw from one texture, so the renderer batches them in one draw call; one texture per
    /// glyph cost a draw call per glyph (a gear tooltip and its compare tooltip: 360 glyphs, 410 more draw calls a frame).
    /// Slots are packed on shelves of one height class. A page that got new rasters is uploaded once a frame, after the
    /// frame's draws and before it renders (Flush), however many fields drew into it.
    /// The pages are 512 x 512, not the client's 1024: the client updates a 256 x 512 part of a texture
    /// (TexUpdWidth/Height), but an ImageTexture updates whole, so a smaller page keeps that upload at 0.5 MB.
    /// </summary>
    sealed class Page
    {
        public const int Width = 512, Height = 512;
        public readonly ImageTexture Texture;
        readonly byte[] pixels = new byte[Width * Height * 2];   // La8
        sealed class Shelf { public int Y, H, X; }   // a class element: the list runs the shared generic code
        readonly List<Shelf> shelves = new();
        int top;
        bool dirty;

        public Page()
        {
            using var image = Image.CreateEmpty(Width, Height, false, Image.Format.La8);
            Texture = ImageTexture.CreateFromImage(image);
        }

        /// <summary>A free slot of <paramref name="w"/> x <paramref name="h"/>, or false when the page has none.</summary>
        public bool Place(int w, int h, out int x, out int y)
        {
            int hc = (h + 7) & ~7;   // shelf height class
            for (int i = 0; i < shelves.Count; i++)
            {
                var s = shelves[i];
                if (s.H != hc || s.X + w > Width) continue;
                x = s.X; y = s.Y;
                s.X += w;
                return true;
            }
            x = y = 0;
            if (top + hc > Height || w > Width) return false;
            shelves.Add(new Shelf { Y = top, H = hc, X = w });
            y = top; top += hc;
            return true;
        }

        public void Write(int x, int y, int w, int h, byte[] data)
        {
            for (int r = 0; r < h; r++) System.Array.Copy(data, r * w * 2, pixels, ((y + r) * Width + x) * 2, w * 2);
            if (dirty) return;
            dirty = true;
            dirtyPages.Add(this);
            if (dirtyPages.Count == 1) FlushCall.CallDeferred();
        }

        /// <summary>
        /// Uploads the page. A new Image each time: under GL Compatibility with a separate render thread the update keeps a
        /// reference to it, so it must not change after the call.
        /// </summary>
        public void Upload()
        {
            dirty = false;
            using var image = Image.CreateFromData(Width, Height, false, Image.Format.La8, pixels);
            Texture.Update(image);
        }
    }

    /// <summary>
    /// The pages written since the last flush. A page dropped by a reset stays here until it is uploaded, so the nodes that
    /// drew from it this frame still show their glyphs.
    /// </summary>
    static readonly List<Page> dirtyPages = new();
    static readonly Callable FlushCall = Callable.From(Flush);

    /// <summary>
    /// Uploads every page written this frame, once. It is a deferred call queued by the frame's first write, so it runs after
    /// the queued redraws that wrote it and before the frame renders.
    /// </summary>
    static void Flush()
    {
        for (int i = 0; i < dirtyPages.Count; i++) dirtyPages[i].Upload();
        dirtyPages.Clear();
    }

    static readonly List<Page> pages = new();
    static Page lastPage;   // the page the last raster went into, tried first
    const int MaxPages = 16;

    /// <summary>
    /// One raster per (font, shadow ramp size, glyph, blur, strength). That set is bounded by the glyphs the HUD's fonts can
    /// draw times the shadow parameters the movies use, not by the strings: dynamic text (counts, timers, chat) reuses the
    /// glyphs. Free text could still reach many distinct glyphs (a font's whole Hangul set), so the cache is dropped whole
    /// when MaxPages pages are full; a node keeps the pages its last draw used until it draws again. A node that does not
    /// redraw after a reset (a hidden, kept dialog) so keeps old pages alive, up to MaxPages x 0.5 MB, until it draws.
    /// It lives for the session as the client's does: the shadow rasters sit in the renderer's GlyphCache
    /// (Renderer2D::pGlyphCache, render_renderer2d.h:171), which outlives the movies and is only emptied by ClearCache or
    /// SetParams (render_glyphcache.h:207-208). So a dialog's first close keeps its new glyphs' rasters in the pages.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<(Rid, int, long, float, float), Entry> cache = new();
    const int SlotPadding = 2;              // GlyphCacheParams::SlotPadding

    static (Rid, int, long, float, float) Key(Rid font, int ramp, long glyph, float rb, float strength) =>
        (font, ramp, glyph, MathF.Round(rb, 3), MathF.Round(strength, 3));

    static Entry Get((Rid, int, long, float, float) key)
    {
        if (cache.TryGetValue(key, out var e)) return e;
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
        var origin = gofs + new Vector2(bx0 - pad, by0 - pad);
        if (W + 1 > Page.Width || H + 1 > Page.Height)
        {
            // a raster no page can hold (a large ramp size with a wide blur) keeps a texture of its own, as every raster did
            // before the pages, so its shadow is still drawn
            using var own = Image.CreateFromData(W, H, false, Image.Format.La8, data);
            return new Entry(ImageTexture.CreateFromImage(own), new Rect2(0, 0, W, H), origin, new Vector2(W, H));
        }
        int px = 0, py = 0;
        Page page = null;
        // one empty texel between slots, so the scaled draw's filter reads no neighbour. The page the last raster went
        // into goes first: a field's new glyphs then share one page (one texture, so one batch), where first fit could
        // send glyphs of different heights to different pages and switch the texture glyph by glyph. The others follow
        // before a new page, so a page with room is not left behind.
        if (lastPage != null && lastPage.Place(W + 1, H + 1, out px, out py)) page = lastPage;
        else
            for (int i = 0; i < pages.Count; i++)
                if (pages[i] != lastPage && pages[i].Place(W + 1, H + 1, out px, out py)) { page = pages[i]; break; }
        if (page == null)
        {
            if (pages.Count >= MaxPages)
            {
                // full: start over, as the client's cache does when it has no slot left. The nodes keep the pages
                // their last draw used until they draw again; a page written this frame stays in dirtyPages until the flush.
                pages.Clear();
                cache.Clear();
            }
            page = new Page();
            pages.Add(page);
            page.Place(W + 1, H + 1, out px, out py);
        }
        lastPage = page;
        page.Write(px, py, W, H, data);
        return new Entry(page.Texture, new Rect2(px, py, W, H), origin, new Vector2(W, H));
    }
}
