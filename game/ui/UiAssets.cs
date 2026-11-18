using System.Text.Json.Nodes;
using System.Xml;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Swf;

namespace Ms2Godot.Ui;

/// <summary>
/// Player data for the UI runtime: layouts (built from Resource/Gfx.m2d with Ms2.Swf), DDS atlases, and the flash UI
/// string table (Xml.m2d string/en/korflash.xml, keys such as "$s_login_msg_id").
/// </summary>
public sealed class UiAssets
{
    public PackSet Packs { get; }
    readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> strings = new();
    public string Language = "en";
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

    public UiAssets(PackSet packs) { Packs = packs; }

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

    /// <summary>Parses the shared dependency movies on a worker thread while the field loads.</summary>
    public static void Prefetch(PackSet packs, params string[] keys)
    {
        var ui = new UiAssets(packs);
        System.Threading.Tasks.Task.Run(() => { foreach (var k in keys) try { ui.Dependency(k); } catch { } try { ui.FontBytes("$Font_SD_Neo_1"); } catch { } });
    }

    // A movie's layout is parsed once per pack set, like the dependency movies above, and every load gets its own copy of
    // that template: the parse is pure, so the copy is what a fresh parse would give. The template itself is never handed
    // out, so nothing a build or a dialog does to its layout reaches the next load. The movie is still built on each load.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<JsonObject>>> layouts = new();

    public JsonObject LoadLayout(string movie)
    {
        var packs = Packs;
        var template = layouts.GetOrCreateValue(packs).GetOrAdd(movie, k => new Lazy<JsonObject>(() =>
        {
            var entry = k.EndsWith(".gfx") ? k : k + ".gfx";
            return LayoutJson.Build(SwfMovie.Parse(packs.Read("Resource/Gfx", entry)), k, Dependency);
        })).Value;
        return (JsonObject)template.DeepClone();
    }

    /// <summary>The DDS atlas next to a movie, e.g. "UILoginDialog_I1.dds". Null when missing.</summary>
    public Texture2D Atlas(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        if (textures.TryGetValue(file, out var t)) return t;
        Texture2D tex = null;
        try
        {
            var entry = Packs.FindByFileName("Resource/Gfx", file);
            if (entry != null)
            {
                var img = DdsLoader.Load(Packs.Read("Resource/Gfx", entry));
                if (img != null) tex = ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception e) { GD.PushWarning($"UI atlas {file}: {e.Message}"); }
        if (tex == null) GD.PushWarning($"UI atlas {file} not available");
        textures[file] = tex;
        return tex;
    }

    /// <summary>A DDS or PNG from Resource/Image.m2d, e.g. "map/minimap/02000001.dds" (null when absent).</summary>
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

/// <summary>DDS reader for the UI atlases: uncompressed channel-mask formats are decoded here, block-compressed ones go through Godot.</summary>
public static class DdsLoader
{
    /// <summary>One channel mask: its shift and bit width, measured once per file instead of once per pixel.</summary>
    readonly struct Channel
    {
        readonly uint mask; readonly int shift, width;
        public Channel(uint m)
        {
            mask = m; shift = 0; width = 0;
            if (m == 0) return;
            while ((m >> shift & 1) == 0) shift++;
            while ((m >> (shift + width) & 1) != 0) width++;
        }
        /// <summary>The channel as 8 bits: the top 8 bits of a wider field, a narrower one scaled to 0..255 (255 without a mask).</summary>
        public byte Get(uint px)
        {
            if (mask == 0) return 255;
            uint v = (px & mask) >> shift;
            return (byte)(width >= 8 ? v >> (width - 8) : v * 255 / ((1u << width) - 1));
        }
    }

    public static Image Load(byte[] d)
    {
        if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' || d[2] != 'S') return null;
        int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
        uint pfFlags = BitConverter.ToUInt32(d, 80), fourCc = BitConverter.ToUInt32(d, 84);
        int bits = BitConverter.ToInt32(d, 88);
        uint rm = BitConverter.ToUInt32(d, 92), gm = BitConverter.ToUInt32(d, 96), bm = BitConverter.ToUInt32(d, 100), am = BitConverter.ToUInt32(d, 104);
        if ((pfFlags & 4) != 0 && fourCc != 0) { var img = new Image(); return img.LoadDdsFromBuffer(d) == Error.Ok ? img : null; }
        if (bits != 32 && bits != 24 && bits != 16 && bits != 8) return null;
        int bpp = bits / 8, stride = w * bpp;
        var rgba = new byte[w * h * 4];
        var r = new Channel(rm); var g = new Channel(gm); var b = new Channel(bm); var a = new Channel(am);
        bool alphaOnly = bits == 8 && (pfFlags & 2) != 0 && rm == 0;
        bool lum = (pfFlags & 0x20000) != 0, hasAlpha = (pfFlags & 1) != 0;
        if (!alphaOnly && !lum && bits == 32 && rm == 0x00ff0000 && gm == 0x0000ff00 && bm == 0x000000ff && (am == 0xff000000 || !hasAlpha))
        {
            // A8R8G8B8 / X8R8G8B8, the usual atlas: every channel is a whole byte, so this is a byte swizzle
            for (int y = 0; y < h; y++)
            {
                int o = 128 + y * stride, q = y * w * 4;
                for (int x = 0; x < w; x++, o += 4, q += 4)
                {
                    rgba[q] = d[o + 2]; rgba[q + 1] = d[o + 1]; rgba[q + 2] = d[o];
                    rgba[q + 3] = hasAlpha ? d[o + 3] : (byte)255;
                }
            }
            return Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = 128 + y * stride + x * bpp;
                uint px = 0;
                for (int k = 0; k < bpp; k++) px |= (uint)d[o + k] << (8 * k);
                int q = (y * w + x) * 4;
                if (alphaOnly) { rgba[q] = rgba[q + 1] = rgba[q + 2] = 255; rgba[q + 3] = (byte)px; continue; }
                if (lum) { var l = r.Get(px); rgba[q] = rgba[q + 1] = rgba[q + 2] = l; rgba[q + 3] = am != 0 ? a.Get(px) : (byte)255; continue; }
                rgba[q] = r.Get(px); rgba[q + 1] = g.Get(px); rgba[q + 2] = b.Get(px);
                rgba[q + 3] = hasAlpha ? a.Get(px) : (byte)255;
            }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
    }
}
