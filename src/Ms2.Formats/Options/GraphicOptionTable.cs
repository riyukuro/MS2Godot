using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Ms2.Formats.Options;

/// <summary>
/// CGraphicOptionTable (client/ms2/graphicoptiontable.cpp), from Table/graphicOption.xml. Load @0x141375e50: each
/// &lt;option name&gt;'s level is the first of option1..option5 that is not 0, counted from 0 ("multisample" keeps all five
/// values instead). The attribute value is read into one int kept across the attributes and the rows (TiXmlElement::Attribute
/// leaves it as it was when the attribute is missing), so a missing attribute repeats the last value read. Each
/// &lt;shadow name&gt; row, read after the options with the same int, gives two levels counted from 0: the first of
/// option1..option5 that is 1 (m_mapShadow1, the simple ground shadow) and the first that is 2 (m_mapShadow2, the real shadow).
/// </summary>
public sealed class GraphicOptionTable
{
    readonly Dictionary<string, int> level = new(StringComparer.Ordinal);     // m_mapLevel
    readonly Dictionary<string, int> shadowSimple = new(StringComparer.Ordinal);   // m_mapShadow1
    readonly Dictionary<string, int> shadowReal = new(StringComparer.Ordinal);     // m_mapShadow2
    public readonly int[] Multisample = new int[5];

    public static GraphicOptionTable Parse(XDocument doc)
    {
        var t = new GraphicOptionTable();
        var root = doc.Root?.Name == "ms2" ? doc.Root : null;
        if (root == null) return t;
        int value = 0;
        foreach (var option in root.Elements("option"))
        {
            string name = (string)option.Attribute("name") ?? "";
            for (int i = 1; i <= 5; i++)
            {
                if (int.TryParse((string)option.Attribute("option" + i), out int v)) value = v;
                if (name == "multisample") t.Multisample[i - 1] = value;
                else if (value != 0)
                {
                    t.level[name] = i - 1;
                    break;
                }
            }
        }
        foreach (var shadow in root.Elements("shadow"))
        {
            string name = (string)shadow.Attribute("name") ?? "";
            bool simpleSet = false, realSet = false;
            for (int i = 1; i <= 5; i++)
            {
                if (int.TryParse((string)shadow.Attribute("option" + i), out int v)) value = v;
                if (value == 1 && !simpleSet) { t.shadowSimple[name] = i - 1; simpleSet = true; }
                if (value == 2 && !realSet) { t.shadowReal[name] = i - 1; realSet = true; }
            }
        }
        return t;
    }

    /// <summary>The level a feature needs (the m_mapLevel lookup); false when the table has none.</summary>
    public bool Supported(string name, int quality) => level.TryGetValue(name, out int min) && quality >= min;

    /// <summary>@0x141376510.</summary>
    public bool IsHdrSupported(int quality) => Supported("hdr", quality);
    /// <summary>@0x141376580.</summary>
    public bool IsMirrorSupported(int quality) => Supported("mirror", quality);
    /// <summary>@0x1413765f0.</summary>
    public bool IsShimmerSupported(int quality) => Supported("shimmer", quality);

    /// <summary>The &lt;shadow&gt; row names, GetShadowOption's kinds 0-5 (graphicoptiontable.cpp:212-243).</summary>
    public const string MyPC = "myPC", PC = "PC", Monster = "monster", BossMonster = "bossMonster", DropItem = "dropItem",
        MapObject = "mapObject";

    /// <summary>GetShadowOption @0x141376880: the level the row's real shadow needs (m_mapShadow2), 5 (never) without one.
    /// For <see cref="MapObject"/> it is the level at which the sun casts shadows and the field's casters are kept
    /// (MS2LightService::UpdateLightShadow ms2lightservice.cpp:356-385, WorldService::FUN_141bbd490 worldservice_1.cpp:3412-3432).</summary>
    public int ShadowOption(string name) => shadowReal.TryGetValue(name, out int v) ? v : 5;

    /// <summary>GetShadowOptionOffset @0x1413769d0: the level the row's simple ground shadow needs (m_mapShadow1), 0 without one.</summary>
    public int ShadowOptionOffset(string name) => shadowSimple.TryGetValue(name, out int v) ? v : 0;
}
