using System.Xml.Linq;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemExtractionTable (Table/itemExtraction.xml, itemextractiontable.cpp:53): per TargetItemID its ScrollCount. Load reads
/// every &lt;key&gt; (no availability test) and skips one whose ScrollCount is below 1; a later row for the same target wins.
/// </summary>
public sealed class ItemExtractionTable
{
    public static readonly ItemExtractionTable Empty = new(null);

    readonly Dictionary<uint, uint> scrolls = new();

    public ItemExtractionTable(XDocument doc)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return;
        foreach (var e in root.Elements("key"))
        {
            uint count = U(e, "ScrollCount");
            if (count < 1) continue;
            scrolls[U(e, "TargetItemID")] = count;
        }
    }

    /// <summary>HasRow / IsTarget.</summary>
    public bool HasRow(int itemId) => scrolls.ContainsKey((uint)itemId);

    /// <summary>GetData(id)->m_nScrollCount; null without a row.</summary>
    public uint? ScrollCount(int itemId) => scrolls.TryGetValue((uint)itemId, out uint c) ? c : null;

    /// <summary>The target item ids.</summary>
    public IEnumerable<int> ItemIds => scrolls.Keys.Select(k => (int)k);

    static uint U(XElement e, string name) => uint.TryParse((string)e.Attribute(name), out uint v) ? v : 0;
}
