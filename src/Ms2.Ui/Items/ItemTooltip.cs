using System.Globalization;

namespace Ms2.Ui.Items;

/// <summary>
/// The item tooltip ASTooltip builds (client/ms2/astooltip.cpp): ASTooltip(owner, flags, 2) and FUN_1409e0d40 (:4675),
/// which fills the export_common_tooltip_kindsItem members (the tooltip movie's TooltipKindsItem.setData reads them).
/// <see cref="Flags"/> is m_48: 0x100 another player's equipment, 0x200 the own one, 0x80 the bag, 0x40 / 2 a shop.
/// The builders not listed in <see cref="Fill"/> are not ported yet; their members are left out.
/// </summary>
public sealed class ItemTooltip
{
    public readonly AsTooltip Data = new();
    /// <summary>m_48.</summary>
    public readonly int Flags;
    readonly UiModel model;

    public ItemTooltip(UiModel model, int flags) { this.model = model; Flags = flags; }

    GameTables Tables => model.Tables;
    string Common(string key) => Tables.Common(key) ?? "";

    static string Format(string f, params object[] args)
    {
        for (int i = 0; i < args.Length; i++) f = f.Replace("{" + i + "}", Convert.ToString(args[i], CultureInfo.InvariantCulture));
        return f;
    }

    /// <summary>
    /// FUN_1409e0d40 (:4675): 0 without an item or its data; else the class, the flags, name, grade, kind, ugc tag, icon
    /// and the align (9 with flag 0x40 or 2, else 5). Ported so far: the pet rank (feature 0x8c) or pet flag, the cash /
    /// skin / PC bang / VIP / wedding flags, the name, grade, kind, ugc tag, icon path, description, guide description,
    /// piece flag and pet skin flag.
    /// </summary>
    public bool Fill(Item item)
    {
        if (item?.Data == null) return false;
        var d = item.Data;
        Data["tooltip"] = "export_common_tooltip_kindsItem";                        // SetTooltipClass
        if (model.IsFeatureEnabled(0x8c))
        {
            // SetItemPetRankAndDisableShadow (:4220)
            if (d.IsPetItem) { Data["itemPetRank"] = item.Rarity; Data["dropShadowDisable"] = 1; }
        }
        else Data["itemPet"] = d.IsPetItem;                                       // SetItemPetFlag
        Data["itemCash"] = d.IsCashItem;                                          // SetItemCashFlag
        Data["itemSkin"] = d.HasSkinType;                                         // SetItemSkinFlag
        Data["itemPcBang"] = d.PCBang ? 1 : 0;                                    // SetItemPcBangFlag (an int member)
        Data["itemVIP"] = d.Vip;                                                  // SetItemVIPFlag
        Data["itemWedding"] = d.Wedding;                                          // SetItemWeddingFlag (HasWeddingLimit)
        if (Name(item) is { } name) Data["itemName"] = name;                      // SetItemName
        Data["itemGrade"] = item.Rarity;                                          // SetItemGrade
        Data["itemKinds"] = Kinds(d.IconCode);                                    // FUN_1409d5610
        Data["ugctag"] = item.UgcTag;                                             // SetUgcTag
        Data["iconPath"] = item.IconPath;                                         // SetItemIconPath (FUN_14016a730: to wide text)
        SetItemDescription(item);                                                 // FUN_1409d0710
        SetItemGuideDescription(item);                                            // FUN_1409d0a80
        Data["itemPiece"] = d.IsPieceItem;                                        // SetItemPiece
        Data["itemPetSkin"] = d.GemSubtype == 0xb;                                // SetItemPetSkin(FUN_14053e900() == 0xb), the gem subtype
        Data.SetTooltipAlign((Flags & 0x40) != 0 || (Flags & 2) != 0 ? 9 : 5);
        return true;
    }

