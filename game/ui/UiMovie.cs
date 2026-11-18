using System.Text.Json.Nodes;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>One movie's layout (its own, or a library movie's) with lookups by character id.</summary>
public sealed class UiScope
{
    public JsonObject Layout;
    public UiAssets Assets;
    public Dictionary<int, JsonObject> SubImages = new();
    public Dictionary<int, JsonObject> Images = new();
    public JsonObject Symbols;
    /// <summary>The movie the scope's symbols are defined in (a library's own movie) and the root movie that loads it: where an
    /// IMG tag's export name is looked up (UiAssets.ExportedImage).</summary>
    public string MovieKey, RootKey;

    public UiScope(JsonObject layout, UiAssets assets)
    {
        Layout = layout; Assets = assets;
        Symbols = layout["symbols"] as JsonObject ?? new JsonObject();
        foreach (var n in layout["images"] as JsonArray ?? new JsonArray()) Images[(int)n["id"]] = (JsonObject)n;
        foreach (var n in layout["subImages"] as JsonArray ?? new JsonArray()) SubImages[(int)n["id"]] = (JsonObject)n;
    }

    public JsonObject Symbol(int id) => Symbols.TryGetPropertyValue(id.ToString(), out var n) ? (JsonObject)n : null;

    /// <summary>Texture region (atlas file and rectangle) of a bitmap id used by a shape fill.</summary>
    public (Texture2D Tex, Rect2 Region)? Bitmap(int id)
    {
        if (!SubImages.TryGetValue(id, out var sub)) return null;
        int image = (int)sub["image"];
        if (!Images.TryGetValue(image, out var im)) return null;
        var tex = Assets.Atlas((string)im["file"]);
        if (tex == null) return null;
        return (tex, new Rect2((float)(double)sub["x"], (float)(double)sub["y"], (float)(double)sub["w"], (float)(double)sub["h"]));
    }
}

/// <summary>A loaded movie: builds the node tree of its root timeline or of one of its classes.</summary>
public sealed class UiMovie
{
    public string Name;
    public UiScope Scope;
    public UiAssets Assets;
    readonly Dictionary<string, UiScope> library = new();

    public UiMovie(UiAssets assets, string name)
    {
        Assets = assets; Name = name;
        var layout = assets.LoadLayout(name);
        Scope = new UiScope(layout, assets) { MovieKey = name, RootKey = name };
        if (layout["library"] is JsonObject lib)
            foreach (var (k, v) in lib) library[k] = new UiScope(new JsonObject
            {
                ["symbols"] = v["symbols"]?.DeepClone(), ["images"] = v["images"]?.DeepClone(), ["subImages"] = v["subImages"]?.DeepClone(),
            }, assets) { MovieKey = k, RootKey = name };
    }

    readonly Dictionary<string, UiMovie> externals = new();

    /// <summary>Another movie of the pack loaded whole, for symbols a dialog looks up by name at run time (getDefinitionByName).</summary>
    public UiMovie External(string name)
    {
        if (!externals.TryGetValue(name, out var m)) externals[name] = m = Assets.Movie(name);
        return m;
    }

    public double Fps => Scope.Layout["fps"] == null ? 30 : (double)Scope.Layout["fps"];
    public double StageWidth => (double)Scope.Layout["stage"]["width"];
    public double StageHeight => (double)Scope.Layout["stage"]["height"];

    /// <summary>The root timeline as a movie clip. <paramref name="scale"/> is the scale the caller applies on top (for text crispness).</summary>
    public MovieClipNode BuildRoot(double scale = 1)
    {
        var root = new MovieClipNode(this, Scope, Scope.Layout["root"] as JsonObject, null, scale, scale);
        root.Build();
        return root;
    }

    public MovieClipNode BuildClass(string className, double scale = 1) => BuildClass(className, scale, scale);

    /// <summary>A class instance after setSize(w, h) (CLIK resets the scale to 1): a 9-slice symbol rebuilds its grid at
    /// that size; without a grid the clip is built as is.</summary>
    public MovieClipNode BuildClassSized(string className, double scale, float w, float h)
    {
        foreach (var sc in library.Values.Prepend(Scope))
            foreach (var (id, sym) in sc.Symbols)
                if ((string)sym["class"] == className) return BuildSized(sc, int.Parse(id), sym, scale, w, h);
        return null;
    }

    /// <summary><see cref="BuildClassSized"/> for a symbol of this movie by id (an instance's own symbol).</summary>
    public MovieClipNode BuildSymbolSized(int id, double scale, float w, float h) =>
        Scope.Symbol(id) is { } sym ? BuildSized(Scope, id, sym, scale, w, h) : null;

    /// <summary><see cref="BuildSymbolSized(int, double, float, float)"/> for a symbol of a given scope (a library's).</summary>
    public MovieClipNode BuildSymbolSized(UiScope sc, int id, double scale, float w, float h) =>
        sc.Symbol(id) is { } sym ? BuildSized(sc, id, sym, scale, w, h) : null;

    MovieClipNode BuildSized(UiScope sc, int id, JsonNode sym, double scale, float w, float h)
    {
        NineCtx ctx = null;
        if (sym["scale9"] != null && sym["bounds"] != null)
        {
            var b = UiBuilder.Rc(sym["bounds"]);
            ctx = new NineCtx { Grid = UiBuilder.Rc(sym["scale9"]), Bounds = b, Sx = w / Math.Max(b.Size.X, 0.001f), Sy = h / Math.Max(b.Size.Y, 0.001f) };
        }
        return UiBuilder.BuildSprite(this, sc, id, scale, scale, ctx);
    }

    /// <summary>
    /// A class placement drawn at (<paramref name="csx"/>, <paramref name="csy"/>): when the class symbol (looked up as
    /// <see cref="BuildClass(string, double, double)"/> does) is a sprite with a 9-slice grid, it is built at that size with
    /// the grid, as Scaleform slices the nested clips of a scaled scale9 sprite; otherwise null.
    /// </summary>
    public MovieClipNode BuildClassNine(string className, double scale, double csx, double csy)
    {
        if (className == null || (Math.Abs(csx - 1) <= 1e-3 && Math.Abs(csy - 1) <= 1e-3)) return null;
        foreach (var sc in library.Values.Append(Scope))
            foreach (var (id, sym) in sc.Symbols)
                if ((string)sym["class"] == className)
                {
                    if (sym["scale9"] == null || sym["bounds"] == null || (string)sym["kind"] != "sprite") return null;
                    var b = UiBuilder.Rc(sym["bounds"]);
                    return BuildSized(sc, int.Parse(id), sym, scale, b.Size.X * (float)csx, b.Size.Y * (float)csy);
                }
        return null;
    }

    public MovieClipNode BuildClass(string className, double sx, double sy)
    {
        if (className == null) return null;
        foreach (var (key, sc) in library)
            foreach (var (id, sym) in sc.Symbols)
                if ((string)sym["class"] == className) return UiBuilder.BuildSprite(this, sc, int.Parse(id), sx, sy);
        foreach (var (id, sym) in Scope.Symbols)
            if ((string)sym["class"] == className) return UiBuilder.BuildSprite(this, Scope, int.Parse(id), sx, sy);
        return null;
    }
}
