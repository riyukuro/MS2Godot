using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// Xml.m2d : mapxblock/&lt;xblock&gt;.xml, the field's client-side block (fog, heightfog, clientProperty bgDay, minimap), read as
/// CFieldDataCli::LoadXBlock @0x140a56690 (fielddatacli.cpp:390) does through TinyXML. TiXmlDocument::Parse
/// (tinyxmlparser.cpp:648-657) takes a leading UTF-8 byte order mark as the encoding and skips it, and some of these files
/// carry one (02020120_bf), so the mark is dropped here before the XML parse.
/// </summary>
public static class MapXBlock
{
    public const string Archive = "Xml";

    public static string Entry(string xblock) => $"mapxblock/{xblock}.xml";

    /// <summary>The parsed block; the BOM, if any, is skipped as TinyXML does.</summary>
    public static XDocument Parse(byte[] data) => PackXml.Parse(data);

    public static XDocument Load(PackSet packs, string xblock) => Parse(packs.Read(Archive, Entry(xblock)));

    /// <summary>
    /// The backdrop name a field shows: &lt;clientProperty bgDay&gt; split on "," with one picked at random, as
    /// CUIService::LoadBackgroundMovie (uiservice.cpp:504-530) does with m_strBgDay when the login table has no value.
    /// Null when the block names none.
    /// </summary>
    public static string BgDay(XDocument block, System.Func<int, int> pick)
    {
        var names = ((string)block?.Root?.Element("clientProperty")?.Attribute("bgDay"))?.Split(',')
            .Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
        return names == null || names.Length == 0 ? null : names[System.Math.Clamp(pick(names.Length), 0, names.Length - 1)];
    }
}
