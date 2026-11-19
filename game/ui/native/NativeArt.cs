using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>A text field of a baked symbol frame: where it sits (symbol units) and its format from the movie.</summary>
public sealed record NativeTextDef(string Name, Rect2 Rect, JsonNode Def, JsonNode Filters);

/// <summary>
/// One frame of a movie symbol flattened into a Godot texture: the native UI's art. <see cref="Origin"/> is the
/// symbol-space point at the texture's top-left (1 texel per symbol unit times <see cref="PixelScale"/>);
/// <see cref="Grid"/> is the symbol's scale-9 grid, used as nine-patch margins when the widget is resized.
/// Text fields are not drawn into the texture: they come back as <see cref="Texts"/> for native labels.
/// </summary>
public sealed class NativeSkin
{
    public Texture2D Texture;
    public Vector2 Origin, Size;
    public Rect2? Grid;
    public float PixelScale = 1;
    public readonly List<NativeTextDef> Texts = new();
    public Rect2 Rect => new(Origin, Size);

    StyleBoxTexture box;

    /// <summary>Draws the skin into <paramref name="dest"/> (canvas units). With a grid the corners keep their size.</summary>
    public void Draw(CanvasItem ci, Rect2 dest, Color modulate)
    {
        if (Texture == null) return;
        if (Grid is { } g && (Math.Abs(dest.Size.X - Size.X) > 0.5f || Math.Abs(dest.Size.Y - Size.Y) > 0.5f))
        {
            box ??= new StyleBoxTexture
            {
                Texture = Texture,
                TextureMarginLeft = (g.Position.X - Origin.X) * PixelScale, TextureMarginTop = (g.Position.Y - Origin.Y) * PixelScale,
                TextureMarginRight = (Origin.X + Size.X - g.End.X) * PixelScale, TextureMarginBottom = (Origin.Y + Size.Y - g.End.Y) * PixelScale,
                DrawCenter = true,
            };
            box.ModulateColor = modulate;
            // nine-patch margins are in texels; the drawn corners keep their symbol-unit size
            var s = 1f / PixelScale;
            box.ContentMarginLeft = 0;
            ci.DrawSetTransform(dest.Position, 0, new Vector2(s, s));
            box.Draw(ci.GetCanvasItem(), new Rect2(Vector2.Zero, dest.Size * PixelScale));
            ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        ci.DrawTextureRect(Texture, dest, false, modulate);
    }
}

/// <summary>
/// Turns movie symbols into native art at set-up time. It reads the layout the player's own Gfx.m2d gives
/// (<see cref="UiAssets.LoadLayout"/>: symbols, timelines with their per-frame moves, atlas regions) and composes a
/// symbol's frame from its DDS atlas regions into one image. Nothing of the movie runs afterwards: widgets only draw
/// these textures. Symbols are named by export class ("login_btn_login"), by "#id" in the movie, or by a path of instance
/// names under a class ("login_dialog/m_dialog").
/// </summary>
public sealed class NativeArt
{
    readonly UiAssets assets;
    readonly Dictionary<string, List<UiScope>> scopes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, NativeSkin> cache = new();
    readonly Dictionary<Texture2D, Image> atlasImages = new();
    static readonly string[] SharedMovies = { "commonresource", "component_v1" };

    public NativeArt(UiAssets assets) { this.assets = assets; }

    public UiAssets Assets => assets;

    /// <summary>A movie's own scope followed by its imported library scopes.</summary>
    List<UiScope> ScopesOf(string movie)
    {
        if (scopes.TryGetValue(movie, out var list)) return list;
        list = new List<UiScope>();
        var layout = assets.LoadLayout(movie);
        if (layout != null)
        {
            list.Add(new UiScope(layout, assets));
            if (layout["library"] is JsonObject lib)
                foreach (var (_, v) in lib)
                    list.Add(new UiScope(new JsonObject
                    {
                        ["symbols"] = Ms2.Swf.LayoutCodec.Clone(v["symbols"]), ["images"] = v["images"]?.DeepClone(), ["subImages"] = v["subImages"]?.DeepClone(),
                    }, assets));
        }
        scopes[movie] = list;
        return list;
    }

