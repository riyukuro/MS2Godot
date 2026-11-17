using Godot;
using Godot.Collections;

namespace Ms2Godot.Ui;

/// <summary>
/// A text field's blurred drop shadow or glow, made the way Scaleform's glyph cache makes it (GlyphCache::RasterizeShadow): every
/// glyph is rasterized at the shadow ramp size (SnapShadowSizeToRamp) with gamma 0.4, padded by SlotPadding (2) plus the whole
/// blur, blurred by the recursive gaussian on rows then columns with BlurY = BlurX, strengthened around bias 8
/// (strengthenImage: int((v - 8) * strength + 0.5) + 8, clamped; no bias at strength 1 or less) and drawn scaled to the screen
/// size, tinted with the shadow colour, under the face and offset by angle and distance.
/// It is a child of the field's label drawn behind it, and the glyphs sit where the label's own layout puts them: the pen
/// position of each glyph cluster is the label's character bounds (Label.GetCharacterBounds, the label's shaped lines), the
/// baseline is the label's (line top plus the line ascent, centred in the font height, floored as the face is). A line's
/// glyph ids, fonts and in-cluster advances come from that line's text shaped with the label's font and size.
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
        Name = "GlyphShadow";
        ShowBehindParent = true;
    }

    public GlyphShadow() { }

    public override void _EnterTree() => text.Label.Draw += QueueRedraw;     // the label redraws: its text, settings or layout changed

    public override void _ExitTree() => text.Label.Draw -= QueueRedraw;

    public override void _Draw()
    {
        drawn.Clear();
        var label = text.Label;
        var ls = label.LabelSettings;
        if (ls?.Font == null || string.IsNullOrEmpty(label.Text) || color.A <= 0) return;
        var font = ls.Font;
        int fontSize = ls.FontSize;
        float screen = fontSize * text.RasterScale;
        int ramp = (int)TextNode.ShadowRampSize(screen);
        float rb = blur * ramp / screen, k = fontSize / (float)ramp;    // raster blur in shadow pixels; label pixels per shadow pixel
        float fontH = (float)font.GetHeight(fontSize);
        var ts = TextServerManager.GetPrimaryInterface();

        // code points: Godot positions count UTF-32 characters
        var cps = new List<string>();
        for (int c = 0; c < label.Text.Length; c += char.IsSurrogatePair(label.Text, c) ? 2 : 1)
            cps.Add(char.IsSurrogatePair(label.Text, c) ? label.Text.Substring(c, 2) : label.Text.Substring(c, 1));
        int n = label.VisibleCharacters >= 0 ? Math.Min(cps.Count, label.VisibleCharacters) : cps.Count;

        int i = 0;
        while (i < n)
        {
            var top = label.GetCharacterBounds(i);
            if (top.Size.Y <= 0) { i++; continue; }                     // a line break, or a line the label does not show
            int j = i + 1;
            while (j < n) { var r = label.GetCharacterBounds(j); if (r.Size.Y <= 0 || r.Position.Y != top.Position.Y) break; j++; }
            DrawLine(ts, label, font, fontSize, fontH, string.Concat(cps.GetRange(i, j - i)), i, top.Position.Y, ramp, rb, k);
            i = j;
        }
    }

    void DrawLine(TextServer ts, Label label, Font font, int fontSize, float fontH, string line, int first, float lineTop, int ramp, float rb, float k)
    {
        var tl = new TextLine();
        tl.AddString(line, font, fontSize);
        var rid = tl.GetRid();
        float asc = (float)ts.ShapedTextGetAscent(rid), dsc = (float)ts.ShapedTextGetDescent(rid);
        if (asc + dsc < fontH) asc += (fontH - (asc + dsc)) / 2;
        float baseline = lineTop + asc;
        float pen = 0;
        foreach (Dictionary g in ts.ShapedTextGetGlyphs(rid))
        {
            int start = (int)g["start"], count = (int)g["count"], repeat = (int)g["repeat"];
            long index = (long)g["index"];
            float advance = (float)g["advance"];
            if (count > 0)
            {
                var b = label.GetCharacterBounds(first + start);
                if (b.Size.Y > 0) pen = b.Position.X;
            }
            if (index != 0)
            {
                var e = Get((Rid)g["font_rid"], ramp, index, rb, strength);
                if (e.Texture != null)
                {
                    var o = (Vector2)g["offset"];
                    var at = new Vector2(pen + o.X, MathF.Floor(baseline + o.Y)) + e.Origin * k + offset;
                    DrawTextureRect(e.Texture, new Rect2(at, e.Size * k), false, color);
                    drawn.Add(e.Texture);
                }
            }
            pen += advance * repeat;
        }
        tl.Dispose();
    }

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
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<(ulong, int, long, float, float), Entry> cache = new();
    const int CacheLimit = 4096;
    const int SlotPadding = 2;              // GlyphCacheParams::SlotPadding

    static Entry Get(Rid font, int ramp, long glyph, float rb, float strength)
    {
        rb = MathF.Round(rb, 3); strength = MathF.Round(strength, 3);
        var key = (font.Id, ramp, glyph, rb, strength);
        if (cache.TryGetValue(key, out var e)) return e;
        if (cache.Count >= CacheLimit) cache.Clear();
        return cache[key] = Rasterize(font, ramp, glyph, rb, strength);
    }

    static Entry Rasterize(Rid font, int ramp, long glyph, float rb, float strength)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        var size = new Vector2I(ramp, 0);
        long ti = ts.FontGetGlyphTextureIdx(font, size, glyph);
        if (ti < 0) return default;
        var uv = ts.FontGetGlyphUVRect(font, size, glyph);
        var gofs = ts.FontGetGlyphOffset(font, size, glyph);
        var img = ts.FontGetTextureImage(font, size, ti);
        if (img == null) return default;
        int x0 = (int)uv.Position.X, y0 = (int)uv.Position.Y, w = (int)uv.Size.X, h = (int)uv.Size.Y;
        if (w <= 0 || h <= 0) return default;

        // the glyph's coverage, cut to its inked box (the rasterizer's cell bounds)
        var cov = new byte[w * h];
        int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte a = (byte)Math.Clamp((int)MathF.Round(img.GetPixel(x0 + x, y0 + y).A * 255), 0, 255);
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
        var tex = ImageTexture.CreateFromImage(Image.CreateFromData(W, H, false, Image.Format.La8, data));
        return new Entry(tex, gofs + new Vector2(bx0 - pad, by0 - pad), new Vector2(W, H));
    }
}