    /// <summary>FUN_1409e6fc0 (:5571): the icon code as the movie's tooltip_item_type_N.</summary>
    public static int Kinds(int code) => code switch
    {
        0x28 or 0x29 => 0x19,
        0x2b => 9,
        0x2c => 0xe,
        0x2d => 0x26,
        0x2e or 0x2f => 0x22,
        0x30 or 0x31 => 0xc,
        0x32 => 0x32,
        0x35 => 0x17,
        _ => code,
    };

    /// <summary>
    /// CItem::FUN_140527b10 (item.cpp:2264): the string table name; a gacha box or coin keeps it; an unlimited enchant
    /// level formats s_word_item_unlimited_enchant(level, name); an enchant level "+%d %s". Null where the name needs
    /// what is not ported: a UGC look (GetUgcItemName), a score's music title, a pet's name and pet table, a gem's name.
    /// </summary>
    public string Name(Item item)
    {
        var d = item.Data;
        string name = Tables.ItemName(d.Id);
        if (item.Record?.HasUgc == true || d.IsScoreItem || d.IsPetItem) return null;
        if (d.OpenGachaBoxParam0 != 0 || Tables.GachaBoxes.IsCoinItem(d.Id)) return name;
        if (d.IsGemItem) return null;
        if (item.UnlimitedEnchantLevel > 0) return Format(Common("s_word_item_unlimited_enchant"), item.UnlimitedEnchantLevel, name);
        if (item.EnchantLevel > 0) return "+" + item.EnchantLevel.ToString(CultureInfo.InvariantCulture) + " " + name;
        return name;
    }

    /// <summary>
    /// FUN_1409d0710 (astooltip.cpp:2131), "itemDescription": the string table's tooltipDescription with its value words
    /// replaced (ReplaceValueWords: the local player's name; the gender words read no gender here, none of the item texts
    /// use them). Not ported, so the member is left out: an item with a UGC block (the UGC text 0xf3 or "" by
    /// CItemUgc::FUN_1406321a0 and the look path, for a UGC type or cube mesh item) and a score item (a custom score's
    /// BlockFormat(0x9be, mastery max)); both need what the port does not read.
    /// </summary>
    public void SetItemDescription(Item item)
    {
        var d = item.Data;
        if (item.Record?.HasUgc == true || d.IsScoreItem) return;
        string text = Tables.ItemDescription(d.Id, "tooltipDescription");
        Data["itemDescription"] = Tables.ReservedWords.ReplaceValueWords(text, model.HasLocalPlayer ? model.Player.Name : null);
    }

    /// <summary>
    /// FUN_1409d0a80 (astooltip.cpp:2178), "itemGuideDescription": the string table's guideDescription, then m_50 (set
    /// only by the shop and merat market tooltips, FUN_1409dbc30). Ported: the plain text. Not ported, so the member is left
    /// out: a UGC block, a score, a multi-ride item with feature 0xad (s_multi_riding_item_desc and the UGC counts
    /// FUN_141901d20 / f70), a gacha box (type 0x1a, its item list), the InstallBillBoard limits, an install object's
    /// rights (objCode: 0xafa / 0xaf9), an install item's housing limits and trophy, and a type 0x19 item (the expiry
    /// notice by FUN_1409e0450).
    /// </summary>
    public void SetItemGuideDescription(Item item)
    {
        var d = item.Data;
        if (item.Record?.HasUgc == true || d.IsScoreItem) return;
        if (d.IsMultiRideItem && model.IsFeatureEnabled(0xad)) return;
        if (d.Type == 0x1a || d.FunctionName == "InstallBillBoard" || d.InstallObjCode != 0 || d.IsInstallItem || d.Type == 0x19) return;
        Data["itemGuideDescription"] = Tables.ItemDescription(d.Id, "guideDescription");
    }

    /// <summary>SetEnablePuton (@0x1409c69b0): nothing with flag 0x100; else "enablePuton".</summary>
    public void SetEnablePuton(string text)
    {
        if ((Flags & 0x100) != 0) return;
        Data["enablePuton"] = text;
    }
}
