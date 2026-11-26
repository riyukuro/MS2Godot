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
    /// show2color), StringKey (m_n2c, the stringColorPalette id), ColorSN (m_nColor, -1 without the attribute: itemdata.h:310) and PaletteId (m_nId). Colours are the
    /// ARGB words as written. AchieveId (m_n24, achieveID) and AchieveGrade (m_n28, achieveGrade) tie a colour to the trophy
    /// grade whose type 8 reward unlocks it (itemdatatable.cpp:1171), 0 without the attribute (itemdatastruct.cpp:262).
    /// </summary>
    public sealed record Color(uint Ch0, uint Ch1, uint Ch2, uint Visible, bool Show2Color, int StringKey, int ColorSN, int PaletteId,
        uint Eye0 = 0, uint Eye2 = 0, int AchieveId = 0, int AchieveGrade = 0)
    {
        public uint Channel(int i) => i switch { 0 => Ch0, 1 => Ch1, _ => Ch2 };
    }

    public static readonly ColorPaletteTable Empty = new();

    /// <summary>CColorPaletteInfo: the colours in file order (m_spColors) and by colorSN (m_spColorMap).</summary>
    sealed class Info { public readonly List<Color> Rows = new(); public readonly Dictionary<int, Color> BySN = new(); }

    readonly Dictionary<int, Info> palettes = new();
    static readonly IReadOnlyList<Color> NoRows = Array.Empty<Color>();

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
            var info = palettes[id] = new Info();                                 // a repeated id is cleared (spInfo->Clear)
            foreach (var c in p.Elements("color"))
            {
                uint ch0 = Hex((string)c.Attribute("ch0"));
                string pal = (string)c.Attribute("palette") ?? "";
                var color = new Color(ch0, Hex((string)c.Attribute("ch1")), Hex((string)c.Attribute("ch2")),
                    pal.Length > 0 ? Hex(pal) : ch0, Flag((string)c.Attribute("show2color")),
                    ItemData.Atoi((string)c.Attribute("stringKey") ?? "0"), ItemData.Atoi((string)c.Attribute("colorSN") ?? "-1"), id,
                    Hex((string)c.Attribute("ch0_eye")), Hex((string)c.Attribute("ch2_eye")),
                    ItemData.Atoi((string)c.Attribute("achieveID") ?? "0"), ItemData.Atoi((string)c.Attribute("achieveGrade") ?? "0"));
                info.Rows.Add(color);                                          // AddColor: the array keeps every row,
                info.BySN[color.ColorSN] = color;                              // the map a later colorSN replaces
            }
        }
    }

    /// <summary>CItemDataTable::GetPaletteColorBySN (itemdatatable.cpp:802): null without the palette or the colour.</summary>
    public Color BySN(int palette, int colorSN) =>
        palettes.TryGetValue(palette, out var info) && info.BySN.TryGetValue(colorSN, out var c) ? c : null;

    /// <summary>CItemDataTable::GetPaletteColors (itemdatatable.cpp:820): the palette's colours in file order, a repeated colorSN
    /// included; empty without the palette.</summary>
    public IReadOnlyList<Color> Rows(int palette) => palettes.TryGetValue(palette, out var info) ? info.Rows : NoRows;

    /// <summary>ParseHexColor: strtoul base 16, so an optional "0x" and then the leading hex digits ("ff00zz" is 0xff00); 0 when
    /// there are none.</summary>
    static uint Hex(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.TrimStart();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        int n = 0;
        while (n < s.Length && Uri.IsHexDigit(s[n])) n++;
        if (n == 0) return 0;
        return n > 8 ? uint.MaxValue : uint.Parse(s[..n], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    /// <summary>XmlQuery of a bool: "1" or "true".</summary>
    static bool Flag(string s) => s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
}
