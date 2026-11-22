namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409e1770 (astooltip.cpp:4808): the item tooltip of an item id at a rank, with no item behind it (a quickslot
    /// item that is gone, a shop product, a reward): false when the item has no data. The class, the pet rank (feature
    /// 0x8c, FUN_1409d7560) or pet flag, the cash / skin / PC bang / VIP / wedding flags, the name (SetItemNameById), the
    /// grade (FUN_1409d55a0: the rank, 1 for 0), kind, movie, icon path (FUN_1409d5770), the sell price of a new item at
    /// the rank (FUN_1409d5860), box and package lists, the description, guide description (FUN_1409d5b70), stat
    /// description (FUN_1409d6120), extra description at level 0 (FUN_1409d64a0), maid row (FUN_1409d6730), piece flag,
    /// data period (FUN_1409d6a40 with 0 minutes), UGC market limit (SetUgcItemLimitRegister by data, @0x1409d8800),
    /// gemstone level, super chat preview, break ingredients at the rank with no sockets (FUN_1409d8650), season and pet
    /// skin flag; the align is 7 with flag 0x40 or 2, else 5.
    /// STILL-OPEN, so nothing is set for them: the housing set block (FUN_1409c8df0), and a ChatEmoticonAdd item's stat
    /// text (FUN_1409daff0) and extra comment (FUN_1409d7630 = FUN_1409daa30's emoticons).
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
        // FUN_1409c8df0, the housing set block: STILL-OPEN
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

    /// <summary>
    /// FUN_1409d5b70 (astooltip.cpp:2925), "itemGuideDescription" by id: the string table's guideDescription, then m_50
    /// (<see cref="ShopGuideText"/>). STILL-OPEN, so the member is left out: an install object's rights (objCode: 0xafa /
    /// 0xaf9) and an install item's housing level and trophy lines.
    /// </summary>
    void SetGuideDescriptionById(ItemData d)
    {
        if (d.InstallObjCode != 0 || d.IsInstallItem) return;
        string text = Tables.ItemDescription(d.Id, "guideDescription");
        if (!string.IsNullOrEmpty(ShopGuideText))
            text = string.IsNullOrEmpty(text) ? ShopGuideText : text + "\n" + ShopGuideText;
        Data["itemGuideDescription"] = text;
    }

    /// <summary>FUN_1409d6120 (astooltip.cpp:2984), "statDescription" by id: the string table's mainDescription as a
    /// { type 0, text } row when not empty; else a ChatEmoticonAdd item's FUN_1409daff0 text (STILL-OPEN, nothing set).</summary>
    void SetStatDescriptionById(ItemData d)
    {
        string main = Tables.ItemDescription(d.Id, "mainDescription");
        if (main.Length != 0) Data["statDescription"] = new Dictionary<string, object> { ["type"] = 0, ["text"] = main };
    }
}
