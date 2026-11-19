using System.Text.Json.Nodes;
using System.Xml;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Swf;

namespace Ms2Godot.Ui;

/// <summary>
/// Player data for the UI runtime: layouts (built from Resource/Gfx.m2d with Ms2.Swf), DDS atlases, and the flash UI
/// string table (Xml.m2d string/&lt;language&gt;/korflash.xml, keys such as "$s_login_msg_id").
/// </summary>
public sealed class UiAssets
{
    public PackSet Packs { get; }
    readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> strings = new();
    /// <summary>The string folder (GameOptions.StringLanguage, the client's g_strLocaleDir2).</summary>
    public string Language = GameOptions.StringLanguage;
    readonly Dictionary<string, Font> fonts = new();
    SystemFont fallbackFont;

    /// <summary>System font used where the game font has no glyph (and when gfxfontlib is missing).</summary>
    public SystemFont Fallback => fallbackFont ??= new SystemFont { FontNames = new[] { "Noto Sans CJK KR", "Noto Sans CJK JP", "Noto Sans", "DejaVu Sans", "sans-serif" }, Antialiasing = TextServer.FontAntialiasing.Gray };

    /// <summary>The game's UI font for a flash font class such as "$Font_SD_Neo_3", built from the glyph outlines in gfxfontlib. Falls back to a system font.</summary>
    public Font FontFor(string fontClass) => FontFor(fontClass, false);

    /// <summary>
    /// The font for readability text: the same outlines, hinted. Scaleform fits a readable glyph's lowercase and uppercase
    /// tops to whole pixels vertically (GlyphCache::RasterizeGlyph addShapeAutoFit, sfr_render_06d.cpp:949; on when the
    /// font has a layout, TextMeshProvider sfr_render_07o.cpp:953) and rasterizes small glyphs 2.5x wide, so their x is not
    /// snapped (addRasterGlyph :1795); FreeType's light hinting is vertical only, the nearest Godot setting.
    /// </summary>
    /// <paramref name="oversampling"/> &gt; 0 rasterizes the glyphs at the font size times it and draws them back at the
    /// font size: a readable label of whole font size N draws from a raster of the exact quarter-pixel size.
    public Font FontFor(string fontClass, bool readable, float oversampling = 0)
    {
        if (string.IsNullOrEmpty(fontClass)) return Fallback;
        if (readable)
        {
            string key = fontClass + "#readable" + oversampling.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (fonts.TryGetValue(key, out var rf)) return rf;
            var plain = FontFor(fontClass, false);
            Font r = plain is FontFile pf ? new FontFile { Data = pf.Data, Antialiasing = pf.Antialiasing, Hinting = TextServer.Hinting.Light, GenerateMipmaps = false, Fallbacks = pf.Fallbacks, Oversampling = oversampling } : plain;
            fonts[key] = r;
            return r;
        }
        if (fonts.TryGetValue(fontClass, out var f)) return f;
        Font result = Fallback;
        try
        {
            var data = FontBytes(fontClass);
            if (data != null)
            {
                var ff = new FontFile { Data = data, Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.None, GenerateMipmaps = false };
                ff.Fallbacks = new Godot.Collections.Array<Font> { Fallback };
                result = ff;
            }
            else GD.PushWarning($"UI font class {fontClass} not found in gfxfontlib");
        }
        catch (Exception e) { GD.PushWarning($"UI font {fontClass}: {e.Message}"); }
        fonts[fontClass] = result;
        return result;
    }

    // The TTF of a flash font class, built from the glyph outlines in the shared font library. Pure, so it is built once per
    // pack set and a prefetch may build it on another thread; a weak key keeps the cache from outliving the pack set.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<byte[]>>> fontData = new();

    byte[] FontBytes(string fontClass)
    {
        var packs = Packs;
        return fontData.GetOrCreateValue(packs).GetOrAdd(fontClass, k => new Lazy<byte[]>(() =>
        {
            var lib = Dependency("gfxfontlib");
            if (lib == null) return null;
            foreach (var c in lib.Characters.Values)
                if (c.Font != null && c.ClassName == k) return TtfWriter.Build(c.Font);
            return null;
        })).Value;
    }

