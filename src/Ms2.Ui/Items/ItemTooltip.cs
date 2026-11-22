using System.Globalization;

namespace Ms2.Ui.Items;

/// <summary>
/// The item tooltip ASTooltip builds (client/ms2/astooltip.cpp): ASTooltip(owner, flags, 2) and FUN_1409e0d40 (:4675),
/// which fills the export_common_tooltip_kindsItem members (the tooltip movie's TooltipKindsItem.setData reads them).
/// <see cref="Flags"/> is m_48: 0x100 another player's equipment, 0x200 the own one, 0x80 the bag, 0x40 / 2 a shop.
/// The builders not listed in <see cref="Fill"/> are not ported yet; their members are left out.
/// </summary>
public sealed partial class ItemTooltip
{
    public readonly AsTooltip Data = new();
    /// <summary>m_48.</summary>
    public readonly int Flags;
    readonly UiModel model;

    public ItemTooltip(UiModel model, int flags) { this.model = model; Flags = flags; }

    /// <summary>The tooltip's owner (m_40, the local player: EquipSlot::vfunc_0) by character id (PCBase::FUN_141656a80)
    /// and name, for a binding's owner name (CItemTransfer::GetBindingOwnerName); null: the binding's own name.</summary>
    public (long Id, string Name)? Owner;
    /// <summary>GFxManager::Localize: the movie's "$key" strings (korflash.xml); null leaves out the members that need it.</summary>
    public Func<string, string> Localize;
    /// <summary>The owner player's CEquipment by item uid (PC::GetEquipByUid) for FUN_1409cd750; null: the tooltip has no owner
    /// player (FUN_1409c6540() == 0) and that block is left out.</summary>
    public Func<long, bool> OwnerWearsUid;
    /// <summary>CUIService: the dialog of an id is open (not IsDialogClosed). The item break dialogs (0x4e8d, 0x4e8e) are
    /// not in this port, so by default none is open.</summary>
    public Func<int, bool> IsDialogOpen = _ => false;

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
        SetItemMovie(item);                                                       // FUN_1409c7cc0
        Data["ugctag"] = item.UgcTag;                                             // SetUgcTag
        SetItemStatus(item);                                                      // FUN_1409c7fc0
        SetItemStatusSet(item);                                                   // FUN_1409c8020
        SetItemSocket(item);
        Data["iconPath"] = item.IconPath;                                         // SetItemIconPath (FUN_14016a730: to wide text)
        SetSellPrice(item);                                                       // SetItemSellPrice
        SetNotExchange(item);
        SetItemCreateTime(item);
        SetItemUsePeriod(item);
        SetItemCompare(item);                                                     // FUN_1409cfbf0
        SetItemDescription(item);                                                 // FUN_1409d0710
        SetItemGuideDescription(item);                                            // FUN_1409d0a80
        SetStatDescription(item);                                                 // FUN_1409d1d30
        SetExtraDescription(item);                                                // FUN_1409d64a0
        SetItemDamage(item);                                                      // FUN_1409c7b10
        SetExtraComment(item);
        SetItemLockInfo(item);
        Data["itemPiece"] = d.IsPieceItem;                                        // SetItemPiece
        Data["itemPetSkin"] = d.GemSubtype == 0xb;                                // SetItemPetSkin(FUN_14053e900() == 0xb), the gem subtype
        if ((Flags & 0x80) != 0 || (Flags & 4) != 0) SetUseHints(item);              // FUN_1409cd750
        if ((Flags & (0x80 | 4 | 0x200 | 0x20 | 0x400 | 0x1000 | 0x100 | 2)) != 0)
            if (SetCustomizeColor(item)) SetCustomizeColorData(item);             // FUN_1409d2cb0, then FUN_1409d3010
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
    /// CItem::FUN_140527b10 (item.cpp:2264): the string table name (FUN_141811980(1, id)), then by the item's kind:
    /// a UGC block gives GetUgcItemName of its look; a score item its music title (GetMusicTitle); a pet item the pet
    /// block's name (or the table name) as common 0xcd6 s_word_battle_pet for a type 1 pet of CPetDataTable, 0xcd5
    /// s_word_pet for another one, unformatted without one; a gacha box or coin the name; a gem the name followed by its
    /// gem name when that is not empty. Then an unlimited enchant level formats s_word_item_unlimited_enchant(level,
    /// name), an enchant level "+%d %s". A made item (no record) has no UGC look, music block, pet name or gem.
    /// </summary>
    public string Name(Item item)
    {
        var d = item.Data;
        var rec = item.Record;
        string name = Tables.ItemName(d.Id);
        if (rec?.HasUgc == true && rec.Ugc != null) return UgcItemName(rec.Ugc, name);
        if (d.IsScoreItem) return MusicTitle(item, name);
        if (d.IsPetItem)
        {
            if (!string.IsNullOrEmpty(rec?.PetName)) name = rec.PetName;           // m_strB8
            return Tables.Pets.TypeOf((uint)d.PetId) switch
            {
                1 => Format(Tables.Common(0xcd6), name),                            // FormatStringId(0xcd6, name)
                int => Format(Tables.Common(0xcd5), name),
                null => name,
            };
        }
        if (d.OpenGachaBoxParam0 != 0 || Tables.GachaBoxes.IsCoinItem(d.Id)) return name;
        if (d.IsGemItem && GemName(item) is { Length: > 0 } gem) return name + gem;
        if (item.UnlimitedEnchantLevel > 0) return Format(Common("s_word_item_unlimited_enchant"), item.UnlimitedEnchantLevel, name);
        if (item.EnchantLevel > 0) return "+" + item.EnchantLevel.ToString(CultureInfo.InvariantCulture) + " " + name;
        return name;
    }

