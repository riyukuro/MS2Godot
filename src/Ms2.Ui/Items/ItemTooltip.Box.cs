using System.Globalization;
using Ms2.Quest;

namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409d5920 (astooltip.cpp:2892): a box item (type 0x19) with a box id (ItemFunction::GetBoxId) shows, for a local
    /// player, the box's items for the player's job group and gender (ItemDropTable::GetBoxItems, hidden rows skipped) as
    /// SetItemPackageList's rows.
    /// </summary>
    void SetItemBox(int itemId)
    {
        var d = Tables.Items.Get(itemId);
        if (d == null || d.Type != 0x19) return;
        int boxId = BoxId(d);
        if (boxId == 0 || !model.HasLocalPlayer) return;
        var results = new List<ItemDropEntry>();
        if (!Tables.IndividualDrops.GetBoxItems(Tables.Items, boxId, model.Player.JobGroup, model.Player.Gender, true, results)) return;
        SetItemPackageList(results);
    }

    /// <summary>ItemFunction::GetBoxId (itemdatastruct.cpp:1821): OpenItemBox's first word; OpenItemBoxWithKey's first word,
    /// else its sixth; OpenItemBoxLullu(Simple)'s xml commonBoxId; SelectItemBox's xml individualDropBoxId, else its second
    /// word; OpenGachaBox's gacha box (first word) individualDropBoxID; else 0.</summary>
    int BoxId(ItemData d)
    {
        switch (d.FunctionName)
        {
            case "OpenItemBox": return d.FunctionParam1;
            case "OpenItemBoxWithKey": return d.FunctionParam1 != 0 ? d.FunctionParam1 : d.FunctionArgInt(5, 0);
            case "OpenItemBoxLullu" or "OpenItemBoxLulluSimple" when d.HasFunctionXml: return d.FunctionXmlInt("commonBoxId");
            case "SelectItemBox": return d.HasFunctionXml ? d.FunctionXmlInt("individualDropBoxId") : d.FunctionArgInt(1, 0);
            case "OpenGachaBox": return Tables.GachaBoxes.IndividualDropBoxId(d.FunctionArgInt(0, 0));
            default: return 0;
        }
    }

    /// <summary>SetItemPackageList (astooltip.cpp:5626): with any result, "itemPackageList" is one row per result whose item
    /// has data (ASObj::FUN_1409c5bd0) and the tooltip class is export_common_tooltip_kindsItemPackage.</summary>
    void SetItemPackageList(List<ItemDropEntry> results)
    {
        if (results.Count == 0) return;
        var rows = new List<object>();
        foreach (var r in results)
            if (PackageRow(r) is { } row) rows.Add(row);
        Data["itemPackageList"] = rows;
        Data["tooltip"] = "export_common_tooltip_kindsItemPackage";
    }

    /// <summary>ASObj::FUN_1409c5bd0 (astooltip.cpp:763): one package row.</summary>
    Dictionary<string, object> PackageRow(ItemDropEntry r)
    {
        var d = Tables.Items.Get(r.ItemId);
        if (d == null) return null;
        bool money = IsCurrencyItem(r.ItemId);
        var row = new Dictionary<string, object>();
        if (money) row["itemCount"] = 0;
        else row["itemCount"] = (uint)r.Count;
        if (model.IsFeatureEnabled(0x8c))
        {
            if (d.IsPetItem) { row["itemPetRank"] = r.Rank; row["dropShadowDisable"] = 1; }
        }
        else row["itemPet"] = d.IsPetItem;
        row["itemCash"] = d.IsCashItem;
        row["iconPath"] = d.IconPath;
        row["itemSkin"] = d.HasSkinType;
        string name = Tables.ItemName(d.Id);                                     // GetItemDisplayName
        if (money) row["itemName"] = CommonF("s_tooltip_fmt_in_package_money", name, NumberText.Group(r.Count.ToString(CultureInfo.InvariantCulture)));
        else if (d.HasPeriod) row["itemName"] = CommonF("s_item_end_time_tooltip_expiration_prefix", name);
        else row["itemName"] = name;
        row["itemGrade"] = r.Rank;
        if (d.IsGemstone)
        {
            int level = (ushort)Tables.GemstoneUpgrades.Level(d.Id);             // FUN_140626f20
            if (level > 0) row["gemstoneLevel"] = level;
        }
        else if (r.Enchant > 0) row["gemstoneLevel"] = r.Enchant;
        else if ((uint)d.OpenGachaBoxParam0 > 0) row["itemSeason"] = Tables.GachaBoxes.SeasonText(d.OpenGachaBoxParam0);   // FUN_1409e5320
        row["itemPiece"] = d.IsPieceItem;
        return row;
    }

    /// <summary>IsCurrencyItem (gameentitycommon.cpp:84): FUN_1403ba790 gives the item a currency kind, which
    /// FUN_1403ba590 knows.</summary>
    static bool IsCurrencyItem(int id) => id is 90000001 or 90000002 or 90000003 or 90000004 or 90000006 or 90000011
        or (>= 90000013 and <= 90000018) or 90000020 or (>= 90000022 and <= 90000025) or 90000027;
}
