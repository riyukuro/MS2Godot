using System.Globalization;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemDataTable's colour palettes (LoadColorPalette, commonfiles/itemdatatable.cpp:1134): each &lt;colorPalette id&gt;'s
/// &lt;color&gt; rows as ItemColorEx, keyed by colorSN (CColorPaletteInfo::AddColor :303, a later row with the same colorSN
/// replacing an earlier one). The KMS client loads table/colorpalette.xml (:465), which has no stringKey; the GMS2 data
/// adds table/na/colorpalette_achieve.xml, the same palettes with each colour's stringKey (and the achievement colours),
/// so that file is read in its place when it exists.
/// </summary>
public sealed class ColorPaletteTable
{
    /// <summary>
    /// ItemColorEx: ch0..ch2 (m_aValues), Visible (m_n38: the palette attribute, else ch0), Show2Color (m_bFlag,
    /// show2color), StringKey (m_n2c, the stringColorPalette id), ColorSN (m_nColor) and PaletteId (m_nId). Colours are the
    /// ARGB words as written.
    /// </summary>
    public sealed record Color(uint Ch0, uint Ch1, uint Ch2, uint Visible, bool Show2Color, int StringKey, int ColorSN, int PaletteId)
    {
        public uint Channel(int i) => i switch { 0 => Ch0, 1 => Ch1, _ => Ch2 };
    }

    public static readonly ColorPaletteTable Empty = new();

    readonly Dictionary<int, Dictionary<int, Color>> palettes = new();

    ColorPaletteTable() { }

    public ColorPaletteTable(PackSet packs)
    {
        string file = packs.Exists("Xml", "table/na/colorpalette_achieve.xml") ? "table/na/colorpalette_achieve.xml"
            : packs.Exists("Xml", "table/colorpalette.xml") ? "table/colorpalette.xml" : null;
        if (file == null) return;
        Load(PackXml.Parse(packs.Read("Xml", file)).Root);
    }

    /// <summary>A table of the &lt;ms2&gt; root <paramref name="root"/> (a colorpalette file's document root).</summary>
    public static ColorPaletteTable Parse(System.Xml.Linq.XElement root)
    {
        var t = new ColorPaletteTable();
        t.Load(root);
        return t;
    }

    void Load(System.Xml.Linq.XElement root)
    {
        if (root == null) return;
        foreach (var p in root.Elements("colorPalette"))
        {
            int id = ItemData.Atoi((string)p.Attribute("id") ?? "0");
            var map = palettes[id] = new Dictionary<int, Color>();              // a repeated id is cleared (spInfo->Clear)
            foreach (var c in p.Elements("color"))
            {
                uint ch0 = Hex((string)c.Attribute("ch0"));
                string pal = (string)c.Attribute("palette") ?? "";
                var color = new Color(ch0, Hex((string)c.Attribute("ch1")), Hex((string)c.Attribute("ch2")),
                    pal.Length > 0 ? Hex(pal) : ch0, Flag((string)c.Attribute("show2color")),
                    ItemData.Atoi((string)c.Attribute("stringKey") ?? "0"), ItemData.Atoi((string)c.Attribute("colorSN") ?? "0"), id);
                map[color.ColorSN] = color;
            }
        }
    }

    /// <summary>CItemDataTable::GetPaletteColorBySN (itemdatatable.cpp:802): null without the palette or the colour.</summary>
    public Color BySN(int palette, int colorSN) =>
        palettes.TryGetValue(palette, out var map) && map.TryGetValue(colorSN, out var c) ? c : null;

    /// <summary>ParseHexColor: "0xAARRGGBB" (0 when empty or not hex).</summary>
    static uint Hex(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : 0;
    }

    /// <summary>XmlQuery of a bool: "1" or "true".</summary>
    static bool Flag(string s) => s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
}
