using System.Xml.Linq;

namespace Ms2.Ui;

/// <summary>CTitleTable's tags (Load, commonfiles/titletable.cpp: Table/titleTag.xml &lt;key id tag&gt;): the title id of a tag.</summary>
public sealed class TitleTags
{
    readonly Dictionary<string, uint> tags = new();

    public static readonly TitleTags Empty = new(null, _ => true);

    public TitleTags(XDocument xml, Func<XElement, bool> available)
    {
        foreach (var e in xml?.Root?.Elements("key") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            var tag = (string)e.Attribute("tag") ?? "";
            if (tag.Length > 0 && uint.TryParse((string)e.Attribute("id"), out var id)) tags[tag] = id;   // m_tags.SetAt
        }
    }

    /// <summary>FindTitleId: the id of the tag, 0 without one.</summary>
    public uint FindTitleId(string tag) => tags.TryGetValue(tag, out var id) ? id : 0;
}
