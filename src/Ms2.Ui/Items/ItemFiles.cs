using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// The item and item model rows as elements, for the readers that walk the XML themselves (the character create page):
/// KMS keeps them in itemdata/&lt;id / 100000&gt;.xml (&lt;item id&gt;) and itemmodel/&lt;id / 100000&gt;.xml (&lt;ItemModel id&gt;); GMS2 keeps
/// one file per item (item/&lt;a&gt;/&lt;bb&gt;/&lt;id&gt;.xml, its available &lt;environment&gt; returned) and per model (itempreset/..., the rows
/// of <see cref="PerFileTable.ItemModels"/>). The KMS files are read where the pack has them, so the KMS data never touches
/// the per-file folders. A data-shape adapter: the client reads its own layout (itemdata.cpp), and the GMS2 client's reader is
/// unread (STILL-OPEN). The rows are shared and read only.
/// </summary>
public sealed class ItemFiles
{
    readonly PackSet packs;
    readonly Dictionary<int, XElement> dataRoots = new(), modelRoots = new();
    readonly Dictionary<int, XElement> perItem = new();
    Dictionary<int, string> index;

    public ItemFiles(PackSet packs) { this.packs = packs; }

    /// <summary>The item's data (KMS: its &lt;item&gt; row; GMS2: the environment of its file), null when the data has none.</summary>
    public XElement Item(int id)
    {
        int prefix = id / 100000;
        string entry = $"itemdata/{prefix}.xml";
        if (packs.Exists("Xml", entry))
        {
            if (!dataRoots.TryGetValue(prefix, out var root)) dataRoots[prefix] = root = PackXml.Parse(packs.Read("Xml", entry))?.Root;
            return root?.Elements("item").FirstOrDefault(e => (string)e.Attribute("id") == id.ToString());
        }
        if (perItem.TryGetValue(id, out var row)) return row;
        index ??= ItemDataTable.PerItemIndex(packs.List("Xml"));
        if (index.TryGetValue(id, out var file) && GameStrings.Parse(packs.Read("Xml", file))?.Root is { } r)
            row = FeatureElements.SelectEnvironment(packs, r);
        return perItem[id] = row;
    }

    /// <summary>The item model row (&lt;ItemModel id&gt;) of a model id, null when the data has none.</summary>
    public XElement Model(int id)
    {
        int prefix = id / 100000;
        if (!modelRoots.TryGetValue(prefix, out var root))
        {
            string entry = $"itemmodel/{prefix}.xml";
            modelRoots[prefix] = root = PerFileTable.Choose(packs.Exists("Xml", entry), () => PackXml.Parse(packs.Read("Xml", entry)), () => PerFileTable.ItemModels(packs, prefix))?.Root;
        }
        return root?.Elements("ItemModel").FirstOrDefault(e => (string)e.Attribute("id") == id.ToString());
    }
}
