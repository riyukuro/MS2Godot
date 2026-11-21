using System.Text;
using Ms2.Formats.Pack;
using System.Xml.Linq;

namespace Ms2.Formats.Text;

/// <summary>
/// The one reader for an XML entry of a pack (a table in Xml.m2d, a model's companion .xml). The client reads these through
/// TinyXML (TiXmlDocument::Parse, TIXML_ENCODING_UNKNOWN): it skips a leading UTF-8 byte-order mark, takes the bytes as UTF-8
/// whatever an encoding declaration says, and has no UTF-16 reader. The same here, so a file the client loads loads here:
/// a UTF-8 mark (KMS2_DEV Character/male/male.xml and female/female.xml have one) is dropped, not left for XDocument.Parse to reject with "Data at
/// the root level is invalid. Line 1, position 1". A UTF-16 mark is decoded as UTF-16 (the client would fail that file; we
/// read it rather than stop a field).
/// </summary>
public static class PackXml
{
    /// <summary>The entry's text with any byte-order mark removed.</summary>
    public static string Text(byte[] data)
    {
        int n = data.Length;
        if (n >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, n - 2).TrimStart('﻿');
        if (n >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, n - 2).TrimStart('﻿');
        int start = 0;
        while (start + 2 < n && data[start] == 0xEF && data[start + 1] == 0xBB && data[start + 2] == 0xBF) start += 3;
        return Encoding.UTF8.GetString(data, start, n - start).TrimStart('﻿');
    }

    public static XDocument Parse(byte[] data) => XDocument.Parse(Text(data));

    public static XDocument Read(PackSet packs, string archive, string entry) => Parse(packs.Read(archive, entry));
}
