using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemSocketTable (ms2/commonfiles/itemsocketsystemutil.cpp), the parts the item tooltip reads. Load (:1368) takes
/// each available &lt;itemSocket&gt; of table/itemSocket.xml with an id into that id's rows, in file order. It is read on
/// first use.
/// </summary>
public sealed class ItemSocketTable
{
    /// <summary>CItemSocketData (ctor :1316: grade, maxCount, openType 0): "grade", "maxCount", "openType", and the
    /// minimum of the open range ("fixOpenCount" for open type 0).</summary>
    sealed record Row(int Grade, uint MaxCount, int OpenType, int OpenMin);

    readonly PackSet packs;
    Dictionary<int, List<Row>> rows;

    public static readonly ItemSocketTable Empty = new(null);

    public ItemSocketTable(PackSet packs) { this.packs = packs; }

    Dictionary<int, List<Row>> Rows()
    {
        if (rows != null) return rows;
        rows = new();
        if (packs == null || !packs.Exists("Xml", "table/itemsocket.xml")) return rows;
        foreach (var e in GameStrings.Parse(packs.Read("Xml", "table/itemsocket.xml")).Root.Elements("itemSocket"))
        {
            if (!FeatureElements.IsAvailable(packs, e)) continue;
            int id = ItemData.Atoi((string)e.Attribute("id") ?? "");
            if (id == 0) continue;
            int grade = ItemData.Atoi((string)e.Attribute("grade") ?? "0");
            uint max = (uint)ItemData.Atoi((string)e.Attribute("maxCount") ?? "0");
            int openType = ItemData.Atoi((string)e.Attribute("openType") ?? "0");
            // open type 1 reads randomOpenRange into the range; its minimum is not read here (GetInitialSocketCount
            // reads it only for open type 0)
            int min = openType == 0 ? ItemData.Atoi((string)e.Attribute("fixOpenCount") ?? "0") : 0;
            if (!rows.TryGetValue(id, out var list)) rows[id] = list = new();
            list.Add(new Row(grade, max, openType, min));
        }
        return rows;
    }

    /// <summary>CItemSocketTable::Find (:1432) and CItemSocketDataContainer::Find (:1337): the row of the grade, else
    /// the last grade 0 row; null when none.</summary>
    Row Find(int id, int grade)
    {
        if (!Rows().TryGetValue(id, out var list)) return null;
        Row fallback = null;
        foreach (var r in list)
        {
            if (r.Grade == grade) return r;
            if (r.Grade == 0) fallback = r;
        }
        return fallback;
    }

    /// <summary>IsSocketFeatureEnabled (:1499): feature 0x2e, and for a skin item also 0x5e.</summary>
    static bool SocketFeature(ItemData d, Func<int, bool> feature) => feature(0x2e) && (!d.HasSkinType || feature(0x5e));

    /// <summary>
    /// FUN_14062d980 (:1512), the transfer's m_b41 (CItemTransfer::InitFromItem): true without a socketDataId; false
    /// without its row; else whether the max count (GetMaxSocketCount :1448) is above the initial count
    /// (GetInitialSocketCount :1473).
    /// </summary>
    public bool SocketCanGrow(ItemData d, int grade, Func<int, bool> feature)
    {
        int id = d.SocketDataId;
        if (id == 0) return true;
        var r = Find(id, grade);
        if (r == null) return false;
        bool on = SocketFeature(d, feature);
        uint max = !on || d.IsLapenshard ? 0 : r.MaxCount;
        uint start = !on ? 1 : r.OpenType == 0 ? (uint)r.OpenMin : 0;
        return max > start;
    }
}
