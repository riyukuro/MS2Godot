using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The item box use handlers of RegisterItemUseFuncs (unit_140d1ae30_itemfunctiondispatch.cpp): OpenItemBox @0x140d259d0,
/// its confirm boxes and OpenItemBoxContents, and SelectItemBox @0x140d22430. Nothing is sent or used up here before the
/// server answers: the plain use goes through <see cref="RequestUseItem"/>, the multi open to the window of
/// <see cref="OpenMultiBox"/>.
/// STILL-OPEN: the 7-day "do not ask again" timers (m_n10Time / m_n18Time, game option save; the boxes always ask),
/// UseItemCheckCapacity (the capacity box is never shown: the confirm goes straight to OpenItemBoxContents), content flag 0x6e (shutdown, FUN_140d1d3f0),
/// the xml "notifyBox" common string of the confirm (ItemData does not parse the xml function form; the default key is
/// used), the item's m_dc (FUN_140529640, a count over 0 forces a single open: read as 0), the ShutdownDropBox list, the multi-count box's spBox->FUN_141a024a0(0) call.
/// </summary>
public sealed partial class InventoryController
{
    /// <summary>The tags of the three confirm boxes (their callbacks are OnOpenItemBoxConfirm / Capacity / Multi).</summary>
    public const string OpenBoxKey = "s_msg_item_open_item_box", OpenBoxMultiKey = "s_msg_item_open_item_box_multi",
        OpenBoxMultiNoCountKey = "s_msg_item_open_item_box_multi_no_having_count", OpenBoxMultiOpenMsgKey = "s_msg_item_open_item_box_multiopen_msg",
        OpenBoxMultiOpenTitleKey = "s_msg_item_open_item_box_multiopen_title", OpenBoxMultiOpenOkKey = "s_msg_item_open_item_box_multiopen_ok",
        CancelWordKey = "s_word_cancel", GenderWarningSound = "s_warningGender";

    /// <summary>FUN_140c3f9d0 (the multi open window): item id, rarity (the request's slot), the arguments, the count.</summary>
    public Action<int, int, string, int> OpenMultiBox;
    /// <summary>SelectItemBox's GetItemUseDialog / open (0x4eff): the item whose box is picked from.</summary>
    public Action<Item> OpenSelectBox;
    /// <summary>The select dialog is already open (SelectItemBox returns 1 without opening).</summary>
    public Func<bool> SelectBoxOpen = () => false;
    public event Action<string> WarningSound;

    /// <summary>Bag count of an item id (FUN_140a5f180(id, 0)).</summary>
    public int BagCountOf(int itemId) => BagCount(itemId);

    int BagCount(int itemId)
    {
        int n = 0;
        foreach (var c in View.Cells(View.TabOfItem(itemId))) if (!c.Empty && c.ItemId == itemId) n += c.Amount;
        return n;
    }

    /// <summary>FUN_140a5f410: the count of the same id and rarity.</summary>
    int BagCountSameRarity(Item item)
    {
        int n = 0;
        foreach (var c in View.Cells(View.TabOfItem(item.ItemId)))
            if (!c.Empty && c.ItemId == item.ItemId && FindItem(c.Uid) is { } o && o.Rarity == item.Rarity) n += c.Amount;
        return n;
    }

    string ItemName(Item item) => new ItemTooltip(Model, 0).Name(item);

    /// <summary>
    /// OpenItemBox(spItem, pc) @0x140d259d0: a gender the item does not allow plays s_warningGender with notice 0x2d, nothing
    /// sent; nCount is arg 4 (default 1) only past 4 arguments; more than the bag holds shows
    /// s_msg_item_open_item_box_multi_no_having_count (have, count); else <see cref="OpenItemBoxAsk"/>.
    /// </summary>
    internal bool OpenItemBox(Item item)
    {
        var d = item.Data;
        var record = Wearer?.Invoke();
        if (record != null && !d.IsGenderAllowed(record.Gender))
        {
            WarningSound?.Invoke(GenderWarningSound);
            SystemNotice?.Invoke(Model.Tables.Common(0x2d));
            return false;
        }
        int nCount = d.FunctionArgs.Count > 4 ? d.FunctionArgInt(4, 1) : 1;
        int have = BagCount(item.ItemId);
        if (nCount > have)
        {
            ShowBox(MessageBoxQueue.Create(0, ClientFormat.Format(Model.Tables.Common(OpenBoxMultiNoCountKey) ?? "", have, nCount), null, ""));
            return false;
        }
        OpenItemBoxAsk(item, nCount);
        return true;
    }