    /// <summary>Finds a symbol: by class in the movie (and its libraries), then in the shared movies.</summary>
    public (UiScope Scope, int Id)? Find(string movie, string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return null;
        var parts = symbol.Split('/');
        var head = FindHead(movie, parts[0]);
        if (head == null) return null;
        var (scope, id) = head.Value;
        for (int i = 1; i < parts.Length; i++)
        {
            var tl = scope.Symbol(id)?["timeline"] as JsonObject;
            var child = (tl?["children"] as JsonArray)?.FirstOrDefault(c => (string)c["name"] == parts[i]);
            if (child == null) return null;
            if (child["id"] is { } cid) id = (int)cid;
            else if (FindClass(movie, (string)child["class"]) is { } r) (scope, id) = r;
            else return null;
        }
        return (scope, id);
    }

    (UiScope, int)? FindHead(string movie, string head)
    {
        if (head.StartsWith('#')) return (ScopesOf(movie)[0], int.Parse(head[1..]));
        return FindClass(movie, head);
    }

    (UiScope, int)? FindClass(string movie, string cls)
    {
        if (cls == null) return null;
        foreach (var m in new[] { movie }.Concat(SharedMovies))
            foreach (var sc in ScopesOf(m))
                if (sc.Symbols.OfClass(cls, out var id) != null) return (sc, id);
        return null;
    }

    public JsonObject Symbol(string movie, string symbol) => Find(movie, symbol) is { } f ? f.Scope.Symbol(f.Id) : null;

    /// <summary>The frame rate of the movie a symbol is found through (its own movie, else the shared one).</summary>
    public double Fps(string movie, string symbol)
    {
        foreach (var m in new[] { movie }.Concat(SharedMovies))
        {
            var sc = ScopesOf(m);
            if (sc.Count == 0) continue;
            bool own = sc.Any(s => s.Symbols.Named.Any(n => n.Class == symbol.Split('/')[0])) || symbol.StartsWith('#');
            if (own) return (double?)sc[0].Layout["fps"] ?? 30;
        }
        return 30;
    }

    /// <summary>Frame labels of a symbol (name -> first frame) and its frame count.</summary>
    public (Dictionary<string, int> Labels, int Frames) Timeline(string movie, string symbol)
    {
        var labels = new Dictionary<string, int>();
        var tl = Symbol(movie, symbol)?["timeline"] as JsonObject;
        if (tl == null) return (labels, 1);
        foreach (var l in tl["labels"] as JsonArray ?? new JsonArray()) labels.TryAdd((string)l["name"], (int)l["frame"]);
        return (labels, (int?)tl["frames"] ?? 1);
    }

    /// <summary>
    /// A named child's placement and alpha on each frame from <paramref name="from"/> to <paramref name="to"/> (the movie's
    /// keyframes and per-frame moves), for replaying a timeline animation as a Godot tween.
    /// </summary>
    public List<(Transform2D Xf, float Alpha)> Track(string movie, string symbol, string child, int from, int to)
    {
        var list = new List<(Transform2D, float)>();
        var tl = Symbol(movie, symbol)?["timeline"] as JsonObject;
        var c = (tl?["children"] as JsonArray)?.FirstOrDefault(x => (string)x["name"] == child);
        if (c == null) return list;
        for (int f = from; f <= to; f++)
        {
            var m = Matrix(c["matrix"]); var col = ColorXf.From(c["color"]);
            foreach (var mv in c["moves"] as JsonArray ?? new JsonArray())
            {
                if ((int)mv["frame"] > f) break;
                if ((bool?)mv["hasMatrix"] == true) m = Matrix(mv["matrix"]);
                if (mv["color"] != null) col = ColorXf.From(mv["color"]);
            }
            list.Add((m, col.Mul.A));
        }
        return list;
    }