    /// <summary>
    /// GetUgcItemName (@0x141277a90, uihelpers): a blocked look (CUgcItemLook::FUN_140893520, state 2) gives common 0xf4
    /// s_item_ugc_name_blocked; a look with a local path (GetLocalPath, ugcdef.cpp:1508: m_n30 set and an url holding
    /// "item/" or "blueprint/") its own item name m_str28; else the fallback. STILL-OPEN: GetDownloadFilePath's shutdown
    /// test (CGameEventValueManager shutdown value 0xb for kinds 0xc9 / 0xd1 empties the path) is not ported, so such a
    /// look always has its path.
    /// </summary>
    public string UgcItemName(Ms2.Net.Packets.UgcLook look, string fallback)
    {
        if (look.State == 2) return Tables.Common(0xf4);
        bool localPath = look.Flag30 != 0 && (look.Url.Contains("item/", StringComparison.Ordinal) || look.Url.Contains("blueprint/", StringComparison.Ordinal));
        return localPath ? look.ItemName : fallback;
    }

    /// <summary>
    /// CItem::GetMusicTitle (@0x1405296c0, item.cpp:2393): a custom score with its music block (FUN_140525680, m_spMusic)
    /// gives the block's title when the block is not empty (CItemCustomMusicScore::IsEmpty: m_str20 empty), else the name
    /// OnItemInventory mode 7 set (m_strE0) when there is one; every other case the string table name.
    /// </summary>
    static string MusicTitle(Item item, string name)
    {
        if (item.Record is { HasMusic: true } rec)
        {
            if (!string.IsNullOrEmpty(rec.MusicWriter)) return rec.MusicTitle;
            if (!string.IsNullOrEmpty(rec.NotifiedName)) return rec.NotifiedName;
        }
        return name;
    }

