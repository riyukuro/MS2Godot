using System.Xml.Linq;
using Ms2.Formats.Pack;

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
    public static XDocument Parse(byte[] data) => XDocument.Parse(System.Text.Encoding.UTF8.GetString(data).TrimStart('\uFEFF'));

    public static XDocument Load(PackSet packs, string xblock) => Parse(packs.Read(Archive, Entry(xblock)));
}