    /// <summary>The named instances of a symbol's frame 0 with their placement (for widgets made of parts).</summary>
    public List<(string Name, string Kind, Transform2D Xf, int Id, string Class)> Parts(string movie, string symbol)
    {
        var list = new List<(string, string, Transform2D, int, string)>();
        var tl = Symbol(movie, symbol)?["timeline"] as JsonObject;
        foreach (var c in tl?["children"] as JsonArray ?? new JsonArray())
            if ((string)c["name"] is { } n && ((int?)c["first"] ?? 0) == 0)
                list.Add((n, (string)c["kind"], Matrix(c["matrix"]), (int?)c["id"] ?? -1, (string)c["class"]));
        return list;
    }

    /// <summary>
    /// Bakes one frame of a symbol. <paramref name="skipComponents"/> leaves out children that are UI components (they
    /// become native widgets of their own) and static texts. Null when the symbol is not found.
    /// </summary>
    public NativeSkin Bake(string movie, string symbol, int frame = 0, bool skipComponents = false, float pixelScale = 1, string hide = "")
    {
        var key = $"{movie}|{symbol}|{frame}|{skipComponents}|{pixelScale}|{hide}";
        if (cache.TryGetValue(key, out var s)) return s;
        var f = Find(movie, symbol);
        if (f == null) { GD.PushWarning($"native art: {symbol} not found in {movie}"); return cache[key] = null; }
        var saved = hidden;
        hidden = string.IsNullOrEmpty(hide) ? new HashSet<string>() : hide.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        try { s = BakeSymbol(movie, f.Value.Scope, f.Value.Id, frame, skipComponents, pixelScale); }
        finally { hidden = saved; }
        return cache[key] = s;
    }

    /// <summary>Child names or dotted paths left out of the bake in progress (what a dialog's code hides: visible = false).</summary>
    HashSet<string> hidden = new();

    /// <summary>The frame a label rests on: the frame before the next label (CLIK states play into their stop frame).</summary>
    public static int RestFrame(Dictionary<string, int> labels, int frames, string label)
    {
        if (!labels.TryGetValue(label, out int start)) return -1;
        int next = frames;
        foreach (var v in labels.Values) if (v > start && v < next) next = v;
        return Math.Max(start, next - 1);
    }

    NativeSkin BakeSymbol(string movie, UiScope scope, int id, int frame, bool skipComponents, float k)
    {
        var sym = scope.Symbol(id);
        var b = UiBuilder.Rc(sym?["bounds"]);
        var skin = new NativeSkin { PixelScale = k, Origin = b.Position, Size = b.Size };
        if (sym?["scale9"] is JsonObject g) skin.Grid = UiBuilder.Rc(g);
        int w = Math.Max(1, (int)Math.Ceiling(b.Size.X * k)), h = Math.Max(1, (int)Math.Ceiling(b.Size.Y * k));
        var canvas = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var xf = new Transform2D(new Vector2(k, 0), new Vector2(0, k), -b.Position * k);
        Draw(movie, scope, sym, frame, xf, new ColorXf(), canvas, skin, "", skipComponents, true);
        skin.Texture = ImageTexture.CreateFromImage(canvas);
        return skin;
    }

    struct ColorXf
    {
        public Color Mul, Add;
        public ColorXf() { Mul = Colors.White; Add = new Color(0, 0, 0, 0); }
        public static ColorXf From(JsonNode c)
        {
            var r = new ColorXf();
            if (c == null) return r;
            if (c["mul"] is JsonArray m) r.Mul = new Color(Num(m[0], 1), Num(m[1], 1), Num(m[2], 1), Num(m[3], 1));
            if (c["add"] is JsonArray a) r.Add = new Color(Num(a[0], 0) / 255f, Num(a[1], 0) / 255f, Num(a[2], 0) / 255f, Num(a[3], 0) / 255f);
            return r;
        }
        /// <summary>This (inner) transform followed by <paramref name="outer"/>.</summary>
        public ColorXf Then(ColorXf outer) => new() { Mul = Mul * outer.Mul, Add = Add * outer.Mul + outer.Add };
        public bool Identity => Mul == Colors.White && Add == new Color(0, 0, 0, 0);
    }