    /// <summary>
    /// The font's ascent per pixel of font size, as Scaleform lays out a line: DefineFont2/3 Ascent over its EM square
    /// (GFx reads the DefineFont3 ascent / 20 into a 1024 EM). The system fallback's own ascent when the class is not in gfxfontlib.
    /// </summary>
    public float AscentRatio(string fontClass)
    {
        var lib = string.IsNullOrEmpty(fontClass) ? null : Dependency("gfxfontlib");
        if (lib != null)
            foreach (var c in lib.Characters.Values)
                if (c.Font != null && c.ClassName == fontClass && c.Font.HasLayout && c.Font.Ascent > 0) return (float)c.Font.Ascent / c.Font.EmSquare;
        return Fallback.GetAscent(100) / 100f;
    }

    /// <summary>
    /// The font's line height (ascent + descent) and leading per pixel of font size, from the DefineFont2/3 layout as
    /// AscentRatio reads it; the system fallback's own height and no leading when the class is not in gfxfontlib.
    /// </summary>
    public (float Height, float Leading) LineRatios(string fontClass)
    {
        var lib = string.IsNullOrEmpty(fontClass) ? null : Dependency("gfxfontlib");
        if (lib != null)
            foreach (var c in lib.Characters.Values)
                if (c.Font != null && c.ClassName == fontClass && c.Font.HasLayout && c.Font.Ascent > 0)
                    return ((float)(c.Font.Ascent + c.Font.Descent) / c.Font.EmSquare, (float)c.Font.Leading / c.Font.EmSquare);
        return ((Fallback.GetAscent(100) + Fallback.GetDescent(100)) / 100f, 0);
    }

    public UiAssets(PackSet packs) { Packs = packs; Ms2Godot.UiCode.UseCaches(); }

    readonly Dictionary<string, UiMovie> movies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A movie loaded once and shared (library movies such as commonresource).</summary>
    public UiMovie Movie(string name)
    {
        if (!movies.TryGetValue(name, out var m)) movies[name] = m = new UiMovie(this, name);
        return m;
    }

    /// <summary>The shared runtime library every dialog can instantiate classes from (commonresource.gfx).</summary>
    public UiMovie Common => Movie("commonresource");

    /// <summary>A table XML from the Xml pack, e.g. "table/gamemenu.xml" (null when absent).</summary>
    public System.Xml.Linq.XDocument Table(string entry)
    {
        try { return System.Xml.Linq.XDocument.Load(new MemoryStream(Packs.Read("Xml", entry))); }
        catch (Exception e) { GD.PushWarning($"UI table {entry}: {e.Message}"); return null; }
    }

    // Movies a layout depends on (the font library, shared components) are parsed once per pack set and shared by every
    // layout that names them; the parse is pure, so a prefetch may run it on another thread.
    // The cache lives and dies with its pack set (a weak key), so a pack set of an earlier field is never kept alive by it.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SwfMovie>>> dependencies = new();

    public SwfMovie Dependency(string key)
    {
        var packs = Packs;
        return dependencies.GetOrCreateValue(packs).GetOrAdd(key, k => new Lazy<SwfMovie>(() =>
        {
            var e = packs.FindByFileName("Resource/Gfx", k + ".gfx");
            return e == null ? null : SwfMovie.Parse(packs.Read("Resource/Gfx", e));
        })).Value;
    }

    // The movie source every dialog opens from: the parsed movies above, with their ABC blocks and layouts built once per
    // pack set (GFx keeps a loaded movie's definition; each open still makes its own runtime, player and display list).
    // Opened on the main thread only.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Ms2.Avm2.Flash.MovieSource> sources = new();

    public Ms2.Avm2.Flash.MovieSource Movies => sources.GetValue(Packs, p => { var ui = new UiAssets(p); var s = Ms2.Avm2.Flash.MovieSource.FromParsed(ui.Dependency);
        s.Layouts = (k, withLibrary, full, plain) => LayoutCache.Get(p, k, withLibrary, full, plain);
        return s; });

