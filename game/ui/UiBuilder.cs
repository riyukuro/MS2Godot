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

    public float MapX(float x)
    {
        float bw = Bounds.Size.X, W = bw * Sx, k = (W - (bw - Grid.Size.X)) / Math.Max(Grid.Size.X, 0.001f);
        if (x <= Grid.Position.X) return x;
        if (x >= Grid.End.X) return x + (W - bw);
        return Grid.Position.X + (x - Grid.Position.X) * k;
    }

    public float MapY(float y)
    {
        float bh = Bounds.Size.Y, H = bh * Sy, k = (H - (bh - Grid.Size.Y)) / Math.Max(Grid.Size.Y, 0.001f);
        if (y <= Grid.Position.Y) return y;
        if (y >= Grid.End.Y) return y + (H - bh);
        return Grid.Position.Y + (y - Grid.Position.Y) * k;
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
        float size = UiBuilder.F(Def["size"], 12);
        float asx = (float)sx, asy = (float)sy;
        Label = new Label
        {
            Position = new Vector2(Bounds.Position.X * asx, Bounds.Position.Y * asy),
            Size = new Vector2(Bounds.Size.X * asx, Bounds.Size.Y * asy),
            HorizontalAlignment = (string)Def["align"] switch { "center" => HorizontalAlignment.Center, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left },
            VerticalAlignment = VerticalAlignment.Top, ClipText = false, MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = (bool)(Def["wordWrap"] ?? false) ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            LabelSettings = new LabelSettings { Font = font, FontSize = Math.Max(1, (int)Math.Round(size * asy)), FontColor = UiBuilder.ParseColor((string)Def["color"], Colors.Black) },
            Scale = new Vector2(1f / asx, 1f / asy), Text = scope.Assets.Str((string)Def["initial"] ?? ""),
        };
        // label sits in unscaled units: position uses the scaled rect, the node scale cancels the accumulated scale
        Label.Position = new Vector2(Bounds.Position.X, Bounds.Position.Y - 0.12f * size);   // Noto's line box sits lower than the flash font's
        Label.Size = new Vector2(Bounds.Size.X * asx, Bounds.Size.Y * asy);
        Label.PivotOffset = Vector2.Zero;
        AddChild(Label);
    }

    public string Text { get => Label.Text; set => Label.Text = value ?? ""; }
}