    /// <summary>
    /// CItem::GetGemName (@0x140529de0) through CItemGem::FUN_1405a7d60: the gem's vfunc_11, which only CGemPetSkin
    /// (type 11) fills: string table 0x3b (petname) of its pet id (@0x1405a6d50); "" for any other gem or none.
    /// </summary>
    string GemName(Item item) => item.Record is { GemType: 11 } rec ? Tables.StringTableText(0x3b, rec.GemPetId) : "";

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
        string text = null;
        if (!(item.Record?.HasUgc == true || d.IsScoreItem)
            && !(d.IsMultiRideItem && model.IsFeatureEnabled(0xad))
            && !(d.Type == 0x1a || d.FunctionName == "InstallBillBoard" || d.InstallObjCode != 0 || d.IsInstallItem || d.Type == 0x19))
            text = Tables.ItemDescription(d.Id, "guideDescription");
        if (!string.IsNullOrEmpty(ShopGuideText))
            text = string.IsNullOrEmpty(text) ? ShopGuideText : text + "\n" + ShopGuideText;   // :2355-2361
        if (text != null) Data["itemGuideDescription"] = text;
    }

    /// <summary>ASTooltip m_50 (FUN_1409dbc30, astooltip.cpp:506): the text the shop tooltip appends to the guide
    /// description (the missing achievement requirement); set before filling.</summary>
    public string ShopGuideText;

    /// <summary>
    /// FUN_1409d1d30 (astooltip.cpp:2367), "statDescription": flag 0x800 picks the compact FUN_1409d26d0 (:2450), else the
    /// full FUN_1409d1d80 (:2379). Both start with the string table's mainDescription as a { type 0, text } row and stop
    /// there (every item has an enchant info: CItemStatus::CItemStatus, itemsocketsystemutil.cpp:1600); then a pet item
    /// (the full layout stops for any pet, the compact one after the size-up row of a type 1 pet); the full layout then
    /// stops for a ChatEmoticonAdd item (FUN_1409daff0) and a QuestStart item (CItemData::HasQuestStartFunction,
    /// itemdata.cpp:1093); both then take the constant-stat rows (<see cref="SetConstantStat"/>) and last a score item's
    /// text (FUN_1409da1c0).
    /// STILL-OPEN, so nothing is set for them: the pet size-up row (s_word_petlevel_sizeup with the pet level, CPetData
    /// m_nType is not in the port), the emoticon and quest-book text, the score text. A QuestStart item whose first
    /// argument is 0 (FUN_1408d1fd0) goes on in the client; it is not an equip item, so only the score text would differ.
    /// </summary>
    public void SetStatDescription(Item item)
    {
        var d = item.Data;
        string main = Tables.ItemDescription(d.Id, "mainDescription");
        if (main.Length > 0) { Data["statDescription"] = new Dictionary<string, object> { ["type"] = 0, ["text"] = main }; return; }
        if (d.IsPetItem) return;                                           // STILL-OPEN, see above
        if ((Flags & 0x800) == 0 && (d.FunctionName == "ChatEmoticonAdd" || d.FunctionName == "QuestStart")) return;
        if (SetConstantStat(item)) return;
        // d.IsScoreItem: FUN_1409da1c0, STILL-OPEN
    }

    /// <summary>
    /// ASTooltip::FUN_1409d4760 (astooltip.cpp:2759): the { type 2, title, extraup 0, min, max } row of the item's constant
    /// stat. A weapon (CItemData::FUN_14053d760, itemdata.cpp:999: equip subtype 0x1e..0x22 or 0x32..0x38) shows Min
    /// Attack's common text with min GetStatValue(0x1b, 3, 0) and max GetStatValue(0x1c, 3, 0). A gemstone (type 0xd) sets
    /// the option group 0's first special (GetSATypeName) and then its first stat (GetStatCommonText), the later one
    /// replacing the member (FUN_1409adbe0 is a plain set), and goes on. Then the stat of CItemData::FUN_14053f7c0
    /// (itemdata.cpp:1857, by equip subtype) shows GetStatValue(stat, 3, 0) when not 0. Each step answers true without a
    /// constantID (GetConstantId, itemdata.cpp:1627); a rate stat (IsStatKindTwo / FUN_14081dcd0) shows
    /// FormatRatePercent(value / 1000), else FormatInt.
    /// </summary>
    bool SetConstantStat(Item item)
    {
        var d = item.Data;
        var o = Tables.ItemOptions;
        void Row(string title, string min, string max) => Data["statDescription"] = new Dictionary<string, object>
            { ["type"] = 2, ["title"] = title, ["extraup"] = 0, ["min"] = min, ["max"] = max };
        string Value(bool rate, long v) => rate ? RatePercent(v / 1000.0f) : IntText((int)v);
        if (d.Type == 1 && d.EquipSubtype is >= 0x1e and <= 0x22 or >= 0x32 and <= 0x38)
        {
            if (d.ConstantId == 0) return true;
            Row(Tables.StatName(0x1b), IntText(StatusValue(item, 0x1b)), IntText(StatusValue(item, 0x1c)));
            return true;
        }
        if (d.IsGemstone)
        {
            if (d.ConstantId == 0) return true;
            var group = item.Record?.Options;
            if (group != null && FirstOption(group.Special[0]) is (int sp, int spv) && sp != 0)
            {
                string v = Value(o.IsSpecialRate(sp), spv);
                Row(Tables.SpecialAbilityName(sp), v, v);
            }
            if (group != null && FirstOption(group.Stat[0]) is (int st, int stv) && st != -1)
            {
                string v = Value(o.IsStatRate(st), stv);
                Row(Tables.StatName(st), v, v);
            }
        }
        int stat = d.EquipSubtype switch
        {
            0xc or 0x14 => 0x1a,
            0x12 or 0x13 => 0x19,
            0xd or 0xe or 0xf or 0x10 or 0x11 or 0x15 or 0x16 or 0x29 => 0x14,
            0x28 => 0x18,
            _ => -1,
        };
        if (stat == -1) return false;
        if (d.ConstantId == 0) return true;
        int value = StatusValue(item, stat);
        if (value != 0)
        {
            string v = Value(o.IsStatRate(stat), value);
            Row(Tables.StatName(stat), v, v);
        }
        return true;
    }

    /// <summary>FormatRatePercent (astooltip.cpp:4642): FormatStringId(0x98e, "{0:0.1f}" of rate * 100), the number with a
    /// % sign.</summary>
    static string RatePercent(float rate) => Ms2.Quest.ClientFormat.Format("{0:0.1f}", rate * 100.0f) + "%";

    /// <summary>CItemOption::GetFirstStat / GetFirstSpecialStat (itemoptiondata_1.cpp:114, :123): the entry at the
    /// CAtlMap's start position (<see cref="AtlMapOrder"/>), null for an empty group ((-1, 0) / (0, 0) in the client).</summary>
    static (int Key, int Value)? FirstOption(Dictionary<int, int> group) =>
        group.Count == 0 ? null : AtlMapOrder(group.Keys)[0] is var k ? (k, group[k]) : null;

    /// <summary>
    /// ASTooltip::FUN_1409d64a0 (astooltip.cpp:3014), "extraDescription": with the local player, { text, color } rows in
    /// this order: the item level (AddItemLevelRow), the level limit (AddLevelLimitRow), the name row (FUN_1409de140;
    /// a gemstone adds its equip-part row, FUN_1409df760), the enabled jobs (AddJobLimitRows), the VIP and wedding
    /// limits. The level comes from CItem::FUN_140529070 / FUN_140529090 (<see cref="ItemLevels.Of"/>).
    /// STILL-OPEN, so the member is left out: an install item (its housing category row, FUN_1409dea00, reads
    /// CItemDataTable::GetHousing, not ported), and a VIP or wedding item (PC::IsVip and WeddingManager::IsInProgress
    /// pick the row color; neither state is in the port).
    /// </summary>
    public void SetExtraDescription(Item item)
    {
        if (!model.HasLocalPlayer) return;                                    // FUN_1409c6540() == 0
        var d = item.Data;
        if (d.IsInstallItem || d.Vip || d.Wedding) return;
        var (level, additional) = ItemLevels.Of(item) ?? (0, 0);
        var rows = new List<object>();
        AddItemLevelRow(rows, d, item.Rarity, level, additional);
        AddLevelLimitRow(rows, d);
        AddNameRow(rows, d, item.Rarity);
        if (d.IsGemstone) AddGemPartRow(rows, d);                             // FUN_14053d840: FUN_1409df760
        AddJobLimitRows(rows, d);
        Data["extraDescription"] = rows;
    }

    static Dictionary<string, object> Row(string text, string color) => new() { ["text"] = text, ["color"] = color };

    /// <summary>AddItemLevelRow (astooltip.cpp:4311): equipment without a skin type; level 0 shows the rank's base level.</summary>
    void AddItemLevelRow(List<object> rows, ItemData d, int rarity, int level, int additional)
    {
        if (!d.IsEquip || d.HasSkinType) return;
        int nBase = level - additional, nAdd = additional;
        if (level <= 0) { nBase = ItemLevels.Base(d, rarity); nAdd = 0; }   // FUN_14053f0d0
        rows.Add(Row(nAdd > 0 ? CommonF("s_common_item_level_with_value_additional", nBase, nAdd)
            : CommonF("s_common_item_level_with_value", nBase), "aaaaaa"));
    }

    /// <summary>AddLevelLimitRow (astooltip.cpp:4341): "%s %d" of s_word_require_level (0x589) and the limit, red below it.
    /// Not for a type 0x13 item (FUN_14053c8b0) or limit 1.</summary>
    void AddLevelLimitRow(List<object> rows, ItemData d)
    {
        if (d.Type == 0x13 || d.LevelLimit == 1) return;
        string text = Common("s_word_require_level") + " " + d.LevelLimit.ToString(CultureInfo.InvariantCulture);
        rows.Add(Row(text, d.IsLevelAllowed(model.Player.Level) ? "aaaaaa" : "ff4040"));
    }

    /// <summary>
    /// FUN_1409de140 (astooltip.cpp:4189): the rank name (none at rank 0) with the item's kind name; with a rank, a
    /// suffix (FUN_1409df920: s_word_designed 0x58c for a cube texture mesh, s_word_skin 0x594 for a skin type) picks
    /// s_item_tooltip_icon_code_param3 over _param2. A cash item puts s_word_cash (0x593) in front ("{0} {1}").
    /// </summary>
    void AddNameRow(List<object> rows, ItemData d, int rank)
    {
        string code = rank == 0 ? "" : RankName(rank);
        string name = ItemNameText(d);
        string body;
        if (code.Length == 0) body = name;
        else
        {
            string suffix = d.HasCubeTextureMesh ? Common("s_word_designed") : d.HasSkinType ? Common("s_word_skin") : "";
            body = suffix.Length == 0 ? CommonF("s_item_tooltip_icon_code_param2", code, name)
                : CommonF("s_item_tooltip_icon_code_param3", code, suffix, name);
        }
        rows.Add(Row(d.IsCashItem ? Common("s_word_cash") + " " + body : body, "aaaaaa"));
    }

    /// <summary>CStringTable::GetRankName (stringtable.cpp:909): s_word_rank_%02d (rank 0: common 0x4f9; the name row
    /// never asks for it).</summary>
    string RankName(int rank) => Common("s_word_rank_" + rank.ToString("00", CultureInfo.InvariantCulture));

    /// <summary>GetItemNameText (unit_141274d10_uihelpers.cpp:1051): equipment by its slot name (string table 8), a
    /// lapenshard by its color (GetLapenshardName :1082: mask 1 red, 2 blue, 4 green), else the icon code's name.</summary>
    string ItemNameText(ItemData d)
    {
        if (d.IsEquip) return Tables.SlotName(d.EquipSubtype);
        if (d.IsLapenshard)
        {
            switch (Tables.Lapenshards.Mask(d.Id))
            {
                case 1: return Common("s_word_iconcode_lapenshard_red");
                case 2: return Common("s_word_iconcode_lapenshard_blue");
                case 4: return Common("s_word_iconcode_lapenshard_green");
            }
        }
        return IconCodeName(d.IconCode);
    }

    static readonly string[] IconCodeKeys =
    {
        "none", "weapon", "armor", "accessory", "active", "passive", "potion", "scroll", "action", "etc", "land", "building",
        "interior", "souvenir", "riding", "mannequin", "store", "electronics", "coupon", "action_skillbook", "gem", "storybook",
        "maid", "package", "random", "crystal", "airtaxi", "dungeonkey", "workbench_cook", "workbench_alchemy", "workbench_biz",
        "buffportion", "trigger_controller", "interior_pack", "pet", "fishing_rod", "music_instrument", "music_note", "petfood",
        "bait", "gemstone", "jeweldust", "coin", "quest", "glide_item", "petequip", "pettaminggoods", "petgoods",
        "interiorskill", "interiormessage", "lapenshard",
    };

    /// <summary>GetIconCodeName (unit_141274d10_uihelpers.cpp:980): s_word_iconcode_* for codes 0..0x32, 0x34 blueprint
    /// and 0x35 capsule (common 0x52c-0x55b); any other code gets common 0 (s_empty_string).</summary>
    public string IconCodeName(int code) => code switch
    {
        >= 0 and <= 0x32 => Common("s_word_iconcode_" + IconCodeKeys[code]),
        0x34 => Common("s_word_iconcode_blueprint"),
        0x35 => Common("s_word_iconcode_capsule"),
        _ => Common("s_empty_string"),
    };

    /// <summary>FUN_1409df760 (astooltip.cpp:4398): a gemstone with an upgrade row shows its equip parts
    /// (FUN_1406285d0, itemsocketsystemutil.cpp:566: weapon, armor, accessory, skin joined by ", "; mask 0xf and up "").</summary>
    void AddGemPartRow(List<object> rows, ItemData d)
    {
        var gem = Tables.GemstoneUpgrades.Find(d.Id);
        if (gem == null) return;
        rows.Add(Row(EquipPartsText(gem.Mask), "aaaaaa"));
    }

    string EquipPartsText(uint mask)
    {
        if (mask >= 0xf) return "";
        var parts = new List<string>();
        if ((mask & 1) != 0) parts.Add(IconCodeName(1));
        if ((mask & 2) != 0) parts.Add(IconCodeName(2));
        if ((mask & 4) != 0) parts.Add(IconCodeName(3));
        if ((mask & 8) != 0) parts.Add(Common("s_word_skin"));
        return string.Join(", ", parts);
    }

    /// <summary>
    /// AddJobLimitRows (astooltip.cpp:4276) with FUN_1409e68a0 (:5480): the jobLimit ids this server offers
    /// (FUN_1402226b0: 1 with feature 0xe7, 10-80, 90 / 100 / 110 with features 8 / 0x12 / 0x25) by job group name
    /// (string table 0x4d), as s_word_enable_jobs; gray when the player's job (a beginner counts as 10) or job 1 is
    /// listed, else red. The disableJobLimit row (FUN_1409e6aa0) tests the empty output text, so it never shows.
    /// </summary>
    void AddJobLimitRows(List<object> rows, ItemData d)
    {
        int job = model.Player.JobGroup == 1 ? 10 : model.Player.JobGroup;
        bool allowed = false;
        var names = new List<string>();
        foreach (int id in d.JobLimit)
        {
            if (!JobOffered(id)) continue;
            if (id == 1 || id == job) allowed = true;
            names.Add(Tables.JobGroupName(id));
        }
        if (names.Count == 0) return;
        rows.Add(Row(CommonF("s_word_enable_jobs", string.Join(", ", names)), allowed ? "aaaaaa" : "ff4040"));
    }

    /// <summary>FUN_1402226b0 (unit_1402226b0_featureenumhelpers.cpp:70).</summary>
    bool JobOffered(int id) => id switch
    {
        1 => model.IsFeatureEnabled(0xe7),
        10 or 20 or 30 or 40 or 50 or 60 or 70 or 80 => true,
        90 => model.IsFeatureEnabled(8),
        100 => model.IsFeatureEnabled(0x12),
        110 => model.IsFeatureEnabled(0x25),
        _ => false,
    };

    /// <summary>CItemData::IsCustomizable (itemdata.cpp:1286): a model whose color is on with a palette and one default color.</summary>
    bool IsCustomizable(ItemData d) =>
        d.ItemPreset != 0 && Tables.ItemModels.Get(d.ItemPreset) is { } m && m.ColorEnabled && m.Palette != 0 && !m.MultiColor;

    /// <summary>
    /// ASTooltip::FUN_1409d2cb0 (astooltip.cpp:2499), "customizeColor": with the local player, a customizable item's dye
    /// hint (s_msg_tooltip_customize_color, 0x9b7). With flag 4 and no default color the client first checks the
    /// player's equipment for the item (PC::GetEquipByUid, <see cref="OwnerWearsUid"/>): an item it does not wear puts
    /// 0x9b8 (s_msg_tooltip_customize_buy_random_color) and a new line above it. Without that lookup the member is left
    /// out there. Returns whether the member was set.
    /// </summary>
    public bool SetCustomizeColor(Item item)
    {
        if (!model.HasLocalPlayer) return false;
        var d = item.Data;
        if (!IsCustomizable(d)) return false;
        bool defaultColor = Tables.ItemModels.Get(d.ItemPreset).DefaultColor(0) >= 0;   // CItemData::FUN_14053e2b0(0)
        string text = Tables.Common(0x9b7);
        if ((Flags & 4) != 0 && !defaultColor)
        {
            if (OwnerWearsUid == null) return false;
            if (!OwnerWearsUid(item.Record?.Uid ?? 0)) text = Tables.Common(0x9b8) + "\n" + text;
        }
        Data["customizeColor"] = text;
        return true;
    }

    string CommonF(string key, params object[] args) => Ms2.Quest.ClientFormat.Format(Common(key), args);

    /// <summary>
    /// SetItemSellPrice (@0x1409cd9c0, astooltip.cpp:1772), the "itemSellPrice" part: a sellable item (shopSell, not
    /// locked) shows "s_word_sellprice : " and its price as meso text (FUN_1418fec00(1, price, true): s_common_payment
    /// with the grouped amount and the meso icon). Not ported: an install item's housing price, and an equipment item
    /// without a price from level 57 (its rank's Client::Constants price, the array at m_a020, whose names are not
    /// recovered); the member is left out there.
    /// </summary>
    public void SetSellPrice(Item item)
    {
        var d = item.Data;
        if (d.IsInstallItem) return;
        if (!(d.ShopSell && !IsLocked(item))) return;                                // CItemTransfer::IsSellable
        // CItem::GetSellPrice (item.cpp:1235) = CItemData::GetSellPrice(rarity) (itemdata.cpp)
        long price = d.PropertySellPrice(item.Rarity);
        if (d.IsEquipItem)
        {
            if (!d.HasSkinType && price == 0 && (ushort)d.LevelLimit >= 0x39) return;
            price = (long)(price * Tables.EquipmentSellRate(item.Rarity));            // float math as the client
        }
        Data["itemSellPrice"] = Common("s_word_sellprice") + " : " + MesoText(price);  // 0x587
    }

    /// <summary>FUN_1418fec90(1, amount, 0) (uicommon.cpp:913): s_common_payment with the grouped amount
    /// (FUN_1409404f0), the meso icon and its vspace (FUN_1418fe870: export_common_icon_meso_h21, -6).</summary>
    string MesoText(long amount) => CommonF("s_common_payment", NumberText.MesoText(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "export_common_icon_meso_h21", -6);

    /// <summary>
    /// SetItemSellPrice (@0x1409cd9c0, astooltip.cpp:1772), the "notExchange" part (flag 0x2000 clear): FUN_1409e6120's
    /// trade, bind and move lines (:5397), then FUN_1409d98a0's dismantle line (:3553), the socket transfer line
    /// (feature 0xa7 ItemSocketTransfer01, a skin item that cannot transfer its socket), the pet line (feature 0x8d) and the
    /// couple partner, joined with line breaks. The member is left out where an
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

    /// <summary>
    /// ASTooltip::FUN_1409d3470 (astooltip.cpp:2574), "extraComment", set only when the text is not empty. A score item
    /// (IsScoreItem) gets FUN_1409d9c50's text; else a gemstone (FUN_14053d840) with feature 0x2e that has a next tier in
    /// the upgrade table s_msg_tooltip_gemstone_enable_upgrade (0x9c3) and, with ingredients, a line of
    /// s_msg_tooltip_gemstone_enable_upgrade_ingredients listing their names; else a type 0x10 item its mastery recipes
    /// (<see cref="MasteryRecipeText"/>); else a ChatEmoticonAdd item FUN_1409daa30's emoticons.
    /// STILL-OPEN: FUN_1409d9c50 (the score item text) and FUN_1409daa30 (the emoticon images) are not ported; the member is
    /// left out for those items.
    /// </summary>
    public void SetExtraComment(Item item)
    {
        var d = item.Data;
        string text = "";
        if (d.IsScoreItem) { }                                                              // FUN_1409d9c50: STILL-OPEN
        else if (d.IsGemstone)
        {
            if (!model.IsFeatureEnabled(0x2e)) return;
            var row = Tables.GemstoneUpgrades.Find(d.Id);
            if (row != null && !Tables.GemstoneUpgrades.IsLastTier(row))
            {
                text = Common("s_msg_tooltip_gemstone_enable_upgrade");                     // 0x9c3
                if (row.Ingredients.Count != 0)
                {
                    string names = "";
                    foreach (int id in row.Ingredients)
                    {
                        if (names.Length != 0) names += ", ";
                        names += Tables.ItemName(id);
                    }
                    text += "\n" + CommonF("s_msg_tooltip_gemstone_enable_upgrade_ingredients", names);
                }
            }
        }
        else if (d.Type == 0x10) text = MasteryRecipeText(item);
        if (text.Length != 0) Data["extraComment"] = text;
    }

    /// <summary>Unk_Inventory::FUN_140a5f180(id, 0) (inventory.cpp:782) on g_143ab2a90: the amounts of the local player's bag
    /// items of the id (in the id's bag tab) the player can use (PC::CanUseItem); 0 with no player.</summary>
    public Func<int, int> OwnedCount = _ => 0;

    /// <summary>
    /// ASTooltip::FUN_1409da690 (astooltip.cpp:3699): empty when no recipe uses the item (CMasteryTable::IngredientReceipes).
    /// Else per (recipe id, count) pair with a recipe (Find; none is skipped), joined by line breaks:
    /// s_msg_tooltip_mastery_recipe_info of the type name (TypeName), the recipe name (string table 0x2f,
    /// stringMasteryReceipeName) and s_msg_tooltip_mastery_recipe_count (_lack when the owned count is below the count) of
    /// the owned count (<see cref="OwnedCount"/>) and the count.
    /// </summary>
    string MasteryRecipeText(Item item)
    {
        var table = Tables.MasteryRecipes;
        var list = table.IngredientRecipes(item.Data.Id);
        if (list == null) return "";
        string text = "";
        foreach (var (recipeId, need) in list)
        {
            var recipe = table.Find(recipeId);
            if (recipe == null) continue;
            int have = OwnedCount(item.Data.Id);
            string key = "s_msg_tooltip_mastery_recipe_count";
            if (have < need) key += "_lack";
            string type = MasteryRecipeTable.TypeKey(recipe.Type) is { } k ? Common(k) : "";
            string name = Tables.MasteryRecipeName(recipe.Id);
            string count = CommonF(key, have, need);
            if (text.Length != 0) text += "\n";
            text += CommonF("s_msg_tooltip_mastery_recipe_info", type, name, count);
        }
        return text;
    }

    /// <summary>
    /// ASTooltip::SetItemLockInfo (astooltip.cpp:2681): lock state 0 with an unlock time ahead of the server time shows
    /// the unlock icon and the time left in hours and minutes (at least one minute; $s_itemlock_tooltip_msg_unlock, or
    /// _unlock2 under an hour); state 1 shows the lock icon and $s_itemlock_tooltip_msg_lock; else neither icon.
    /// </summary>
    public void SetItemLockInfo(Item item)
    {
        long now = new DateTimeOffset(model.Now).ToUnixTimeSeconds();
        if (item.LockState == 0 && item.UnlockTime > now)
        {
            Data["itemIconLock"] = false;
            Data["itemIconUnLock"] = true;
            long remain = item.UnlockTime - now;
            int hours = (int)(remain / 3600);
            int minutes = Math.Max((int)(remain % 3600 / 60), 1);
            if (Localize == null) return;
            Data["itemLockInfo"] = hours > 0
                ? Ms2.Quest.ClientFormat.Format(Localize("$s_itemlock_tooltip_msg_unlock"), hours, minutes)
                : Ms2.Quest.ClientFormat.Format(Localize("$s_itemlock_tooltip_msg_unlock2"), minutes);
            return;
        }
        bool locked = item.LockState == 1;
        Data["itemIconLock"] = locked;
        Data["itemIconUnLock"] = false;
        if (locked && Localize != null) Data["itemLockInfo"] = Localize("$s_itemlock_tooltip_msg_lock");
    }

    /// <summary>
    /// ASTooltip::SetItemCreateTime (astooltip.cpp:1849), "itemCreateTime": for a cube texture mesh item or a UGC type
    /// item with a UGC look, s_ugc_upload_must_profile when the look has no UGC id, else s_item_edit_time with the
    /// author and the creation time in the server zone, and "designOrgItemName" the item's name. A custom music score's
    /// writer (s_msg_tooltip_musicscore_writer, by the music score table's custom note) is not ported.
    /// </summary>
    public void SetItemCreateTime(Item item)
    {
        var d = item.Data;
        if (!(d.HasCubeTextureMesh || d.IsUgcType)) return;
        var look = item.Record?.Ugc;
        if (look == null) return;
        if (look.Uid == 0)
        {
            Data["itemCreateTime"] = Common("s_ugc_upload_must_profile");                      // 0x46a
            return;
        }
        var t = Ms2.Net.MsTime.LocalTm(look.CreationTime);
        Data["itemCreateTime"] = CommonF("s_item_edit_time", look.Author, t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);   // 0xb7
        Data["designOrgItemName"] = Tables.ItemName(d.Id);                                    // CStringTable::FUN_141811980(1, id)
    }

    /// <summary>
    /// ASTooltip::SetItemUsePeriod (astooltip.cpp:1890), "itemUsePeriod": an item with an expiry time (CItem m_80) that
    /// is not a pet shows its end date in the server zone (s_item_end_time, the maid and PC cafe variants, each with an
    /// _expired form once the time has passed). Without one, a shop item's life (FUN_1409cf4d0) comes first; shop
    /// items are not ported, and an owned item has none. Then an item data period or a PC cafe item takes
    /// <see cref="SetDataPeriod"/>.
    /// </summary>
    public void SetItemUsePeriod(Item item)
    {
        var d = item.Data;
        if (item.ExpiryTime > 0)
        {
            if (d.IsPetItem) return;
            var t = Ms2.Net.MsTime.LocalTm(item.ExpiryTime);
            bool expired = item.ExpiryTime <= new DateTimeOffset(model.Now).ToUnixTimeSeconds();   // CItem::IsExpired (item.cpp:1303)
            string key = d.HasMaidId ? (expired ? "s_item_end_time_maid_expired" : "s_item_end_time_maid")             // 0xbb / 0xba
                : d.PCBang ? (expired ? "s_item_end_time_pcbang_expired" : "s_item_end_time_pcbang")                   // 0xbd / 0xbc
                : expired ? "s_item_end_time_expired" : "s_item_end_time";                                             // 0xb9 / 0xb8
            Data["itemUsePeriod"] = CommonF(key, t.Year, t.Month, t.Day, t.Hour, t.Minute);
            return;
        }
        if (d.HasPeriod || d.PCBang) SetDataPeriod(d, 0);
    }

    /// <summary>
    /// ASTooltip::FUN_1409d6a40 (astooltip.cpp:3070): a use period (the data's minutes, a PC cafe item's
    /// PCBangItemDefaultPeriod, or <paramref name="minutes"/>) as "{days}{ hours}{ minutes}" in s_item_end_time_tooltip
    /// (the PC cafe and maid variants); else the data's end date (s_item_end_time_tooltip_expiration), its weekly or
    /// monthly expiry (at least 1). An end set by a term tag (TermTagManagerCli::GetEndTime) is not ported; the member is
    /// left out there.
    /// </summary>
    void SetDataPeriod(ItemData d, long minutes)
    {
        if (d.UsePeriod > 0 || d.PCBang || minutes > 0)
        {
            long seconds = 0;
            if (d.UsePeriod > 0) seconds = d.UsePeriod * 60L;
            else if (d.PCBang) seconds = Tables.PCBangItemDefaultPeriod * 60L;
            if (minutes > 0) seconds = minutes * 60;
            long days = seconds / 86400, hours = seconds / 3600 % 24, mins = seconds / 60 % 60;   // ATL::CTimeSpan
            string dayText = "", hourText = "", minuteText = "";
            if (days > 0) dayText = CommonF("s_item_word_d", days);                                // 0xc5
            if (hours > 0)
            {
                hourText = CommonF("s_item_word_h", hours);                                        // 0xc6
                if (days > 0) hourText = " " + hourText;
            }
            if (mins > 0)
            {
                minuteText = CommonF("s_item_word_m", mins);                                       // 199
                if (days > 0 || hours > 0) minuteText = " " + minuteText;
            }
            string period = dayText + hourText + minuteText;
            Data["itemUsePeriod"] = d.PCBang ? CommonF("s_item_end_time_tooltip_pcbang", period)   // 0xc0
                : d.HasMaidId ? CommonF("s_item_end_time_tooltip_maid", period)                    // 0xc1
                : CommonF("s_item_end_time_tooltip", period);                                      // 0xbe
            return;
        }
        if (d.ExpirationTime > 0)
        {
            var t = Ms2.Net.MsTime.LocalTm(d.ExpirationTime);
            Data["itemUsePeriod"] = CommonF("s_item_end_time_tooltip_expiration",                  // 0xbf
                CommonF("s_item_end_time", t.Year, t.Month, t.Day, t.Hour, t.Minute));            // 0xb8
        }
        else if (d.ExpirationType == 1)
            Data["itemUsePeriod"] = CommonF("s_item_end_time_tooltip_expirationtype_weekly", Math.Max(d.NumberOfWeeksMonths, 1u));
        else if (d.ExpirationType == 2)
            Data["itemUsePeriod"] = CommonF("s_item_end_time_tooltip_expirationtype_monthly", Math.Max(d.NumberOfWeeksMonths, 1u));
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

    /// <summary>
    /// FUN_1409cd750 (astooltip.cpp:1736): with an owner player, an item it wears gets itemEquip s_word_equiped (0x58a);
    /// else, with flag 0x80, an equipment item while neither item break dialog is open (FUN_1409e6c80) gets the default
    /// enablePuton (0x751) and, for subtype 0x1f / 0x22 (FUN_14053d510), enableCompareSwap (0x765); a usable item
    /// (CItem::FUN_140525a80) gets enableUseDbClick: a StoryBook s_msg_dclick_readable (0x763), a score item the default
    /// only when FUN_140525730 holds (not ported: the score items get none), a shop item s_msg_dclick_open_shop, else the
    /// default s_msg_dclick_usable (0x762). Puton and compare swap are skipped with flag 0x100.
    /// </summary>
    public void SetUseHints(Item item)
    {
        if (OwnerWearsUid == null) return;
        var d = item.Data;
        if (OwnerWearsUid(item.Uid)) { Data["itemEquip"] = Common("s_word_equiped"); return; }
        if ((Flags & 0x80) == 0) return;
        if (d.Type == 1 && !IsDialogOpen(0x4e8d) && !IsDialogOpen(0x4e8e))
        {
            SetEnablePuton(Common("s_msg_rclick_puton_equip"));                      // SetEnablePutonDefault
            if (d.EquipSubtype is 0x1f or 0x22 && (Flags & 0x100) == 0)
                Data["enableCompareSwap"] = Common("s_msg_tooltip_compare_swap");     // SetEnableCompareSwapDefault
        }
        if (!IsDoubleClickUsable(item)) return;
        if (d.FunctionName == "StoryBook") Data["enableUseDbClick"] = Common("s_msg_dclick_readable");
        else if (d.IsScoreItem) { }
        else if (d.ShopId > 0) Data["enableUseDbClick"] = Common("s_msg_dclick_open_shop");
        else Data["enableUseDbClick"] = Common("s_msg_dclick_usable");
    }

    /// <summary>CItem::FUN_140525a80 (item.cpp:1761): an equipment item with a cube texture mesh and no UGC id; types 2, 5,
    /// 9, 0xb, 0xc and 0x18; a function other than RecallParty and TreasureMap; or a shop id.</summary>
    static bool IsDoubleClickUsable(Item item)
    {
        var d = item.Data;
        switch (d.Type)
        {
            case 1:
                if (d.HasCubeTextureMesh && !(item.Record is { HasUgc: true, Ugc.Uid: not 0 })) return true;
                break;
            case 2: case 5: case 9: case 0xb: case 0xc: case 0x18:
                return true;
        }
        if (d.FunctionName.Length != 0) return d.FunctionName is not ("RecallParty" or "TreasureMap");
        return d.ShopId > 0;
    }

    /// <summary>
    /// SetEnablePreview (astooltip.cpp:3829): for an item FUN_141b48950 lets on the character (not expired, CItem::IsExpired
    /// item.cpp:1303, and IsPreviewable: no doNotPreview, uiugcitemeditdialog.cpp:2326), "enablePreview" is
    /// s_msg_tooltip_enable_preview with the key of action 0x49406 (FUN_1409e7880: CKeyTable::GetActionKeyName(0x49406, 0)).
    /// </summary>
    public void SetEnablePreview(Item item)
    {
        if (item?.Data == null) return;
        if (item.ExpiryTime > 0 && item.ExpiryTime <= new DateTimeOffset(model.Now).ToUnixTimeSeconds()) return;
        if (item.Data.DoNotPreview != 0) return;
        var keys = model.Keys;
        Data["enablePreview"] = CommonF("s_msg_tooltip_enable_preview", keys.GetKeyName(keys.FindKeyByActionAndPriority(0x49406, 0)));
    }

    /// <summary>The compare tooltip's sides, set only when a side filled (its member is there).</summary>
    ItemTooltip lhs, rhs;

    /// <summary>A side tooltip: ASTooltip(the owner, flags, 2) (FUN_1409e13f0 passes m_40 on).</summary>
    ItemTooltip Side() => new(model, Flags) { Owner = Owner, Localize = Localize, OwnerWearsUid = OwnerWearsUid, IsDialogOpen = IsDialogOpen,
        OwnerEquipment = OwnerEquipment, OwnerGender = OwnerGender, ShiftHeld = ShiftHeld };

    /// <summary>
    /// FUN_1409e13a0 (:315) with this tooltip's flags for both sides, i.e. FUN_1409e13f0 (:4748): false without either
    /// item. With both, the class export_common_tooltip_tooltipCompare and FUN_1409e15a0: "lhsTooltip" the compared item's
    /// and "rhsTooltip" the item's tooltip, each set when it fills. With one, this tooltip filled with it (the flags stay).
    /// </summary>
    public bool FillCompare(Item compare, Item item)
    {
        if (item == null && compare == null) return false;
        if (compare != null && item != null)
        {
            Data["tooltip"] = "export_common_tooltip_tooltipCompare";             // SetTooltipClass
            var l = Side(); var r = Side();
            r.ShopGuideText = ShopGuideText;   // the shop text rides the item side (unit_141347b10.cpp:450-452)
            if (l.Fill(compare)) { Data["lhsTooltip"] = l.Data; lhs = l; }
            if (r.Fill(item)) { Data["rhsTooltip"] = r.Data; rhs = r; }
        }
        else Fill(compare ?? item);
        return true;
    }

    /// <summary>SetEnablePutonBoth (@0x1409c6a20): nothing with flag 0x100; else "enablePuton" on each side, this tooltip when
    /// that side is not there.</summary>
    public void SetEnablePutonBoth(string lhsText, string rhsText)
    {
        if ((Flags & 0x100) != 0) return;
        (lhs?.Data ?? Data)["enablePuton"] = lhsText;
        (rhs?.Data ?? Data)["enablePuton"] = rhsText;
    }

    /// <summary>SetEnablePreviewBoth (@0x1409db930): SetEnablePreview of the compared item on the left side and of the item
    /// on the right side, each on this tooltip when that side is not there.</summary>
    public void SetEnablePreviewBoth(Item compare, Item item)
    {
        if (compare != null) (lhs ?? this).SetEnablePreview(compare);
        if (item != null) (rhs ?? this).SetEnablePreview(item);
    }

    /// <summary>SetEnableUseDbClick (@0x1409c65f0, astooltip.cpp:116): "enableUseDbClick".</summary>
    public void SetEnableUseDbClick(string text) => Data["enableUseDbClick"] = text;

    /// <summary>SetEnablePuton (@0x1409c69b0): nothing with flag 0x100; else "enablePuton".</summary>
    public void SetEnablePuton(string text)
    {
        if ((Flags & 0x100) != 0) return;
        Data["enablePuton"] = text;
    }
}
