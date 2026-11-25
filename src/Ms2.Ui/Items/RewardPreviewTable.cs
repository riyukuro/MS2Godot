using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>RewardPreviewItemTag (rewardpreviewitem.h:24): the badge of a preview item; the order is the sort's (Compare lists the higher tag first).</summary>
public enum RewardPreviewItemTag { None = 0, New = 1, Event = 2 }

/// <summary>
/// RewardPreviewItem (client/ms2/rewardpreviewitem.cpp): one item of a reward preview, its ItemEntity (itemID, grade, count), its tag, the
/// jobs and level range it is shown to, and whether it is tradable.
/// </summary>
public sealed class RewardPreviewItem
{
    public int ItemId, Rank, Count;
    public RewardPreviewItemTag Tag;
    public bool Tradable;
    /// <summary>reqJobs, a comma list of job group ids (XmlTokenList); empty: every job.</summary>
    public readonly List<int> ReqJobs = new();
    /// <summary>reqLevel, "low-high" (GetLevelRangeAttr uidungeoncategory.cpp:210); 0-0: every level.</summary>
    public int ReqLevelLow, ReqLevelHigh;

    /// <summary>Load (@0x140dc2f80): ItemEntity::vfunc_2 (@0x14026ae40: itemID, grade, count), itemTag, reqJobs, reqLevel.</summary>
    public static RewardPreviewItem Load(XElement e)
    {
        var r = new RewardPreviewItem
        {
            ItemId = Int(e, "itemID"), Rank = Int(e, "grade"), Count = Int(e, "count"),
            Tag = ParseTag((string)e.Attribute("itemTag") ?? ""),
        };
        if ((string)e.Attribute("reqJobs") is { } jobs)
            foreach (var t in jobs.Split(',')) r.ReqJobs.Add(ItemData.Atoi(t));
        if ((string)e.Attribute("reqLevel") is { } range)
        {
            var p = range.Split('-');
            if (p.Length == 2)      // a range of another shape is left unread (GetLevelRangeAttr returns 2)
            {
                r.ReqLevelLow = (ushort)ItemData.Atoi(p[0]);
                r.ReqLevelHigh = (ushort)ItemData.Atoi(p[1]);
            }
        }
        return r;
    }

    /// <summary>ParseRewardPreviewItemTag (@0x140dc4ee0): "new" and "event", any case; else none.</summary>
    public static RewardPreviewItemTag ParseTag(string s) =>
        string.Equals(s, "new", StringComparison.OrdinalIgnoreCase) ? RewardPreviewItemTag.New
        : string.Equals(s, "event", StringComparison.OrdinalIgnoreCase) ? RewardPreviewItemTag.Event
        : RewardPreviewItemTag.None;

    /// <summary>Set (@0x140dc3020): the id, rank and tradable flag of a drop result.</summary>
    public static RewardPreviewItem Of(ItemDropEntry result, RewardPreviewItemTag tag = RewardPreviewItemTag.None) =>
        new() { ItemId = result.ItemId, Rank = result.Rank, Tradable = result.Tradable, Tag = tag };

    /// <summary>IsJobValid (@0x140dc3630): no job list passes; else the job group (1 counted as 10) must be listed.</summary>
    public bool IsJobValid(int jobGroup)
    {
        if (ReqJobs.Count == 0) return true;
        if (jobGroup == 1) jobGroup = 10;
        return ReqJobs.Contains(jobGroup);
    }

    /// <summary>IsLevelValid (@0x140dc36e0): 0-0 passes; else the level must lie in the range.</summary>
    public bool IsLevelValid(int level) =>
        (ReqLevelLow == 0 && ReqLevelHigh == 0) || (level >= ReqLevelLow && level <= ReqLevelHigh);

    /// <summary>IsValid (@0x140dc3090).</summary>
    public bool IsValid(int jobGroup, int level) => IsJobValid(jobGroup) && IsLevelValid(level);

    static int Int(XElement e, string a) => ItemData.Atoi((string)e.Attribute(a) ?? "0");

    /// <summary>
    /// RewardPreviewItem::Compare (@0x140dc3140), the preview's sort: false when either item's data is unknown; a higher tag first; a
    /// higher rank first (the data's rank, else the entry's); the type ascending; for type 1 the subtype descending; then the id ascending.
    /// </summary>
    public static bool Less(ItemDataTable items, RewardPreviewItem a, RewardPreviewItem b)
    {
        var da = items.Get(a.ItemId);
        var db = items.Get(b.ItemId);
        if (da == null || db == null) return false;
        if (a.Tag != b.Tag) return a.Tag > b.Tag;
        int ra = da.Rank == 0 ? a.Rank : da.Rank, rb = db.Rank == 0 ? b.Rank : db.Rank;
        if (ra != rb) return ra > rb;
        if (da.Type != db.Type) return da.Type < db.Type;
        if (da.Type == 1 && da.Subtype != db.Subtype) return da.Subtype > db.Subtype;
        return (uint)da.Id < (uint)db.Id;
    }
}

/// <summary>
/// CRewardPreviewTable's dungeon part (@0x140dc4c60 LoadDungeon, rewardpreviewitem.cpp:412): Table/RewardPreview_dungeon.xml, each
/// &lt;preview id&gt; of the &lt;ms2&gt; root by room id (id 0 skipped, a later row of an id replacing the earlier), holding its available
/// &lt;item&gt; children (RewardPreviewInfo::Load @0x140dc37b0). The GMS2 data has it in the region folder (table/na/).
/// STILL-OPEN: the boss part (LoadBoss, GetBoss) is not read; the dungeon room panel does not use it.
/// </summary>
public sealed class RewardPreviewTable
{
    readonly Dictionary<int, List<RewardPreviewItem>> dungeon = new();

    public static readonly RewardPreviewTable Empty = new();

    RewardPreviewTable() { }

    public RewardPreviewTable(PackSet packs)
    {
        string entry = packs == null ? null : TablePath.Resolve(packs, "RewardPreview_dungeon.xml");
        if (entry == null) return;
        var root = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", entry))?.Root;
        if (root is not { Name.LocalName: "ms2" }) return;
        foreach (var p in root.Elements("preview"))
        {
            int id = ItemData.Atoi((string)p.Attribute("id") ?? "0");
            if (id == 0) continue;
            var items = new List<RewardPreviewItem>();
            foreach (var i in p.Elements("item"))
                if (FeatureElements.IsAvailable(packs, i)) items.Add(RewardPreviewItem.Load(i));
            dungeon[id] = items;
        }
    }

    /// <summary>The ids of the rooms that have a preview.</summary>
    public IEnumerable<int> DungeonIds => dungeon.Keys;

    /// <summary>GetDungeon (@0x140dc3d30): the room's preview items, null when it has none.</summary>
    public IReadOnlyList<RewardPreviewItem> GetDungeon(int roomId) => dungeon.TryGetValue(roomId, out var l) ? l : null;
}
