using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// ItemGachaBoxTable (ms2/commonfiles/itemgachaboxtable.cpp), the parts a slot's season number reads: Load (:82) takes
/// table/gacha_info.xml's &lt;randomBox&gt; rows by FUN_14028faa0 (one per randomBoxID), read on first use.
/// </summary>
public sealed class GachaBoxTable
{
    sealed record Box(int Id, int Season, int CoinItemId, int ShopId, bool SeasonNumberOnIcon, int IndividualDropBoxId, int CoinAmount, int Ui, int[] SaleEnd);

    readonly PackSet packs;
    SortedDictionary<int, Box> boxes;

    public static readonly GachaBoxTable Empty = new(null);

    public GachaBoxTable(PackSet packs) { this.packs = packs; }

    SortedDictionary<int, Box> Boxes()
    {
        if (boxes != null) return boxes;
        boxes = new();
        if (packs == null || !packs.Exists("Xml", "table/gacha_info.xml")) return boxes;
        var root = GameStrings.Parse(packs.Read("Xml", "table/gacha_info.xml")).Root;
        foreach (var e in FeatureElements.Collect(packs, root, "randomBox", "randomBoxID"))
        {
            int id = ItemData.Atoi((string)e.Attribute("randomBoxID") ?? "");
            // m_nSeason[DAT_143ab0248]: the locale index, 0 = KR (CreateClientPlatform, ms2clientplatform.cpp:29)
            int season = ItemData.Atoi((string)e.Attribute("randomBoxSeason" + LobbyFeatures.LocaleOf(packs)) ?? "");
            int coin = ItemData.Atoi((string)e.Attribute("coinItemID") ?? "");
            int shopId = ItemData.Atoi((string)e.Attribute("shopID") ?? "");
            string onIcon = (string)e.Attribute("seasonNumberOnIcon") ?? "";
            bool shows = onIcon.Equals("true", StringComparison.OrdinalIgnoreCase) || onIcon.Equals("yes", StringComparison.OrdinalIgnoreCase) || onIcon == "1";
            int dropBox = ItemData.Atoi((string)e.Attribute("individualDropBoxID") ?? "");   // m_nIndividualDropBoxID (:111)
            int amount = e.Attribute("coinItemAmount") is { } am ? ItemData.Atoi((string)am) : 1;   // m_nCoinItemAmount (:114), 1 by the ctor
            int ui = ItemData.Atoi((string)e.Attribute("ui") ?? "");                                 // m_nUi (:110)
            boxes[id] = new Box(id, season, coin, shopId, shows, dropBox, amount, ui, SaleEndOf((string)e.Attribute("saleEndDate")));               // m_map[nKey] = spBox
        }
        return boxes;
    }

    /// <summary>saleEndDate (:109, a CMsTime written "year-month-day-hour-minute-second"): its six fields, null when the
    /// attribute is missing or not six numbers (m_saleEndDate stays CMsTime(0), no end).</summary>
    static int[] SaleEndOf(string v)
    {
        if (string.IsNullOrEmpty(v)) return null;
        var f = v.Split('-');
        if (f.Length != 6) return null;
        var r = new int[6];
        for (int i = 0; i < 6; i++) if (!int.TryParse(f[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out r[i])) return null;
        return r;
    }

    /// <summary>GetBox(boxId)->m_nUi: the machine's look (the dialog's uiType), 0 for no box.</summary>
    public int Ui(int boxId) => Boxes().TryGetValue(boxId, out var b) ? b.Ui : 0;
    /// <summary>GetBox(boxId)->m_saleEndDate as year, month, day, hour, minute, second; null for no box or no end.</summary>
    public int[] SaleEnd(int boxId) => Boxes().TryGetValue(boxId, out var b) ? b.SaleEnd : null;

    /// <summary>IsCoinItem (:285).</summary>
    public bool IsCoinItem(int itemId) => Boxes().Values.Any(b => b.CoinItemId == itemId);
    /// <summary>GetRandomBoxIdByCoinItem (:234): the first box (by id) with that coin, else 0.</summary>
    public int RandomBoxIdByCoinItem(int itemId) => Boxes().Values.FirstOrDefault(b => b.CoinItemId == itemId)?.Id ?? 0;
    /// <summary>ItemGachaBoxTable::GetCoinItemIdByShopId (called at uishopdialog.cpp:635, :844, :866; its body is not in the
    /// restored source): the first box (by id) for that shop, else 0.</summary>
    public int CoinItemIdByShopId(int shopId) => Boxes().Values.FirstOrDefault(b => b.ShopId == shopId)?.CoinItemId ?? 0;
    /// <summary>GetBox != null.</summary>
    public bool HasBox(int boxId) => Boxes().ContainsKey(boxId);
    /// <summary>The box's m_nCoinItemID and m_nCoinItemAmount (what LoadGachaBoxes puts in the break table); null for no box.</summary>
    public (int ItemId, int Count)? Coin(int boxId) => Boxes().TryGetValue(boxId, out var b) ? (b.CoinItemId, b.CoinAmount) : null;
    /// <summary>GetBox(boxId)->m_nIndividualDropBoxID, 0 for no box (ItemFunction::GetBoxId, itemdatastruct.cpp:1845).</summary>
    public int IndividualDropBoxId(int boxId) => Boxes().TryGetValue(boxId, out var b) ? b.IndividualDropBoxId : 0;
    /// <summary>IsSeasonNumberOnIcon (:297).</summary>
    public bool IsSeasonNumberOnIcon(int boxId) => Boxes().TryGetValue(boxId, out var b) && b.SeasonNumberOnIcon;

    /// <summary>FUN_1409e5320 (astooltip.cpp:5367): the box's season as "%ld", "" for no box or season 0.</summary>
    public string SeasonText(int boxId)
        => Boxes().TryGetValue(boxId, out var b) && (uint)b.Season > 0 ? b.Season.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
}
