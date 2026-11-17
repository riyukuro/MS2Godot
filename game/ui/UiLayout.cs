using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// How the client places and sizes dialogs on the stage: ASDisplayObject::vfunc_2 alignment, and the CLIK Constraints
/// reflow a resizable dialog runs on setSize (ConstrainMode.REFLOW: each element keeps its distances to the edges it is
/// tied to; tied to both sides it stretches).
/// </summary>
public static class UiLayout
{
    [Flags]
    public enum Edge { Left = 1, Right = 2, Top = 4, Bottom = 8, All = 15 }

    /// <summary>
    /// ASDisplayObject::vfunc_2(mode, dx, dy): 0 centre, 1 top centre, 2 bottom centre, 3 left, 4 right, 5 absolute,
    /// 6 top right, 7 bottom left, 8 bottom right. <paramref name="size"/> is the dialog's AS3 width and height.
    /// Returns the registration point in stage units.
    /// </summary>
    public static Vector2 Align(int mode, Vector2 stage, Vector2 size, float dx, float dy)
    {
        float cx = (int)((stage.X - size.X) * 0.5f), cy = (int)((stage.Y - size.Y) * 0.5f);
        var p = mode switch
        {
            0 => new Vector2(cx, cy), 1 => new Vector2(cx, 0), 2 => new Vector2(cx, stage.Y - size.Y), 3 => new Vector2(0, cy),
            4 => new Vector2(stage.X - size.X, cy), 6 => new Vector2(stage.X - size.X, 0), 7 => new Vector2(0, stage.Y - size.Y),
            8 => new Vector2(stage.X - size.X, stage.Y - size.Y), _ => Vector2.Zero,
        };
        return p + new Vector2(dx, dy);
    }

    /// <summary>
    /// The root timeline's instance of <paramref name="dialogClass"/> at scale 1: CLIK's UIComponent takes the authored
    /// size as its size and resets scaleX/scaleY to 1, so the authored stretch never shows.
    /// </summary>
    public static void ResetRootScale(UiMovie movie, string dialogClass)
    {
        SymbolOfClass(movie, dialogClass, out var id);
        if (movie.Scope.Layout["root"]?["children"] is not JsonArray children) return;
        foreach (var c in children.OfType<JsonObject>())
            if (c["id"] != null && (int)c["id"] == id && c["matrix"] is JsonObject m) { m["a"] = 1.0; m["d"] = 1.0; m["b"] = null; m["c"] = null; }
    }

    /// <summary>The timeline of the symbol exported as <paramref name="className"/> in the movie's own scope.</summary>
    public static JsonObject SymbolOfClass(UiMovie movie, string className, out int id)
    {
        foreach (var (k, sym) in movie.Scope.Symbols)
            if ((string)sym["class"] == className) { id = int.Parse(k); return (JsonObject)sym; }
        id = -1;
        return null;
    }

    /// <summary>
    /// Constraints.update(size) applied to the layout before the dialog is built, so 9-slice children rebuild at their new
    /// size instead of being stretched. <paramref name="orig"/> is the dialog's authored width and height.
    /// </summary>
    public static void Reflow(UiMovie movie, string dialogClass, Vector2 orig, Vector2 size, params (string Name, Edge Edges)[] elements)
    {
        var sym = SymbolOfClass(movie, dialogClass, out _);
        if (sym?["timeline"]?["children"] is not JsonArray children) return;
        float dw = size.X - orig.X, dh = size.Y - orig.Y;
        foreach (var c in children.OfType<JsonObject>())
        {
            var name = (string)c["name"];
            var e = elements.FirstOrDefault(x => x.Name == name);
            if (e.Name == null) continue;
            var m = c["matrix"] as JsonObject ?? new JsonObject();
            c["matrix"] = m;
            float x = UiBuilder.F(m["x"], 0), y = UiBuilder.F(m["y"], 0), a = UiBuilder.F(m["a"], 1), d = UiBuilder.F(m["d"], 1);
            var b = c["id"] == null ? null : movie.Scope.Symbol((int)c["id"])?["bounds"];
            float bw = UiBuilder.F(b?["w"], 0) * a, bh = UiBuilder.F(b?["h"], 0) * d;
            if (e.Edges.HasFlag(Edge.Left) && e.Edges.HasFlag(Edge.Right)) { if (bw > 0) m["a"] = (double)(a * (bw + dw) / bw); }
            else if (e.Edges.HasFlag(Edge.Right)) m["x"] = (double)(x + dw);
            if (e.Edges.HasFlag(Edge.Top) && e.Edges.HasFlag(Edge.Bottom)) { if (bh > 0) m["d"] = (double)(d * (bh + dh) / bh); }
            else if (e.Edges.HasFlag(Edge.Bottom)) m["y"] = (double)(y + dh);
        }
    }
}
