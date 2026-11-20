using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui.Items;

/// <summary>
/// ItemGachaBoxTable (ms2/commonfiles/itemgachaboxtable.cpp), the parts a slot's season number reads: Load (:82) takes
/// table/gacha_info.xml's &lt;randomBox&gt; rows by FUN_14028faa0 (one per randomBoxID), read on first use.
/// </summary>
public sealed class GachaBoxTable
{
    sealed record Box(int Id, int Season, int CoinItemId, int ShopId, bool SeasonNumberOnIcon);

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
            int season = ItemData.Atoi((string)e.Attribute("randomBoxSeason" + LobbyFeatures.LocaleName) ?? "");
            int coin = ItemData.Atoi((string)e.Attribute("coinItemID") ?? "");
            int shopId = ItemData.Atoi((string)e.Attribute("shopID") ?? "");
            string onIcon = (string)e.Attribute("seasonNumberOnIcon") ?? "";
            bool shows = onIcon.Equals("true", StringComparison.OrdinalIgnoreCase) || onIcon.Equals("yes", StringComparison.OrdinalIgnoreCase) || onIcon == "1";
            boxes[id] = new Box(id, season, coin, shopId, shows);               // m_map[nKey] = spBox
        }
        return boxes;
    }

    /// <summary>IsCoinItem (:285).</summary>
    public bool IsCoinItem(int itemId) => Boxes().Values.Any(b => b.CoinItemId == itemId);
    /// <summary>GetRandomBoxIdByCoinItem (:234): the first box (by id) with that coin, else 0.</summary>
    public int RandomBoxIdByCoinItem(int itemId) => Boxes().Values.FirstOrDefault(b => b.CoinItemId == itemId)?.Id ?? 0;
    /// <summary>GetCoinItemIdByShopId (itemgachaboxtable.cpp:155): the first box (by id) for that shop, else 0.</summary>
    public int CoinItemIdByShopId(int shopId) => shopId != 0 ? Boxes().Values.FirstOrDefault(b => b.ShopId == shopId)?.CoinItemId ?? 0 : 0;
    /// <summary>GetBox != null.</summary>
    public bool HasBox(int boxId) => Boxes().ContainsKey(boxId);
    /// <summary>IsSeasonNumberOnIcon (:297).</summary>
    public bool IsSeasonNumberOnIcon(int boxId) => Boxes().TryGetValue(boxId, out var b) && b.SeasonNumberOnIcon;

    /// <summary>FUN_1409e5320 (astooltip.cpp:5367): the box's season as "%ld", "" for no box or season 0.</summary>
    public string SeasonText(int boxId)
        => Boxes().TryGetValue(boxId, out var b) && (uint)b.Season > 0 ? b.Season.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
}
