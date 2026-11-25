using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The use handlers of RegisterItemUseFuncs (unit_140d1ae30_itemfunctiondispatch.cpp:373) that check, ask or open in place
/// of the plain use request: <see cref="OwnUse"/> runs the ported ones for UseItem. The confirm boxes carry the tag
/// "use_function_item" (s_strUseFunctionItem) whose yes is <see cref="ConfirmUseFunctionItem"/>; nothing is used up here.
/// </summary>
public sealed partial class InventoryController
{
    /// <summary>The UI ids the handlers test (CUIService::IsDialogOpen): the enchant scroll window and the remake, socket
    /// and repacking scroll windows.</summary>
    public const int EnchantScrollDialogId = 0x4ee0, RemakeScrollDialogId = 0x4f09, SocketScrollDialogId = 0x4f10,
        RePackingScrollDialogId = 0x4f11;

    /// <summary>The common strings of the handlers, by key.</summary>
    public const string RemakeScrollOpenKey = "s_itemremake_scroll_already_open", SocketScrollOpenKey = "s_itemsocket_scroll_already_open",
        RePackingScrollOpenKey = "s_item_repacking_scroll_already_open",
        WithKeyNoKeyKey = "s_msg_item_open_item_box_with_key_no_key_count", WithKeyKey = "s_msg_item_open_item_box_with_key",
        TutorialLevelUpKey = "s_tutorial_levelup_item_not_use", TargetLevelKey = "s_err_cannot_already_target_level_potion",
        GuildNameKey = "s_guild_ui_change_name";

    /// <summary>s_strChangeGuildName: the guild name box's tag; its yes is UIMessageBox::HandleChangeGuildNameButton.</summary>
    public const string ChangeGuildNameTag = "changeGuildName";

    /// <summary>WorldService::GetFieldData()->HasTutorialType(): the field is a tutorial one (LevelPotion refuses there).</summary>
    public Func<bool> FieldIsTutorial = () => false;
    /// <summary>DAT_143ab2914, the local player's GM level: from 0x32 the limited quantity box has no cooldown.</summary>
    public Func<int> GmLevel = () => 0;
    /// <summary>CQuestManagerCommon::IsQuestRewarded.</summary>
    public Func<int, bool> IsQuestRewarded = _ => false;
    /// <summary>CGuildManager::IsMaster of the local player's name (vfunc_77).</summary>
    public Func<bool> IsGuildMaster = () => false;
    /// <summary>GetTickCountMs2: the client's millisecond tick.</summary>
    public Func<uint> TickCount = () => (uint)Environment.TickCount;

    /// <summary>s_itemUseMap (@0x1400d1fc0): item id to the tick of its last limited quantity use; one map for the client.</summary>
    static readonly Dictionary<int, uint> lastLimitedUse = new();

    /// <summary>Empties s_itemUseMap (tests: the map outlives a controller, as the client's does).</summary>
    internal static void ClearLimitedUses() => lastLimitedUse.Clear();

