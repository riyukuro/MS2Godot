using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// The cube categories' housing grades, UGCCubeCategoryTable::LoadInteriors (@0x140886fc0,
/// commonfiles/ugccubecategorytable.cpp:285-311): Xml.m2d : table/ugcCubeDefaultCategory.xml, &lt;ms2&gt;&lt;list
/// categoryIndex requireHousingLevel ...&gt;, keyed by categoryIndex with CAtlMap::SetAt (a later row wins). GetInterior is
/// the lookup.
/// <para>Read is requireHousingLevel (u16, default 1, the Interior ctor :99), which the cube lock check uses. Left out:
/// housingBonusCount / housingBonusPoint and the bonus map, which nothing ported reads.</para>
/// </summary>
public sealed class CubeInteriorTable
{
    public const string File = "ugccubedefaultcategory.xml";

    readonly Dictionary<int, ushort> requireLevel = new();

    public CubeInteriorTable(PackSet packs)
    {
        if (HousingTable.EntryOf(packs, File) is { } e) Load(PackXml.Parse(packs.Read("Xml", e)));
    }

    /// <summary>The table from the xml itself (tests).</summary>
    public CubeInteriorTable(XDocument doc) => Load(doc);

    public int Count => requireLevel.Count;

    /// <summary>GetInterior(category)->m_nRequireLevel, null when the category has no row.</summary>
    public ushort? RequireLevel(int category) => requireLevel.TryGetValue(category, out var l) ? l : null;

    void Load(XDocument doc)
    {
        if (doc?.Root is not { Name.LocalName: "ms2" } root) return;
        foreach (var e in root.Elements("list"))
        {
            // XmlGetAttr leaves the default when the attribute is missing; the key defaults to 0 (nCategory = 0)
            int category = (int)UInt(e, "categoryIndex", 0);
            ushort level = ushort.TryParse((string)e.Attribute("requireHousingLevel"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var v) ? v : (ushort)1;
            requireLevel[category] = level;
        }
    }

    static uint UInt(XElement e, string a, uint keep) =>
        uint.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : keep;
}
