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
        Scope = new UiScope(layout, assets);
        if (layout["library"] is JsonObject lib)
            foreach (var (k, v) in lib) library[k] = new UiScope(new JsonObject
            {
                ["symbols"] = v["symbols"]?.DeepClone(), ["images"] = v["images"]?.DeepClone(), ["subImages"] = v["subImages"]?.DeepClone(),
            }, assets);
    }

    public double StageWidth => (double)Scope.Layout["stage"]["width"];
    public double StageHeight => (double)Scope.Layout["stage"]["height"];

    /// <summary>The root timeline as a movie clip. <paramref name="scale"/> is the scale the caller applies on top (for text crispness).</summary>
    public MovieClipNode BuildRoot(double scale = 1)
    {
        var root = new MovieClipNode(this, Scope, Scope.Layout["root"] as JsonObject, null, scale, scale);
        root.Build();
        return root;
    }

    public MovieClipNode BuildClass(string className, double scale = 1)
    {
        foreach (var (key, sc) in library)
            foreach (var (id, sym) in sc.Symbols)
                if ((string)sym["class"] == className) return UiBuilder.BuildSprite(this, sc, int.Parse(id), scale, scale);
        foreach (var (id, sym) in Scope.Symbols)
            if ((string)sym["class"] == className) return UiBuilder.BuildSprite(this, Scope, int.Parse(id), scale, scale);
        return null;
    }
}