    /// <summary>
    /// The ported handlers of RegisterItemUseFuncs by the item's function name: their result (UseItem returns it), or null
    /// for a name whose handler is not ported here (UseItem then raises it).
    /// </summary>
    internal bool? OwnUse(Item item)
    {
        switch (item.Data.FunctionName)
        {
            case "ResetAbilityCoolTime":
                return ResetAbilityCoolTime(item);
            case "ChangeGuildName":
                return ChangeGuildName(item);
            case "ChangeCharName":
                return ChangeCharName(item);
            case "TreasureMap":         // RecallParty's handler @0x140d1df70: returns 1, nothing done
                return true;
            case "EnchantScroll":       // @0x140d225d0
                if (IsDialogOpen(EnchantScrollDialogId)) { ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(0xb68), null, "")); return false; }
                return FunctionUse(item, "");
            case "ItemRemakeScroll":    // @0x140d221c0
                return ScrollUse(item, RemakeScrollDialogId, RemakeScrollOpenKey);
            case "ItemRePackingScroll": // @0x140d22090
                return ScrollUse(item, RePackingScrollDialogId, RePackingScrollOpenKey);
            case "ItemSocketScroll":    // @0x140d21c30: 1 after the use, whatever it returned
                if (IsDialogOpen(SocketScrollDialogId)) { ShowCommonBox(SocketScrollOpenKey); return false; }
                FunctionUse(item, "");
                return true;
            case "ExpandInven":
                return ExpandInven(item);
            case "OpenItemBoxWithKey":
                return OpenItemBoxWithKey(item);
            case "LevelPotion":
                return LevelPotion(item);
            case "OpenItemBoxLimitedQuantity":
                return OpenItemBoxLimitedQuantity(item);
            default:
                return null;
        }
    }

    /// <summary>Unk_Inventory::FUN_140a63880 (inventory.cpp:1403): an item with a function is used with the argument
    /// (<see cref="RequestUseItem"/>); else 0.</summary>
    bool FunctionUse(Item item, string argument) => item.Data.HasFunction && RequestUseItem(item, argument ?? "");

    /// <summary>The remake, repacking and socket scroll handlers: their window already open shows the key's box; else the
    /// plain use (the server's answer opens the window).</summary>
    bool ScrollUse(Item item, int dialogId, string openKey)
    {
        if (IsDialogOpen(dialogId)) { ShowCommonBox(openKey); return false; }
        return FunctionUse(item, "");
    }

    /// <summary>The handlers' confirm box: CreateMessageBox(0x11, text, 0, "use_function_item"), SetItemUid, FUN_141a024a0(0)
    /// when <paramref name="noEnter"/> (Enter does not answer it), SetItemEntry(item, its name, 1, 0, -1, -1, 1) when
    /// <paramref name="entry"/>, SetButtonLabelIds(0x477, 0x478, 0), shown.</summary>
    void UseFunctionBox(Item item, string text, bool noEnter, bool entry = true)
    {
        var box = MessageBoxQueue.Create(0x11, text, null, UseFunctionItemTag);
        box.ItemUid = item.Uid;
        if (noEnter) box.EnterAllowed = false;
        if (entry) box.SetItemEntry(item, ItemName(item), 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
        box.SetButtonLabelIds(0x477, 0x478, 0);
        ShowBox(box);
    }

    /// <summary>CStringTable::GetInventoryTabName (stringtable.cpp:931): the tab's common string, "" past the 15 tabs.</summary>
    public static string InventoryTabName(GameTables t, int tab) => tab switch
    {
        0 => t.Common(0x259), 4 => t.Common(0x25a), 7 => t.Common(0x25b), 2 => t.Common(0x25c), 1 => t.Common(0x25d),
        6 => t.Common(0x25e), 5 => t.Common(0x25f), 3 => t.Common(0x260), 8 => t.Common(0x261), 9 => t.Common(0x262),
        10 => t.Common(0x263), 11 => t.Common(0x264), 12 => t.Common(0x265),
        13 => t.Common("s_inventory_tab_lapenshard"), 14 => t.Common("s_inventory_tab_piece"),
        _ => t.Common(0),
    } ?? "";

    /// <summary>IsExtraSlotsMaxed (inventory.cpp:461): the tab's extra slots equal its expand maximum (FUN_140a66750).</summary>
    bool TabMaxed(int tab) => View.Capacity(tab) - BaseSlots(tab) == MaxSlots(tab);

    /// <summary>
    /// ExpandInven @0x140d23870: FUN_140a5ce30 names the tabs already at their maximum (", " between); all 15 at it: box
    /// 0x2a3, nothing sent. Else the text is BlockFormat(0x29c), or FormatStringId(0x2a6, the names) when some tab is at its
    /// maximum and the item has exactly one argument; a <see cref="UseFunctionBox"/> with the item entry.
    /// </summary>
    bool ExpandInven(Item item)
    {
        string names = "";
        int maxed = 0;
        for (int i = 0; i < 15; i++)
        {
            if (!TabMaxed(i)) continue;
            if (maxed++ > 0) names += ", ";
            names += InventoryTabName(Model.Tables, i);
        }
        if (maxed >= 15) { ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(0x2a3), null, "")); return false; }
        string text = ClientFormat.Format(Model.Tables.Common(0x29c) ?? "");
        if (names.Length != 0 && item.Data.FunctionArgs.Count == 1) text = ClientFormat.Format(Model.Tables.Common(0x2a6) ?? "", names);
        UseFunctionBox(item, text, noEnter: true);
        return true;
    }

    /// <summary>
    /// OpenItemBoxWithKey @0x140d25430: the key id (argument 0) and the count it needs (argument 1) against the bag's count
    /// of the key (FUN_140a5f180); too few: s_msg_item_open_item_box_with_key_no_key_count (the key's name, have, need).
    /// A gender the box allows: <see cref="UseFunctionBox"/> of s_msg_item_open_item_box_with_key (the key's name);
    /// else s_warningGender and box 0x2d. Without the wearer's record the gender is not checked.
    /// </summary>
    bool OpenItemBoxWithKey(Item item)
    {
        var d = item.Data;
        int keyId = d.FunctionArgInt(0, 0), need = d.FunctionArgInt(1, 0);
        int have = keyId == 0 ? 0 : BagCount(keyId);
        string keyName = Model.Tables.ItemName(keyId) ?? "";
        if (need > have)
        {
            ShowBox(MessageBoxQueue.Create(0, ClientFormat.Format(Model.Tables.Common(WithKeyNoKeyKey) ?? "", keyName, have, need), null, ""));
            return true;
        }
        var record = Wearer?.Invoke();
        if (record != null && !d.IsGenderAllowed(record.Gender))
        {
            WarningSound?.Invoke(GenderWarningSound);
            ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(0x2d), null, ""));
            return true;
        }
        UseFunctionBox(item, ClientFormat.Format(Model.Tables.Common(WithKeyKey) ?? "", keyName), noEnter: false);
        return true;
    }

    /// <summary>
    /// LevelPotion @0x140d24900: a tutorial field: the notice s_tutorial_levelup_item_not_use; the xml targetLevel (0:
    /// nothing); a player already at it: box s_err_cannot_already_target_level_potion (the level); dead: box 0x54; else the
    /// plain use. Without the wearer's record the level is not checked.
    /// </summary>
    bool LevelPotion(Item item)
    {
        if (FieldIsTutorial()) { SystemNotice?.Invoke(TutorialLevelUpKey); return false; }
        int level = (ushort)item.Data.FunctionXmlInt("targetLevel");
        if (level == 0) return false;
        var record = Wearer?.Invoke();
        if (record != null && level <= record.Level)
        {
            ShowBox(MessageBoxQueue.Create(0, ClientFormat.Format(Model.Tables.Common(TargetLevelKey) ?? "", level), null, ""));
            return false;
        }
        if (LocalPlayerDead()) { ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(0x54), null, "")); return false; }
        return FunctionUse(item, "");
    }

    /// <summary>
    /// OpenItemBoxLimitedQuantity @0x140d25c50: the xml cooldownTime (seconds; 0 for an empty xml); with one and a GM level
    /// under 0x32, a use sooner than that since the last one of the same item id does nothing, else the tick is kept; then the
    /// plain use.
    /// </summary>
    bool OpenItemBoxLimitedQuantity(Item item)
    {
        int cooldown = item.Data.HasFunctionXml ? item.Data.FunctionXmlInt("cooldownTime") : 0;
        if (cooldown > 0 && GmLevel() < 0x32)
        {
            uint now = TickCount();
            uint last = lastLimitedUse.GetValueOrDefault(item.ItemId);
            if (now - last < (uint)(cooldown * 1000)) return false;
            lastLimitedUse[item.ItemId] = now;
        }
        FunctionUse(item, "");
        return true;
    }

    /// <summary>
    /// ResetAbilityCoolTime @0x140d22b50: the character abilities open (FUN_1401e0800 of the level: feature 0x2d and the
    /// start level) and the constant's quest (CharacterAbilityOpenQuestID) rewarded, else box 0xc63; then a type 1 box of
    /// common string 0xc62 (FUN_140d73e40) with the "use_function_item" tag and the item's uid, Enter not answering it.
    /// Without the wearer's record the level is 0 (closed).
    /// </summary>
    bool ResetAbilityCoolTime(Item item)
    {
        ushort level = Wearer?.Invoke()?.Level ?? 0;
        if (!CharacterAbilityData.IsOpen(Model.IsFeatureEnabled(Ms2.Ui.Feature.CharacterAbility), level)
            || !IsQuestRewarded((int)Model.Tables.CharacterAbilityOpenQuestID))
        {
            ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(0xc63), null, ""));
            return false;
        }
        var box = MessageBoxQueue.Create(1, Model.Tables.Common(0xc62), null, UseFunctionItemTag);
        box.ItemUid = item.Uid;
        box.EnterAllowed = false;
        box.SetButtonLabelIds(0x477, 0x478, 0);
        ShowBox(box);
        return true;
    }

    /// <summary>
    /// ChangeGuildName @0x140d233d0: not the guild's master: the notice common 0x416. Else a type 10 box of
    /// s_guild_ui_change_name (GuildNameLengthMax) tagged "changeGuildName" with the item's uid and an empty input of that
    /// many characters (SetInput(0, "", max, 0, 0)). 0 either way.
    /// </summary>
    bool ChangeGuildName(Item item)
    {
        if (!IsGuildMaster()) { NoticeText?.Invoke(Model.Tables.Common(0x416) ?? ""); return false; }
        int max = Model.Tables.GuildNameLengthMax;
        var box = MessageBoxQueue.Create(10, ClientFormat.Format(Model.Tables.Common(GuildNameKey) ?? "", max), null, ChangeGuildNameTag);
        box.ItemUid = item.Uid;
        box.SetInput(0, "", max, 0, 0);
        ShowBox(box);
        return false;
    }

    /// <summary>ChangeCharName @0x140d236a0: the name box (<see cref="CharNameChange.InputBox"/>); 1. STILL-OPEN: the blocked
    /// action 10 (CBlockManager::FindBlockedAction, its notice FUN_140a30ad0) goes first in the client.</summary>
    bool ChangeCharName(Item item)
    {
        ShowBox(CharNameChange.InputBox(item.Uid, Model.Tables.Common));
        return true;
    }

    /// <summary>HandleConfirmChangeCharNameButton's use (uimessagebox.cpp:1624): the item of the uid used with the name
    /// (FUN_140a63880); nothing without the item.</summary>
    public void ConfirmCharName(long uid, string name)
    {
        if (Model != null && View?.FindByUid(uid) is { } e && ToItem(e, Model.Tables.Items) is { } item) FunctionUse(item, name);
    }

    /// <summary>UIMessageBox::HandleChangeGuildNameButton's use (uimessagebox.cpp:2159), after its empty name and
    /// CheckGuildName tests: the item of the box's uid is used with the name (FUN_140a63880); no such item: 0 (the box
    /// stays).</summary>
    public bool ConfirmGuildName(long uid, string name)
    {
        if (Model == null || View?.FindByUid(uid) is not { } e || ToItem(e, Model.Tables.Items) is not { } item) return false;
        FunctionUse(item, name);
        return true;
    }
}
