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
    Dictionary<string, Character> fontSources;
    SystemFont fallbackFont;

    /// <summary>System font used where the game font has no glyph (and when gfxfontlib is missing).</summary>
    public SystemFont Fallback => fallbackFont ??= new SystemFont { FontNames = new[] { "Noto Sans CJK KR", "Noto Sans CJK JP", "Noto Sans", "DejaVu Sans", "sans-serif" }, Antialiasing = TextServer.FontAntialiasing.Gray };

    /// <summary>The game's UI font for a flash font class such as "$Font_SD_Neo_3", built from the glyph outlines in gfxfontlib. Falls back to a system font.</summary>
    public Font FontFor(string fontClass)
    {
        if (string.IsNullOrEmpty(fontClass)) return Fallback;
        if (fonts.TryGetValue(fontClass, out var f)) return f;
        Font result = Fallback;
        try
        {
            if (fontSources == null)
            {
                fontSources = new Dictionary<string, Character>();
                var m = SwfMovie.Parse(Packs.Read("Resource/Gfx", "gfxfontlib.gfx"));
                foreach (var c in m.Characters.Values) if (c.Font != null && c.ClassName != null) fontSources[c.ClassName] = c;
            }
            if (fontSources.TryGetValue(fontClass, out var src))
            {
                var ff = new FontFile { Data = TtfWriter.Build(src.Font), Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.None, GenerateMipmaps = false };
                ff.Fallbacks = new Godot.Collections.Array<Font> { Fallback };
                result = ff;
            }
            else GD.PushWarning($"UI font class {fontClass} not found in gfxfontlib");
        }
        catch (Exception e) { GD.PushWarning($"UI font {fontClass}: {e.Message}"); }
        fonts[fontClass] = result;
        return result;
    }

    public UiAssets(PackSet packs) { Packs = packs; }

    public JsonObject LoadLayout(string movie)
    {
        var entry = movie.EndsWith(".gfx") ? movie : movie + ".gfx";
        var m = SwfMovie.Parse(Packs.Read("Resource/Gfx", entry));
        return LayoutJson.Build(m, movie, key =>
        {
            var e = Packs.FindByFileName("Resource/Gfx", key + ".gfx");
            return e == null ? null : SwfMovie.Parse(Packs.Read("Resource/Gfx", e));
        });
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
        int Shift(uint m) { if (m == 0) return 0; int s = 0; while ((m >> s & 1) == 0) s++; return s; }
        int Width(uint m) { int s = Shift(m), n = 0; while ((m >> (s + n) & 1) != 0) n++; return n; }
        byte Ex(uint px, uint m) { if (m == 0) return 255; int n = Width(m); uint v = (px & m) >> Shift(m); return (byte)(n >= 8 ? v >> (n - 8) : v * 255 / ((1u << n) - 1)); }
        bool lum = (pfFlags & 0x20000) != 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = 128 + y * stride + x * bpp;
                uint px = 0;
                for (int k = 0; k < bpp; k++) px |= (uint)d[o + k] << (8 * k);
                int q = (y * w + x) * 4;
                if (bits == 8 && (pfFlags & 2) != 0 && rm == 0) { rgba[q] = rgba[q + 1] = rgba[q + 2] = 255; rgba[q + 3] = (byte)px; continue; }   // alpha only
                if (lum) { var l = Ex(px, rm); rgba[q] = rgba[q + 1] = rgba[q + 2] = l; rgba[q + 3] = am != 0 ? Ex(px, am) : (byte)255; continue; }
                rgba[q] = Ex(px, rm); rgba[q + 1] = Ex(px, gm); rgba[q + 2] = Ex(px, bm);
                rgba[q + 3] = (pfFlags & 1) != 0 ? Ex(px, am) : (byte)255;
            }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
    }
}
