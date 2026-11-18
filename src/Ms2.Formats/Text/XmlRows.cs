using System.Text;
using System.Xml;

namespace Ms2.Formats.Text;

/// <summary>
/// One streaming reader for the Xml.m2d tables, for code that reads rows' attributes and keeps nothing of the document.
/// XDocument.Parse builds the whole tree (every element, attribute and string of the file) before the first row is read;
/// for the string tables that is about two to three times the time and the allocation of reading the same attributes
/// from the reader underneath it. The text and the reader settings are the ones XDocument.Parse uses for
/// <see cref="World.GameStrings.Parse"/>'s input (UTF-8 text with leading BOMs trimmed; XNode.GetXmlReaderSettings for
/// LoadOptions.None: whitespace ignored, DTD parsed, the same entity limit), so attribute values come out the same, and the
/// whole text is read, so a malformed file throws as it does there.
/// </summary>
public static class XmlRows
{
    /// <summary>
    /// A reader over <paramref name="data"/> with XDocument.Parse's settings, reading the text XDocument.Parse is given:
    /// the bytes as UTF-8 (invalid bytes replaced, as Encoding.UTF8.GetString does), leading byte-order marks skipped
    /// (TrimStart('\ufeff')). The text is decoded as the reader goes: a reader over one large string (StringReader) is
    /// several times slower here (27 against 6 ms for the 3 MB item name table).
    /// </summary>
    public static XmlReader Open(byte[] data)
    {
        int start = 0;
        while (start + 2 < data.Length && data[start] == 0xEF && data[start + 1] == 0xBB && data[start + 2] == 0xBF) start += 3;
        var text = new StreamReader(new MemoryStream(data, start, data.Length - start, false), new UTF8Encoding(false), false);
        return XmlReader.Create(text, new XmlReaderSettings
        {
            IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Parse, MaxCharactersFromEntities = 10_000_000, CloseInput = true,
        });
    }

    /// <summary>
    /// Each element named <paramref name="name"/> (no namespace) in document order: at any depth, as
    /// Descendants(name), or with <paramref name="rootChildren"/> only the root's children, as Root.Elements(name). The
    /// reader stands on the element's start tag; read its attributes with GetAttribute (null for a missing one, as the
    /// string cast of a missing XAttribute). The reader must not be moved by the caller.
    /// </summary>
    public static IEnumerable<XmlReader> Elements(byte[] data, string name, bool rootChildren = false)
    {
        using var r = Open(data);
        string atom = r.NameTable.Add(name);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && (object)r.LocalName == atom && r.NamespaceURI.Length == 0 && (!rootChildren || r.Depth == 1))
                yield return r;
    }
}