    /// <summary>A JSON number (int or double) as float.</summary>
    internal static float Num(JsonNode n, float def) => n == null ? def : float.Parse(n.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    static Transform2D Matrix(JsonNode m)
    {
        if (m == null) return Transform2D.Identity;
        float a = Num(m["a"], 1), d = Num(m["d"], 1), bb = Num(m["b"], 0), c = Num(m["c"], 0);
        return new Transform2D(new Vector2(a, bb), new Vector2(c, d), new Vector2(Num(m["x"], 0), Num(m["y"], 0)));
    }

    static bool IsComponent(JsonObject sym) =>
        sym?["bases"] is JsonArray bs && bs.Any(x => ((string)x)?.EndsWith("UIComponent") == true || ((string)x)?.StartsWith("scaleform.") == true);

    void Draw(string movie, UiScope scope, JsonObject sym, int frame, Transform2D xf, ColorXf cx, Image canvas, NativeSkin skin, string path, bool skipComponents, bool top)
    {
        if (sym == null) return;
        switch ((string)sym["kind"])
        {
            case "shape": DrawShape(scope, sym, xf, cx, canvas); return;
            case "text":
                skin.Texts.Add(new NativeTextDef(path, ToUnits(xf, UiBuilder.Rc(sym["bounds"]), skin), sym["text"], null));
                return;
            case "sprite": break;
            default: return;
        }
        var tl = sym["timeline"] as JsonObject;
        if (tl == null) return;
        int frames = (int?)tl["frames"] ?? 1;
        int f = Math.Min(frame, frames - 1);
        var kids = new List<(int Depth, JsonNode C, Transform2D M, ColorXf Cx)>();
        foreach (var c in tl["children"] as JsonArray ?? new JsonArray())
        {
            int first = (int?)c["first"] ?? 0, last = (int?)c["last"] ?? int.MaxValue;
            if (f < first || f > last) continue;
            if ((bool?)c["visible"] == false || c["clipDepth"] != null) continue;
            var m = Matrix(c["matrix"]); var col = ColorXf.From(c["color"]);
            foreach (var mv in c["moves"] as JsonArray ?? new JsonArray())
            {
                if ((int)mv["frame"] > f) break;
                if ((bool?)mv["hasMatrix"] == true) m = Matrix(mv["matrix"]);
                if (mv["color"] != null) col = ColorXf.From(mv["color"]);
                if (mv["visible"] is { } vis && !(bool)vis) col.Mul.A = 0;
            }
            kids.Add(((int)c["depth"], c, m, col));
        }
        foreach (var (_, c, m, col) in kids.OrderBy(k => k.Depth))
        {
            var ccx = col.Then(cx);
            if (ccx.Mul.A <= 0.001f && ccx.Add.A <= 0) continue;
            string name = (string)c["name"];
            string cpath = name == null ? path : (path.Length == 0 ? name : path + "." + name);
            if (name != null && (hidden.Contains(cpath) || hidden.Contains(name))) continue;
            UiScope cs = scope; JsonObject csym = null;
            if (c["id"] is { } cid) csym = scope.Symbol((int)cid);
            else if (FindClass(movie, (string)c["class"]) is { } r) { cs = r.Item1; csym = r.Item1.Symbol(r.Item2); }
            if (csym == null) continue;
            if (skipComponents && IsComponent(csym)) continue;
            if (skipComponents && (string)csym["kind"] == "text" && top) { skin.Texts.Add(new NativeTextDef(cpath, ToUnits(xf * m, UiBuilder.Rc(csym["bounds"]), skin), csym["text"], c["filters"])); continue; }
            if ((string)csym["kind"] == "text")
            {
                skin.Texts.Add(new NativeTextDef(cpath, ToUnits(xf * m, UiBuilder.Rc(csym["bounds"]), skin), csym["text"], c["filters"]));
                continue;
            }
            var cxf = xf * m;
            // a scale-9 sprite drawn at a non-unit scale: bake it at its own size, then stretch it nine-patch style
            if ((string)csym["kind"] == "sprite" && csym["scale9"] is JsonObject g9 && (Math.Abs(m.X.X - 1) > 0.01f || Math.Abs(m.Y.Y - 1) > 0.01f))
            {
                float k = skin.PixelScale;
                var inner = new NativeSkin { PixelScale = k };
                var bnd = UiBuilder.Rc(csym["bounds"]);
                int w = Math.Max(1, (int)Math.Ceiling(bnd.Size.X * k)), h = Math.Max(1, (int)Math.Ceiling(bnd.Size.Y * k));
                var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
                Draw(movie, cs, csym, 0, new Transform2D(new Vector2(k, 0), new Vector2(0, k), -bnd.Position * k), new ColorXf(), img, inner, cpath, false, false);
                var gr = UiBuilder.Rc(g9);
                var grid = new Rect2((gr.Position - bnd.Position) * k, gr.Size * k);
                var tl0 = cxf * bnd.Position; var br0 = cxf * bnd.End;
                var dest = new Rect2(tl0, br0 - tl0).Abs();
                var stretched = NineResize(img, grid, new Vector2I(Math.Max(1, (int)Math.Round(dest.Size.X)), Math.Max(1, (int)Math.Round(dest.Size.Y))), new Vector2(k, k));
                Blit(canvas, stretched, new Vector2I((int)Math.Round(dest.Position.X), (int)Math.Round(dest.Position.Y)), ccx);
                continue;
            }
            Draw(movie, cs, csym, 0, cxf, ccx, canvas, skin, cpath, skipComponents, false);
        }
    }

    static Rect2 ToUnits(Transform2D xf, Rect2 r, NativeSkin skin)
    {
        var a = xf * r.Position; var b = xf * r.End;
        var px = new Rect2(a, b - a).Abs();
        return new Rect2(px.Position / skin.PixelScale + skin.Origin, px.Size / skin.PixelScale);
    }

    Image AtlasImage(Texture2D tex)
    {
        if (atlasImages.TryGetValue(tex, out var img)) return img;
        img = tex.GetImage();
        if (img != null)
        {
            if (img.IsCompressed()) img.Decompress();
            if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        }
        return atlasImages[tex] = img;
    }

    void DrawShape(UiScope scope, JsonObject sym, Transform2D xf, ColorXf cx, Image canvas)
    {
        foreach (var p in sym["paths"] as JsonArray ?? new JsonArray())
        {
            var a = (JsonArray)p["pts"];
            var pts = new Vector2[a.Count / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = xf * new Vector2((float)(double)a[2 * i], (float)(double)a[2 * i + 1]);
            FillPolygon(canvas, pts, UiBuilder.ParseColor((string)p["fill"], Colors.White), cx);
        }
        foreach (var fill in sym["bitmaps"] as JsonArray ?? new JsonArray())
        {
            var bm = scope.Bitmap((int)fill["id"]);
            if (bm == null) continue;
            var src = AtlasImage(bm.Value.Tex);
            if (src == null) continue;
            var region = bm.Value.Region;
            var fm = fill["matrix"];
            var m = new Transform2D(new Vector2(UiBuilder.F(fm?["a"], 20) / 20f, 0), new Vector2(0, UiBuilder.F(fm?["d"], 20) / 20f), new Vector2(UiBuilder.F(fm?["x"], 0), UiBuilder.F(fm?["y"], 0)));
            var full = xf * m;
            var p0 = full * Vector2.Zero; var p1 = full * region.Size;
            var dest = new Rect2(p0, p1 - p0).Abs();
            var piece = src.GetRegion(new Rect2I((int)region.Position.X, (int)region.Position.Y, (int)region.Size.X, (int)region.Size.Y));
            int dw = Math.Max(1, (int)Math.Round(dest.Size.X)), dh = Math.Max(1, (int)Math.Round(dest.Size.Y));
            if (dw != piece.GetWidth() || dh != piece.GetHeight())
                piece.Resize(dw, dh, (bool?)fill["smooth"] == false && dw % piece.GetWidth() == 0 ? Image.Interpolation.Nearest : Image.Interpolation.Bilinear);
            if (p1.X < p0.X) piece.FlipX();
            if (p1.Y < p0.Y) piece.FlipY();
            Blit(canvas, piece, new Vector2I((int)Math.Round(dest.Position.X), (int)Math.Round(dest.Position.Y)), cx);
        }
    }

    /// <summary>Alpha-blends <paramref name="src"/> into <paramref name="dst"/> at <paramref name="at"/> through a color transform.</summary>
    static void Blit(Image dst, Image src, Vector2I at, ColorXf cx)
    {
        if (!cx.Identity)
        {
            var data = src.GetData();
            for (int i = 0; i < data.Length; i += 4)
            {
                if (data[i + 3] == 0) continue;
                data[i] = Clamp(data[i] * cx.Mul.R + cx.Add.R * 255);
                data[i + 1] = Clamp(data[i + 1] * cx.Mul.G + cx.Add.G * 255);
                data[i + 2] = Clamp(data[i + 2] * cx.Mul.B + cx.Add.B * 255);
                data[i + 3] = Clamp(data[i + 3] * cx.Mul.A + cx.Add.A * 255);
            }
            src = Image.CreateFromData(src.GetWidth(), src.GetHeight(), false, Image.Format.Rgba8, data);
        }
        dst.BlendRect(src, new Rect2I(0, 0, src.GetWidth(), src.GetHeight()), at);
    }

    static byte Clamp(float v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    static void FillPolygon(Image canvas, Vector2[] pts, Color color, ColorXf cx)
    {
        if (pts.Length < 3) return;
        color = new Color(color.R * cx.Mul.R + cx.Add.R, color.G * cx.Mul.G + cx.Add.G, color.B * cx.Mul.B + cx.Add.B, color.A * cx.Mul.A + cx.Add.A);
        if (color.A <= 0.004f) return;
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(canvas.GetWidth() - 1, (int)Math.Ceiling(maxX));
        int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(canvas.GetHeight() - 1, (int)Math.Ceiling(maxY));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Geometry2D.IsPointInPolygon(new Vector2(x + 0.5f, y + 0.5f), pts))
                    canvas.SetPixel(x, y, canvas.GetPixel(x, y).Blend(color));
    }

    /// <summary>Scale-9 stretch of an image: corners keep their size (times <paramref name="cornerScale"/> 1), edges stretch one way, the centre both.</summary>
    static Image NineResize(Image src, Rect2 grid, Vector2I size, Vector2 cornerScale)
    {
        int sw = src.GetWidth(), sh = src.GetHeight();
        int[] sx = { 0, (int)Math.Round(grid.Position.X), (int)Math.Round(grid.End.X), sw };
        int[] sy = { 0, (int)Math.Round(grid.Position.Y), (int)Math.Round(grid.End.Y), sh };
        int l = sx[1], r = sw - sx[2], t = sy[1], b = sh - sy[2];
        // when the target is smaller than the corners, shrink them proportionally (as Flash does)
        float fx = Math.Min(1f, size.X / (float)Math.Max(1, l + r)), fy = Math.Min(1f, size.Y / (float)Math.Max(1, t + b));
        int[] dx = { 0, (int)(l * fx), size.X - (int)(r * fx), size.X };
        int[] dy = { 0, (int)(t * fy), size.Y - (int)(b * fy), size.Y };
        var dst = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                int w0 = sx[i + 1] - sx[i], h0 = sy[j + 1] - sy[j], w1 = dx[i + 1] - dx[i], h1 = dy[j + 1] - dy[j];
                if (w0 <= 0 || h0 <= 0 || w1 <= 0 || h1 <= 0) continue;
                var piece = src.GetRegion(new Rect2I(sx[i], sy[j], w0, h0));
                if (w0 != w1 || h0 != h1) piece.Resize(w1, h1, Image.Interpolation.Bilinear);
                dst.BlitRect(piece, new Rect2I(0, 0, w1, h1), new Vector2I(dx[i], dy[j]));
            }
        return dst;
    }
}
