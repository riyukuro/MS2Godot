namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409e1770 (astooltip.cpp:4808): the item tooltip of an item id at a rank, with no item behind it (a quickslot
    /// item that is gone, a shop product, a reward): false when the item has no data. The class, the pet rank (feature
    /// 0x8c, FUN_1409d7560) or pet flag, the cash / skin / PC bang / VIP / wedding flags, the name (SetItemNameById), the
    /// grade (FUN_1409d55a0: the rank, 1 for 0), kind, movie, housing set block (FUN_1409c8df0), icon path
    /// (FUN_1409d5770), the sell price of a new item at the rank (FUN_1409d5860), box and package lists, the description,
    /// guide description (FUN_1409d5b70), stat description (FUN_1409d6120), extra description at level 0 (FUN_1409d64a0),
    /// maid row (FUN_1409d6730), piece flag, data period (FUN_1409d6a40 with 0 minutes), UGC market limit
    /// (SetUgcItemLimitRegister by data, @0x1409d8800), gemstone level, super chat preview, break ingredients at the rank
    /// with no sockets (FUN_1409d8650), season and pet skin flag; the align is 7 with flag 0x40 or 2, else 5.
    /// STILL-OPEN, so nothing is set for them: a ChatEmoticonAdd item's stat text (FUN_1409daff0) and extra comment
    /// (FUN_1409d7630 = FUN_1409daa30's emoticons).
    /// </summary>
    public bool FillById(int itemId, int rank)
    {
        var d = Tables.Items.Get(itemId);
        if (d == null) return false;
        Data["tooltip"] = "export_common_tooltip_kindsItem";                        // SetTooltipClass
        if (model.IsFeatureEnabled(0x8c))
        {
            if (d.IsPetItem) { Data["itemPetRank"] = rank; Data["dropShadowDisable"] = 1; }   // FUN_1409d7560
        }
        else Data["itemPet"] = d.IsPetItem;                                       // SetItemPetFlag
        Data["itemCash"] = d.IsCashItem;                                          // SetItemCashFlag
        Data["itemSkin"] = d.HasSkinType;                                         // SetItemSkinFlag
        Data["itemPcBang"] = d.PCBang ? 1 : 0;                                    // SetItemPcBangFlag
        Data["itemVIP"] = d.Vip;                                                  // SetItemVIPFlag
        Data["itemWedding"] = d.Wedding;                                          // SetItemWeddingFlag
        Data["itemName"] = Tables.ItemName(itemId);                               // SetItemNameById: FUN_141811980(1, id)
        Data["itemGrade"] = rank != 0 ? rank : 1;                                 // FUN_1409d55a0
        Data["itemKinds"] = Kinds(d.IconCode);                                    // FUN_1409d5610
        if ((Flags & 0x40) == 0) Data["itemMovie"] = d.Movie;                     // SetItemMovie(int) (:2855)
        SetHousingSetItem(itemId);                                                // FUN_1409c8df0
        Data["iconPath"] = d.IconPath;                                            // FUN_1409d5770
        if (Item.Create(d, rank) is { } priced) SetSellPrice(priced);             // FUN_1409d5860: a new CItem at the rank
        SetItemBox(itemId);                                                       // FUN_1409d5920
        SetItemFunctionPackage(itemId);                                           // FUN_1409d7900
        Data["itemDescription"] = Tables.ReservedWords.ReplaceValueWords(           // SetItemDescription(int) (:2916)
            Tables.ItemDescription(itemId, "tooltipDescription"), model.HasLocalPlayer ? model.Player.Name : null);
        SetGuideDescriptionById(d);                                               // FUN_1409d5b70
        SetStatDescriptionById(d);                                                // FUN_1409d6120
        SetExtraDescription(d, rank, 0, 0);                                       // FUN_1409d64a0
        SetMaidInfo(Item.Create(d, rank));                                        // FUN_1409d6730
        Data["itemPiece"] = d.IsPieceItem;                                        // SetItemPiece
        SetDataPeriod(d, 0);                                                      // FUN_1409d6a40
        if (UgcMarketItem(d) && !d.EnableRegisterMeratMarket)                     // SetUgcItemLimitRegister(data) (:3347)
            Data["ugcItemLimitRegister"] = Tables.Common(0x9b9);
        SetGemstoneLevel(itemId);                                                 // FUN_1409d43b0
        SetSuperWorldChatPreview(itemId);                                         // FUN_1409d8300
        SetBreakIngredients(itemId, rank, 0);                                     // FUN_1409d8650
        // FUN_1409d7630: a ChatEmoticonAdd item's extra comment, STILL-OPEN
        SetItemSeason(itemId);                                                    // FUN_1409d77b0
        Data["itemPetSkin"] = d.GemSubtype == 0xb;                                // SetItemPetSkin
        Data.SetTooltipAlign((Flags & 0x40) != 0 || (Flags & 2) != 0 ? 7 : 5);
        return true;
    }

    /// <summary>GetItemRank(id): the item's default rank, its data's rank or 1 when that is 0 (or there is no data).</summary>
    public static int ItemRank(ItemDataTable items, int itemId) => items.Get(itemId) is { Rank: not 0 } d ? d.Rank : 1;

    /// <summary>The usual caller form: ASTooltip(local player, flags, 2).FUN_1409e1770(id, rank).GetValue(), null when the
    /// item has no data.</summary>
    public static AsTooltip ById(UiModel model, int flags, int itemId, int rank)
    {
        var tip = new ItemTooltip(model, flags);
        return tip.FillById(itemId, rank) ? tip.Data : null;
    }

    /// <summary>
    /// FUN_1409d5b70 (astooltip.cpp:2925), "itemGuideDescription" by id: the string table's guideDescription; for an
    /// install object (objCode) wrapped in FormatStringId 0xafa when it has the state change permission and 0xaf9 when it
    /// has trigger control (<see cref="GameTables.ObjectCubeRights"/>); for an install item (<see cref="InstallGuideLines"/>)
    /// the housing level and trophy lines, the member left out when it has no housing row; then m_50
    /// (<see cref="ShopGuideText"/>).
    /// </summary>
    void SetGuideDescriptionById(ItemData d)
    {
        string text = InstallRights(d.InstallObjCode, Tables.ItemDescription(d.Id, "guideDescription"));
        if (d.IsInstallItem && !InstallGuideLines(d.Id, ref text)) return;
        if (!string.IsNullOrEmpty(ShopGuideText))
            text = string.IsNullOrEmpty(text) ? ShopGuideText : text + "\n" + ShopGuideText;
        Data["itemGuideDescription"] = text;
    }

    /// <summary>The objCode rights of the guide description (astooltip.cpp:2932-2938 and :2262-2268): FormatStringId(0xafa)
    /// of the text with HasStateChangePermission, then FormatStringId(0xaf9) of that with IsTriggerControlEnabled.</summary>
    string InstallRights(int objCode, string text)
    {
        var (stateChange, triggerControl) = Tables.ObjectCubeRights(objCode);
        if (stateChange) text = Format(Tables.Common(0xafa), text);
        if (triggerControl) text = Format(Tables.Common(0xaf9), text);
        return text;
    }

    /// <summary>
    /// FUN_1409d5b70's install item lines (astooltip.cpp:2939-2972), false with no housing row (the client returns before
    /// setting the member): with feature 0xdb, an interiorLevel above 0 and priceCount 1, the level (raised to the
    /// category's interior requireLevel) above the own housing grade adds BlockFormat(0x6fa, level); a trophy grade not
    /// cleared adds s_construct_require_trophy with GetRequireAchieveText when that is not empty. Each line goes after a
    /// "\n" when the text is not empty.
    /// </summary>
    bool InstallGuideLines(int itemId, ref string text)
    {
        var h = Tables.Housing?.Get(itemId);
        if (h == null) return false;
        int level = h.InteriorLevel;
        if (model.IsFeatureEnabled(CubeLock.HousingLevelFeature) && level > 0 && h.MoneyType == 1)
        {
            if (Tables.CubeInteriors?.RequireLevel(h.CategoryIndex) is { } require && require > level) level = require;
            if (level > (model.HousingGrade?.Invoke() ?? 0)) text = AppendLine(text, Format(Tables.Common(0x6fa), level));
        }
        if (model.IsAchieveGradeCleared?.Invoke((int)h.TrophyId, (int)h.TrophyLevel) != true)
        {
            string trophy = model.RequireAchieveText?.Invoke((int)h.TrophyId, (int)h.TrophyLevel) ?? "";
            if (trophy.Length != 0) text = AppendLine(text, CommonF("s_construct_require_trophy", trophy));
        }
        return true;
    }

    static string AppendLine(string text, string line) => string.IsNullOrEmpty(text) ? line : text + "\n" + line;

    /// <summary>FUN_1409d6120 (astooltip.cpp:2984), "statDescription" by id: the string table's mainDescription as a
    /// { type 0, text } row when not empty; else a ChatEmoticonAdd item's FUN_1409daff0 text (STILL-OPEN, nothing set).</summary>
    void SetStatDescriptionById(ItemData d)
    {
        string main = Tables.ItemDescription(d.Id, "mainDescription");
        if (main.Length != 0) Data["statDescription"] = new Dictionary<string, object> { ["type"] = 0, ["text"] = main };
    }
}