    /// <summary>OpenItemBox(item, count) @0x140d1cbd0: the confirm box 0x1a (item entry, button preset 1, checkbox 0x29e unchecked);
    /// its button 0 is OnOpenItemBoxConfirm.</summary>
    void OpenItemBoxAsk(Item item, int count)
    {
        int have = BagCount(item.ItemId);
        string text = Model.Tables.Common(OpenBoxKey) ?? "";
        if (count > 1) text = ClientFormat.Format(Model.Tables.Common(OpenBoxMultiKey) ?? "", have, count);
        var box = MessageBoxQueue.Create(0x1a, text, null, "");
        box.Callback = (button, b) => OnOpenItemBoxConfirm(button, b);
        box.ItemUid = item.Uid;
        box.SetItemEntry(item, ItemName(item), 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
        box.SetButtonPreset(1);
        box.SetCheckbox0(MessageBoxData.Common(0x29e), false);
        ShowBox(box);
    }

    /// <summary>OnOpenItemBoxConfirm @0x140d1da90: button 0 only; the item found by uid goes straight to
    /// <see cref="OpenItemBoxContents"/> (the client's capacity check, UseItemCheckCapacity and its box, is STILL-OPEN).
    /// STILL-OPEN: the checked box saves the 7-day timer.</summary>
    bool OnOpenItemBoxConfirm(int button, MessageBoxData box)
    {
        if (button != 0) return true;
        if (FindItem(box.ItemUid) is { } item) OpenItemBoxContents(item, "");
        return true;
    }

    /// <summary>OpenItemBoxContents @0x140d1d3f0: nMultiOpen = arg 4 (default 1); the count of the same id and rarity clamped
    /// to 0..<see cref="GameTables.ItemBoxMultiOpenLimitCount"/>; with nMultiOpen 1 and more than one, box 0x29 asks how many (OnOpenItemBoxMulti);
    /// else the plain use with the arguments.</summary>
    public void OpenItemBoxContents(Item item, string strArgs)
    {
        int multi = item.Data.FunctionArgInt(4, 1);
        int count = Math.Clamp(BagCountSameRarity(item), 0, Model.Tables.ItemBoxMultiOpenLimitCount);
        if (multi == 1 && count > 1)
        {
            var box = MessageBoxQueue.Create(0x29, Model.Tables.Common(OpenBoxMultiOpenMsgKey) ?? "", null, "");
            box.Title = Model.Tables.Common(OpenBoxMultiOpenTitleKey) ?? "";
            box.SetButtonLabels(Model.Tables.Common(OpenBoxMultiOpenOkKey) ?? "", Model.Tables.Common(CancelWordKey) ?? "", "");
            box.SetItemEntry(item, ItemName(item), 1, count, count, -1, 1, Model.Tables.Items.TextColor);
            box.Args.Add(strArgs ?? "");
            box.ItemUid = item.Uid;
            box.Callback = (button, b) => OnOpenItemBoxMulti(button, b);
            ShowBox(box);
            return;
        }
        RequestUseItem(item, strArgs ?? "");
    }

    /// <summary>OnOpenItemBoxMulti @0x140d1dc60: button 0; the box's strArgs is its argument 0; the clicked tag is split at ','
    /// (at least two fields) and the second is the count (not below 0, at most the limit). More than one opens the window of
    /// <see cref="OpenMultiBox"/> unless one is open; else the plain use.</summary>
    bool OnOpenItemBoxMulti(int button, MessageBoxData box)
    {
        if (button != 0) return true;
        string strArgs = box.Args.Count > 0 ? box.Args[0] : "";
        var fields = box.ClickedTag.Split(',');
        if (fields.Length < 2) return true;
        int count = Math.Clamp(MessageBoxData.WToI(fields[1]), 0, Model.Tables.ItemBoxMultiOpenLimitCount);
        if (FindItem(box.ItemUid) is not { } item) return true;
        if (count > 1) OpenMultiBox?.Invoke(item.ItemId, item.Rarity, strArgs, count);
        else RequestUseItem(item, strArgs);
        return true;
    }

    /// <summary>The answer of the blueprint open box: UIUGCCubeBillDialog::FUN_140ee8000 (<see cref="CubeBill.OnBlueprintOpenBox"/>), (answer, the box's item uid).
    /// Null while no bill dialog is wired: BlueprintImport then stays with <see cref="OwnUseFunctions"/>.</summary>
    public Func<int, long, bool> BlueprintOpenAnswer;
    public const string UseFunctionItemTag = "use_function_item", OpenBlueprintKey = "s_msg_item_open_blueprint";

    /// <summary>pUgc->m_nUgcUid of GetUgcInfo() (the look's id, uimeratmarketdialog.cpp:1764); null for an item with no ugc block (the client's GetUgcInfo() == 0).</summary>
    static long? BlueprintUgcUid(Item item) => item.Record?.Ugc?.Uid;

    /// <summary>BlueprintImport @0x140d1f6d0 (unit_140d1ae30_itemfunctiondispatch.cpp:982-1022), the blueprint with a ugc uid: box 0x11 with
    /// s_msg_item_open_blueprint and the item's name, the item uid and entry, yes / no 0x477 / 0x478, answered by FUN_140ee8000.
    /// STILL-OPEN: a blueprint with ugc uid 0 (FUN_1406321d0 → FUN_141405440, or UIItemBlueprintCreateDialog 0x4f7b); it stays with <see cref="OwnUseFunctions"/>.
    /// The GetItemUseDialog(0x4f29) test is the bill dialog existing, which always holds here.</summary>
    internal bool BlueprintImport(Item item)
    {
        string name = new ItemTooltip(Model, 0).Name(item);
        var box = MessageBoxQueue.Create(0x11, Ms2.Quest.ClientFormat.Format(Model.Tables.Common(OpenBlueprintKey) ?? "", name), null, UseFunctionItemTag);
        box.ItemUid = item.Uid;
        box.SetItemEntry(item, name, 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
        box.SetButtonLabelIds(0x477, 0x478, 0);
        box.EnterAllowed = false;   // FUN_141a024a0(0) (itemfunctiondispatch.cpp:1015): Enter does not answer
        box.Callback = (answer, b) => BlueprintOpenAnswer(answer, b.ItemUid);
        ShowBox(box);
        return true;
    }

    /// <summary>SelectItemBox @0x140d22430: the dialog of the item (nothing when it is open).</summary>
    internal bool SelectItemBox(Item item)
    {
        if (SelectBoxOpen()) return true;
        OpenSelectBox?.Invoke(item);
        return true;
    }
}
