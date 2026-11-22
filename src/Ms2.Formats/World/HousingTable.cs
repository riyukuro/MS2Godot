using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>
/// The install items' housing rows, CItemDataTable::LoadHousing (@0x14055b030, commonfiles/itemdatatable.cpp:1211-1239):
/// Xml.m2d : table/housing.xml, &lt;ms2&gt;&lt;housing id=...&gt; read by ItemHousing::LoadElement (@0x140548fe0,
/// itemdatastruct.cpp:1473-1486), with each row's money type from the price table (CPriceTable::GetHousing @0x140738d40,
/// pricetable.cpp:78-87).
/// <para>Read are the attributes the cube lock check uses (filter, categoryIndex, trophyID, trophyLevel, interiorLevel,
/// setItemID) and the price table's money type and price. Left out: categoryTag, doNotInstallBlueprint and the price
/// table's ugcHousingBuy (ItemHousing hasPrice), which nothing ported reads.</para>
/// </summary>
public sealed class HousingTable
{
    /// <summary>One ItemHousing (itemdatastruct.cpp:1447-1450 for the defaults).</summary>
    public sealed record Row
    {
        public int ItemId { get; init; }
        /// <summary>"filter" (ItemHousingFilterDefault 100).</summary>
        public int Filter { get; init; } = 100;
        /// <summary>"categoryIndex" (ItemHousingCategoryDefault 10000), the interior table's key.</summary>
        public int CategoryIndex { get; init; } = 10000;
        /// <summary>The price table's ugcHousingMoneyType (the client keeps it in ItemHousing priceCount, default 1).</summary>
        public int MoneyType { get; init; } = 1;
        /// <summary>The price table's ugcHousingDefaultPrice (0 without a price row).</summary>
        public long Price { get; init; }
        /// <summary>"trophyID": the achievement the item needs (0 = none).</summary>
        public uint TrophyId { get; init; }
        /// <summary>"trophyLevel": its grade (default 1).</summary>
        public uint TrophyLevel { get; init; } = 1;
        /// <summary>"interiorLevel": the housing grade the item needs (default 1).</summary>
        public ushort InteriorLevel { get; init; } = 1;
        /// <summary>"setItemID".</summary>
        public uint SetItemId { get; init; }
    }

    /// <summary>One price table row (Unk_PriceHousing, pricetable.cpp:121-136).</summary>
    public readonly record struct Price(long DefaultPrice, int MoneyType);

    readonly Dictionary<int, Row> rows = new();
    readonly Dictionary<uint, Price> prices = new();

    public const string HousingFile = "housing.xml";
    /// <summary>CPriceTable::Load (@0x140738ba0, pricetable.cpp:53-62): the housing prices from these two files, in order.</summary>
    public static readonly string[] PriceFiles = { "shop_maid.xml", "shop_ugcall.xml" };

    /// <summary>GetXmlPath("Table", file): the locale's table folder first (the data keeps the price files in table/kr/),
    /// then the shared one; null when neither has it.</summary>
    public static string EntryOf(PackSet packs, string file)
    {
        return TablePath.Resolve(packs, file);
    }

    public HousingTable(PackSet packs)
    {
        var priceDocs = new List<XDocument>();
        foreach (var file in PriceFiles)
            if (EntryOf(packs, file) is { } e) priceDocs.Add(PackXml.Parse(packs.Read("Xml", e)));
        Load(EntryOf(packs, HousingFile) is { } h ? PackXml.Parse(packs.Read("Xml", h)) : null, priceDocs);
    }

    /// <summary>The table from the xml itself (tests): the housing file and the price files in load order.</summary>
    public HousingTable(XDocument housing, params XDocument[] priceFiles) => Load(housing, priceFiles);

    public int Count => rows.Count;
    /// <summary>Every row, in no set order.</summary>
    public IEnumerable<Row> Rows => rows.Values;

    /// <summary>CItemDataTable::GetHousing: the item's row, null when it has none.</summary>
    public Row Get(int itemId) => rows.TryGetValue(itemId, out var r) ? r : null;

    /// <summary>CPriceTable::GetHousing's row for an id, null when the price table has none.</summary>
    public Price? PriceOf(uint id) => prices.TryGetValue(id, out var p) ? p : null;

    void Load(XDocument housing, IEnumerable<XDocument> priceFiles)
    {
        // CPriceTable::LoadHousing (@0x140738e60, :105-137): every <ms2><key>, CAtlMap::SetAt, so a later row wins
        foreach (var doc in priceFiles)
        {
            if (doc?.Root is not { Name.LocalName: "ms2" } root) continue;
            foreach (var e in root.Elements("key"))
            {
                uint id = UInt(e, "id", 0);
                prices[id] = new Price(Long(e, "ugcHousingDefaultPrice", 0), (int)UInt(e, "ugcHousingMoneyType", 1));
            }
        }
        if (housing?.Root is not { Name.LocalName: "ms2" } hroot) return;
        foreach (var e in hroot.Elements("housing"))
        {
            int id = (int)UInt(e, "id", 0);
            var p = PriceOf((uint)id);
            var row = new Row
            {
                ItemId = id,
                Filter = Int(e, "filter", 100),
                CategoryIndex = Int(e, "categoryIndex", 10000),
                TrophyId = UInt(e, "trophyID", 0),
                TrophyLevel = UInt(e, "trophyLevel", 1),
                InteriorLevel = (ushort)UInt(e, "interiorLevel", 1),
                MoneyType = p?.MoneyType ?? 1,
                Price = p?.DefaultPrice ?? 0,
                SetItemId = UInt(e, "setItemID", 0),
            };
            // std::map::insert: the first row of an id stays
            rows.TryAdd(id, row);
        }
    }

    static int Int(XElement e, string a, int keep) =>
        int.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : keep;
    static uint UInt(XElement e, string a, uint keep) =>
        uint.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : keep;
    static long Long(XElement e, string a, long keep) =>
        long.TryParse((string)e.Attribute(a), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : keep;
}