    /// <summary>Parses the shared dependency movies on a worker thread while the field loads.</summary>
    public static void Prefetch(PackSet packs, params string[] keys)
    {
        var ui = new UiAssets(packs);
        System.Threading.Tasks.Task.Run(() => { foreach (var k in keys) try { ui.Dependency(k); } catch { } try { ui.FontBytes("$Font_SD_Neo_1"); } catch { } });
    }

    // A movie's layout is read once per pack set (from the install's layout file, LayoutCache, else parsed), and every load
    // gets its own copy of that template: the copy is what a fresh parse would give. The template itself is never handed
    // out, so nothing a build or a dialog does to its layout reaches the next load. The movie is still built on each load.
    public JsonObject LoadLayout(string movie)
    {
        var packs = Packs;
        JsonObject Parse() => LayoutJson.Build(SwfMovie.Parse(packs.Read("Resource/Gfx", movie.EndsWith(".gfx") ? movie : movie + ".gfx")), movie, Dependency);
        // a key named with its extension builds a layout named so (not the file's)
        var template = movie.EndsWith(".gfx") ? Parse() : LayoutCache.Get(packs, movie, true, Parse, null);
        return (JsonObject)Ms2.Swf.LayoutCodec.Clone(template);
    }

    /// <summary>
    /// The image an exported name stands for, looked up as MovieImpl::FindExportedResource does for an IMG tag's url
    /// (TextField::ProcessImageTags, sfr_gfx_03e.cpp:1441; sfr_gfx_01i.cpp:2534): in the field's own movie
    /// <paramref name="movie"/> and its imports (MovieDefImpl::GetExportedResource, sfr_gfx_04.cpp:1292: its own exports
    /// by name without case, then each imported movie's in import order), then in the root movie <paramref name="root"/>
    /// that imports it. Found: the texture and its rectangle (the image's size), or no texture when the name is not an
    /// image (a sprite is not supported). Null when no movie exports the name.
    /// </summary>
    public (Texture2D Tex, Rect2 Region)? ExportedImage(string movie, string root, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var key = (movie ?? "", root ?? "", name);
        if (exportedImages.TryGetValue(key, out var hit)) return hit;
        var found = Exported(movie, name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (found == null && !string.IsNullOrEmpty(root) && !string.Equals(root, movie, StringComparison.OrdinalIgnoreCase))
            found = Exported(root, name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return exportedImages[key] = found;
    }

    readonly Dictionary<(string, string, string), (Texture2D, Rect2)?> exportedImages = new();

    (Texture2D, Rect2)? Exported(string movie, string name, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(movie) || !seen.Add(movie)) return null;
        var m = Dependency(movie);
        if (m == null) return null;
        // SymbolClass names are exports in an AS3 movie (the game's movies export their images that way)
        foreach (var (id, n) in m.Exports.Concat(m.SymbolClasses))
        {
            if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!m.Characters.TryGetValue(id, out var c)) return (null, default);
            if (c.Kind == Ms2.Swf.CharKind.SubImage && m.Characters.TryGetValue(c.AtlasImageId ?? -1, out var atlas))
                return (Atlas(atlas.ImageFile), new Rect2((float)c.AtlasRect.X, (float)c.AtlasRect.Y, (float)c.AtlasRect.W, (float)c.AtlasRect.H));
            if (c.Kind == Ms2.Swf.CharKind.Image && c.ImageFile != null)
                return (Atlas(c.ImageFile), new Rect2(0, 0, c.ImageWidth, c.ImageHeight));
            return (null, default);
        }
        foreach (var g in m.Imports)
            if (Exported(System.IO.Path.GetFileNameWithoutExtension(g.Url), name, seen) is { } r) return r;
        return null;
    }

