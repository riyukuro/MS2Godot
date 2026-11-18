using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Ms2.Formats.Options;

/// <summary>
/// CGraphicOptionTable (client/ms2/graphicoptiontable.cpp), from Table/graphicOption.xml. Load @0x141375e50: each
/// &lt;option name&gt;'s level is the first of option1..option5 that is not 0, counted from 0 ("multisample" keeps all five
/// values instead). The attribute value is read into one int kept across the attributes and the rows (TiXmlElement::Attribute
/// leaves it as it was when the attribute is missing), so a missing attribute repeats the last value read.
/// </summary>
public sealed class GraphicOptionTable
{
    readonly Dictionary<string, int> level = new(StringComparer.Ordinal);     // m_mapLevel
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
}
