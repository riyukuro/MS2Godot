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

    /// <summary>The tooltip's owner (m_40, the local player: EquipSlot::vfunc_0) by character id (PCBase::FUN_141656a80)
    /// and name, for a binding's owner name (CItemTransfer::GetBindingOwnerName); null: the binding's own name.</summary>
    public (long Id, string Name)? Owner;

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
        SetNotExchange(item);                                                     // SetItemSellPrice
        SetItemDescription(item);                                                 // FUN_1409d0710
        SetItemGuideDescription(item);                                            // FUN_1409d0a80
        Data["itemPiece"] = d.IsPieceItem;                                        // SetItemPiece
        Data["itemPetSkin"] = d.GemSubtype == 0xb;                                // SetItemPetSkin(FUN_14053e900() == 0xb), the gem subtype
        if ((Flags & (0x80 | 4 | 0x200 | 0x20 | 0x400 | 0x1000 | 0x100 | 2)) != 0)
            SetCustomizeColor(item);                                              // FUN_1409d2cb0; FUN_1409d3010 after it is not ported
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

    /// <summary>CItemData::IsCustomizable (itemdata.cpp:1286): a model whose color is on with a palette and one default color.</summary>
    bool IsCustomizable(ItemData d) =>
        d.ItemPreset != 0 && Tables.ItemModels.Get(d.ItemPreset) is { } m && m.ColorEnabled && m.Palette != 0 && !m.MultiColor;

    /// <summary>
    /// ASTooltip::FUN_1409d2cb0 (astooltip.cpp:2499), "customizeColor": with the local player, a customizable item's dye
    /// hint (s_msg_tooltip_customize_color, 0x9b7). With flag 4 and no default color the client first checks the
    /// player's equipment for the item (unequipped: s_msg_tooltip_customize_buy_random_color, 0x9b8, above it); that
    /// lookup is not ported, so the member is left out there. Returns whether the member was set.
    /// </summary>
    public bool SetCustomizeColor(Item item)
    {
        if (!model.HasLocalPlayer) return false;
        var d = item.Data;
        if (!IsCustomizable(d)) return false;
        bool defaultColor = Tables.ItemModels.Get(d.ItemPreset).DefaultColor(0) >= 0;   // CItemData::FUN_14053e2b0(0)
        if ((Flags & 4) != 0 && !defaultColor) return false;
        Data["customizeColor"] = Common("s_msg_tooltip_customize_color");
        return true;
    }

    string CommonF(string key, params object[] args) => Ms2.Quest.ClientFormat.Format(Common(key), args);

    /// <summary>
    /// SetItemSellPrice (@0x1409cd9c0, astooltip.cpp:1772), the "notExchange" part (flag 0x2000 clear): FUN_1409e6120's
    /// trade, bind and move lines (:5397), then FUN_1409d98a0's dismantle line (:3553), the socket transfer line
    /// (feature 0xa7 ItemSocketTransfer01, a skin item that cannot transfer its socket), the pet line (feature 0x8d) and the
    /// couple partner, joined with line breaks. The "itemSellPrice" part is not ported. The member is left out where an
    /// input is not ported: no record, a UGC block, a socket with a gem (the gem checks), a gacha dismantle id or a pet
    /// item (the dismantle check), a move-disabled item's stored character and a skin item's dismantle reward (CItemBreakManager::HasBreakReward's ingredient table and Lua).
    /// </summary>
    public void SetNotExchange(Item item)
    {
        var d = item.Data;
        var r = item.Record;
        if (r == null || r.HasUgc || HasGem(r) || d.IsPetItem) return;
        var t = r.Transfer;
        bool locked = IsLocked(item);
        // CanTradeItem (FUN_140633e40): the transfer's FUN_140635590, the gems (none here), the enchant's IsEnabled
        bool tradeable = TradeRule(t, locked) != 0 && r.Enchant.HistoryEnabled;
        // CItemTransfer::IsSellable: shopSell (InitFromItem's m_b39) and not locked (FUN_140635870)
        bool personal = d.ShopSell && !locked;
        bool bindable = (t.Rule & 8) != 0, bindOnGet = bindable && t.HasBinding;
        string bindName = bindOnGet ? (Owner is { } o && o.Id == t.BindingId ? o.Name : t.BindingName) : "";
        bool countLeft = !t.HasBinding && (t.Rule & 0x10) != 0 && t.Count != 0;   // HasTradeCountLeft (FUN_140634cc0 is 1)
        int count = countLeft ? t.Count : 0;
        // FUN_1409dfae0: locked, the gems pass, a pet or rarity 4+, a count
        bool remainOnly = locked && (d.IsPetItem || item.Rarity >= 4) && t.Count > 0;
        string text = "";
        if (remainOnly) text = CommonF("s_word_item_limit_trade_count_without_sell_for_itemlock", count);   // 0x525
        else if (tradeable && count > 0)
            text = personal ? CommonF("s_word_item_limit_trade_count", count) : CommonF("s_word_item_limit_trade_count_without_sell", count);   // 0x523 / 0x524
        else if (!personal && !tradeable) text = Common("s_word_item_limit_sell_trade");   // 0x520
        else if (!personal) text = Common("s_word_item_limit_sell");                       // 0x51e
        else if (!tradeable) text = Common("s_word_item_limit_trade");                     // 0x51f
        if (bindable)
        {
            if (bindOnGet) text = Join(text, CommonF("s_word_item_limit_bind_format", bindName));   // 0x527
            else
            {
                string bind = Common("s_word_item_limit_bind");                                     // 0x526
                switch (d.TransferType)
                {
                    case 2: bind = Common("s_word_item_limit_bind_get"); break;
                    case 3: case 4:
                        bind = Common(d.IsPetItem ? "s_word_item_limit_bind_pet" : d.IsEquipItem ? "s_word_item_limit_bind_equip" : "s_word_item_limit_bind_use");
                        break;
                }
                text = Join(text, bind);
            }
        }
        if (model.IsFeatureEnabled(0xcf) && d.MoveDisable)
        {
            if (r.BindingId != 0) return;                                         // s_word_item_limit_moveDisable_store_in: the stored character
            text = Join(text, Common("s_word_item_limit_moveDisable"));
        }
        // FUN_1409d98a0: feature 0x3a, a skin item that CItemBreakManager::CanBreakItem refuses
        if (model.IsFeatureEnabled(0x3a) && d.HasSkinType)
        {
            if (CanBreak(item, locked) is not { } canBreak) return;
            if (!canBreak) text = Join(text, Common("s_invalid_break_skinitem"));   // 0xc44
        }
        if (model.IsFeatureEnabled(0xa7) && d.HasSkinType)
        {
            // CanTransferSocket: m_b42 (read) and m_b41 (InitFromItem: CItemSocketTable::FUN_14062d980 with the rarity);
            // the line is appended after a line break even to an empty text
            if (!(t.SocketTransferEnabled && Tables.ItemSockets.SocketCanGrow(d, item.Rarity, model.IsFeatureEnabled)))
                text += "\n" + Common("s_word_item_limit_socket_transfer");
        }
        if (!string.IsNullOrEmpty(r.CoupleName)) text = Join(text, CommonF("s_couple_effect_partner", r.CoupleName));
        Data["notExchange"] = text;
    }

    static string Join(string text, string line) => text.Length == 0 ? line : text + "\n" + line;

    static bool HasGem(Ms2.Net.Packets.EquipItem r) => r.Socket.MaxSlot != 0 && r.Socket.Gems.Any(g => g != null && g.GemId != 0);

    /// <summary>CItem::FUN_140528e20 (item.cpp): lock state 1, or 0 with an unlock time still ahead of the server time.</summary>
    bool IsLocked(Item item) => item.LockState == 1 || (item.LockState == 0 && new DateTimeOffset(model.Now).ToUnixTimeSeconds() < item.UnlockTime);

    /// <summary>CItemTransfer::FUN_140635590: 0 when bound or locked (FUN_140634cc0 is always 1: the enchant's FUN_14058c8a0
    /// returns 0); else the trade count rule (0x10) with a count left, else the trade rule bit 4.</summary>
    static int TradeRule(Ms2.Net.Packets.ItemTransfer t, bool locked)
    {
        if (t.HasBinding || locked) return 0;
        int result = (t.Rule & 0x10) != 0 && t.Count > 0 ? 1 : 0;
        return result == 0 ? t.Rule & 4 : result;
    }

    /// <summary>
    /// CItemBreakManager::CanBreakItem (itembreakmanager.cpp:276) for an item without gems; null where it needs what is not
    /// ported: a gacha dismantle id (FUN_140531510: the gacha box and the content shutdown flag 0x71), the summoned pet
    /// (FUN_140531630, a pet item) and a skin item's reward (HasBreakReward).
    /// </summary>
    bool? CanBreak(Item item, bool locked)
    {
        var d = item.Data;
        var r = item.Record;
        if (locked) return false;
        if (r.GachaDismantleId != 0 || d.IsPetItem) return null;
        // FUN_140633bf0: CItemTransfer::CanBreak (m_b3F, InitFromItem's m_b3E = enableBreak 0, a PC bang item, locked,
        // a skin item with feature 0x3a off or a cube mesh with no socket slots), then the gems (none here)
        if (r.Transfer.BreakDisabled || d.EnableBreak == 0 || d.PCBang) return false;
        if (d.HasSkinType && !model.IsFeatureEnabled(0x3a)) return false;
        if (d.HasSkinType && d.HasCubeTextureMesh && r.Socket.Gems.Count == 0) return false;
        if (d.HasSkinType) return null;                                            // HasBreakReward
        return true;
    }

    /// <summary>SetEnablePuton (@0x1409c69b0): nothing with flag 0x100; else "enablePuton".</summary>
    public void SetEnablePuton(string text)
    {
        if ((Flags & 0x100) != 0) return;
        Data["enablePuton"] = text;
    }
}