    // the atlases of a pack set, shared by its UiAssets (the HUD's, each NPC dialog's, the message boxes'...): an atlas is
    // decoded and uploaded once per session, and its texture is kept for every later open
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Dictionary<string, Texture2D>> atlases = new();

    /// <summary>The DDS atlas next to a movie, e.g. "UILoginDialog_I1.dds" (<see cref="AtlasCache"/>). Null when missing.</summary>
    public Texture2D Atlas(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        var shared = atlases.GetValue(Packs, _ => new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase));
        lock (shared)
        {
            if (shared.TryGetValue(file, out var t)) return t;
            Texture2D tex = null;
            try
            {
                var entry = Packs.FindByFileName("Resource/Gfx", file);
                if (entry != null && AtlasCache.Load(Packs, entry) is { } img) tex = ImageTexture.CreateFromImage(img);
            }
            catch (Exception e) { GD.PushWarning($"UI atlas {file}: {e.Message}"); }
            if (tex == null) GD.PushWarning($"UI atlas {file} not available");
            shared[file] = tex;
            return tex;
        }
    }

    /// <summary>
    /// A DDS or PNG from Resource/Image.m2d, e.g. "map/minimap/02000001.dds" (null when absent), kept for the session.
    /// The client reads the file again on every movie load: GFxImageLoader::LoadProtocolImage (gfximageloader.cpp:171-218)
    /// has no cache, and the Loader's ImageResource sits on the movie heap (sfr_as3_18.cpp:8374-8389), which the dialog's
    /// close unloads (UIDialog::FUN_141924330, UISwfEntity::FUN_1422dc080). The port keeps the textures for the repeat
    /// open's time: re-reading the world map's 203 tiles on every open costs about 0.9 MB of allocation each time. The
    /// cache is bounded by the images the movies name (one set of world map tiles), so repeat opens keep nothing new.
    /// </summary>
    public Texture2D ImageFile(string entry)
    {
        if (string.IsNullOrEmpty(entry)) return null;
        var key = "image:" + entry;
        if (textures.TryGetValue(key, out var t)) return t;
        Texture2D tex = null;
        try
        {
            var name = Packs.Exists("Resource/Image", entry) ? entry : Packs.FindByFileName("Resource/Image", System.IO.Path.GetFileName(entry));
            if (name != null)
            {
                var bytes = Packs.Read("Resource/Image", name);
                Image img;
                if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { img = new Image(); if (img.LoadPngFromBuffer(bytes) != Error.Ok) img = null; }
                else img = DdsLoader.Load(bytes);
                if (img != null) tex = ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception e) { GD.PushWarning($"image {entry}: {e.Message}"); }
        textures[key] = tex;
        return tex;
    }

    public string Str(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        if (strings.Count == 0) LoadStrings();
        return strings.TryGetValue(key, out var v) ? v : key;
    }

    void LoadStrings()
    {
        strings["$"] = "";
        try
        {
            var bytes = Packs.Read("Xml", $"string/{Language}/korflash.xml");
            using var xr = XmlReader.Create(new MemoryStream(bytes));
            while (xr.Read())
                if (xr.NodeType == XmlNodeType.Element && xr.Name == "key")
                {
                    var id = xr.GetAttribute("id"); var val = xr.GetAttribute("value");
                    if (id != null && val != null) strings[id] = val.Replace("\\n", "\n");
                }
        }
        catch (Exception e) { GD.PushWarning($"UI string table: {e.Message}"); }
    }
}

/// <summary>DDS reader for the UI images: uncompressed formats are converted by <see cref="Ms2.Formats.Pixels.DdsPixels.ToRgba"/>, block-compressed ones go through Godot.</summary>
public static class DdsLoader
{
    public static Image Load(byte[] d)
    {
        if (Ms2.Formats.Pixels.DdsPixels.IsBlockCompressed(d)) { var img = new Image(); return img.LoadDdsFromBuffer(d) == Error.Ok ? img : null; }
        var rgba = Ms2.Formats.Pixels.DdsPixels.ToRgba(d, out int w, out int h);
        return rgba == null ? null : Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
    }
}
