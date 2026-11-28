using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIItemTreasureMapDialog (client/ms2/uiitemtreasuremapdialog.cpp, UI id 0x4f5b, type name "ItemTreasureMapDialog", movie
/// uiitemtreasuremapdialog): the appraise of a treasure map. An npc talk's open-dialog action opens it by name with "free" or
/// nothing (uitalkcinematic.cpp:1221: FUN_140c55b50 first). The bag lists the treasure maps of inventory category 7; a pick
/// asks the server its cost (TreasureMap sub 2), the answer (ItemScript sub 2) shows the map and the cost, the submit button
/// sends the map's use (with "free" for a free appraise) and the server's ItemScript sub 3 shows the appraised item, after
/// which the button goes back to the select page.
/// STILL-OPEN: vfunc_43's place beside the maid dialog (FindTalkCinematic(0x4e40)); the dialog stays at the movie's place.
/// </summary>
public sealed class ItemTreasureMapDialogController : UiController
{
    public override string Movie => "uiitemtreasuremapdialog";
    /// <summary>The CommonDepth layer of the movie: UIItemTreasureMapDialog's ctor sets m_2c to 0xc (uiitemtreasuremapdialog.cpp:89).</summary>
    public override int LoadLayer => CommonDepth.Scene;
    protected override string EntityMethod => "alignment";
    public override string TypeName => Name;   // GetTypeName (@0x140c55210)
    /// <summary>vfunc_13 (uiitemtreasuremapdialog.cpp:167) returns 0 without UIDialog's: Esc passes the dialog, and only the
    /// field Esc's CloseAllDialogs closes it.</summary>
    public override bool EscapeKey() => false;
    /// <summary>The type name, also the name the talk opens it by.</summary>
    public const string Name = "ItemTreasureMapDialog";

    public const int UiId = 0x4f5b;
    public const string Function = "TreasureMap", FreeArgument = "free";
    /// <summary>The inventory category of the maps (RefreshTreasureMapBag: GetItemUidsInCategory(7)).</summary>
    public const int Category = 7;
    /// <summary>The bag's and the shown slot's icon type and flags (CreateItemSlotBag / CreateItemInfoSlot: 0x415 with 8; the
    /// appraised item 0x415 with 0x800).</summary>
    public const int IconType = 0x415, BagFlags = 8, ResultFlags = 0x800;
    /// <summary>StartAppraise's notice when the mesos do not cover the cost (FUN_141275260(0x34, 1, 1)).</summary>
    public const int NotEnoughMesoNotice = 0x34;
    public const string StampSound = "AttendGift_Stamp", ChoiceSound = "Enchant_ItemChoice";
    public const string DescKey = "s_item_treasure_map_appraise_desc", FreeDescKey = "s_item_treasure_map_free_appraise_desc",
        AppraiseButtonKey = "s_item_treasure_map_button_appraise", BackButtonKey = "s_item_treasure_map_button_back",
        CostKey = "s_item_treasure_map_appraise_cost", CantAppraiseKey = "s_item_treasure_map_cant_appraise";

    /// <summary>m_strBag / m_strBind (the ctor @0x140c54ee0: "{0}.Target" / "{0}.Select" of the type name).</summary>
    public const string Bag = Name + ".Target", Bind = Name + ".Select";

    /// <summary>m_nState: 0 select, 1 waiting for the server's result, 2 the result shown.</summary>
    public int State { get; private set; }
    /// <summary>m_nCost: the picked map's cost (-1 before the first select page).</summary>
    public long Cost { get; private set; } = -1;
    /// <summary>m_bFree.</summary>
    public bool Free { get; private set; }
    readonly List<Item> items = new();                 // m_items
    readonly List<string> bagSlots = new();
    Item target;                                       // m_spTarget
    long targetUid;                                    // m_nTargetUid
    object board, submit, itemBag;                     // m_board, m_submit, m_itemBag

    /// <summary>GetItemUidsInCategory(category) then FindItemByUid: the category's items in slot order.</summary>
    public Func<int, IEnumerable<Item>> CategoryItems = _ => Array.Empty<Item>();
    /// <summary>Unk_Inventory::FindItemByUid.</summary>
    public Func<long, Item> FindByUid = _ => null;
    /// <summary>FUN_140a63880(item, argument): the use request; false when it was not sent.</summary>
    public Func<Item, string, bool> RequestUse = (_, _) => false;
    /// <summary>FUN_140710590: sends the packet.</summary>
    public Action<TreasureMapCostRequest> Send = _ => { };
    /// <summary>DAT_143ab2898: the mesos.</summary>
    public Func<long> Mesos = () => 0;
    /// <summary>PlaySystemSound(key, "").</summary>
    public Action<string> PlaySound = _ => { };
    /// <summary>FUN_141275260(id, 1, 1): the common string's system notice.</summary>
    public Action<int> CommonNotice = _ => { };

    public IReadOnlyList<Item> Items => items;
    public Item Target => target;
    public long TargetUid => targetUid;
    public IReadOnlyList<string> BagSlots => bagSlots;

    /// <summary>FUN_140c55b50 @0x140c55b50: "free" (no case) makes the appraise free, anything else not.</summary>
    public void SetFree(string argument) => Free = string.Equals(argument, FreeArgument, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// OnLoadComplete @0x140c553a0: m_board, m_targetItems' m_itemBag, the select page, the bag's list index change handler
    /// (SendTreasureMapMode2), m_board's m_submit click handler and m_desc with the free or paid description.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        board = Host.Child("m_board");
        if (Host.Child("m_targetItems") is { } targets) itemBag = Host.Child("m_itemBag", targets);
        ShowSelectPage();
        if (itemBag != null)
        {
            Func<object[], object> f = _ => { Host.RT.Guard("treasure map listIndexChange", OnBagIndexChange); return null; };
            Host.Invoke(itemBag, "addEventListener", "listIndexChange", f, false, 0, false);   // AddListIndexChangeHandler
        }
        if (board != null)
        {
            submit = Host.Child("m_submit", board);
            if (submit != null)
            {
                Func<object[], object> f = _ => { Host.RT.Guard("treasure map submit", OnSubmitClick); return null; };
                Host.Invoke(submit, "addEventListener", "buttonClick", f, false, 0, false);
            }
            SetHtml(board, "m_desc", Common(Free ? FreeDescKey : DescKey));
        }
    }

    /// <summary>vfunc_67 @0x140c558a0: the bag and the shown slot dropped, the state, target and maps cleared, the children
    /// dropped, the target uid and the free flag cleared, then UIDialog's close.</summary>
    public override void Close()
    {
        DestroyBag();
        Model?.ItemSlots.Remove(Bind);
        State = 0;
        target = null;
        items.Clear();
        board = submit = itemBag = null;
        targetUid = 0;
        Free = false;
        base.Close();
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag) => bag == Bag ? bagSlots : Array.Empty<string>();

    /// <summary>
    /// vfunc_79 @0x140c55960 for ItemScript: loaded only; sub 2 the cost (<see cref="OnAppraiseCost"/>); sub 3 the appraised
    /// item (<see cref="ShowAppraisedItem"/>), or its end: state 2 and back to the select page.
    /// </summary>
    public void OnItemScript(ItemScriptTreasureMap m)
    {
        if (Host?.Entity == null || IsClosed || m == null) return;
        switch (m)
        {
            case ItemScriptTreasureMap.Cost c: OnAppraiseCost(c.Uid, c.Amount); break;
            case ItemScriptTreasureMap.Result { Ok: true } r: ShowAppraisedItem(r.ItemId, r.Rank); break;
            case ItemScriptTreasureMap.Result:
                State = 2;
                ReturnToSelectPage();
                break;
        }
    }

    /// <summary>SendTreasureMapMode2 @0x140c55ba0: the select state only; the bag's selected map's uid with the free flag.</summary>
    void OnBagIndexChange()
    {
        if (itemBag == null || State != 0) return;
        var sel = Host.Get(itemBag, "selectedIndex");
        int index = Rt.IsNullish(sel) ? -1 : Rt.ToInt32(sel);
        if (index < 0 || index >= items.Count) return;
        if (items[index] is not { } item) return;
        Send(new TreasureMapCostRequest(item.Uid, Free));
    }

    /// <summary>A pick of bag row <paramref name="index"/>: the list's selectedIndex set and validated (the CLIK list keeps the new
    /// index until its next validation, which fires listIndexChange; validateNow stands in for that frame).</summary>
    public void SelectRow(int index)
    {
        if (itemBag == null) return;
        Host.Set(itemBag, "selectedIndex", index);
        Host.Invoke(itemBag, "validateNow");
    }

    /// <summary>OnSubmitClick @0x140c55d20: waiting does nothing; the select state starts the appraise; the result state goes back
    /// to the select page and, while the appraised map is still held, asks its cost again.</summary>
    public void OnSubmitClick()
    {
        if (State == 1) return;
        if (State == 0) { StartAppraise(); return; }
        if (State != 2) return;
        ReturnToSelectPage();
        if (targetUid > 0)
        {
            if (FindByUid(targetUid) is { Count: > 0 }) Send(new TreasureMapCostRequest(targetUid, Free));
            targetUid = 0;
        }
    }

    /// <summary>ShowSelectPage @0x140c55ea0: cost 0, the bag refreshed, the submit button "appraise" and disabled, the two
    /// descriptions hidden.</summary>
    void ShowSelectPage()
    {
        if (board == null) return;
        Cost = 0;
        RefreshTreasureMapBag();
        SetChild(board, "m_submit", "label", Common(AppraiseButtonKey));
        SetChild(board, "m_submit", "enabled", false);
        SetChild(board, "m_desc1", "visible", false);
        SetChild(board, "m_desc2", "visible", false);
    }

    /// <summary>RefreshTreasureMapBag @0x140c560f0: the treasure maps of category 7 (<see cref="IsTreasureMap"/>) as the bag
    /// (CreateItemSlotBag 0x415, 8); the list unselected, cleaned up, given the bag id and redrawn.</summary>
    void RefreshTreasureMapBag()
    {
        items.Clear();
        foreach (var it in CategoryItems(Category)) if (IsTreasureMap(it)) items.Add(it);
        DestroyBag();
        for (int i = 0; i < items.Count; i++)
        {
            string name = Bag + "." + i.ToString(CultureInfo.InvariantCulture);
            Model.ItemSlots[name] = new ItemInfoSlot(name, items[i], i, IconType, BagFlags);
            bagSlots.Add(name);
        }
        if (itemBag == null) return;
        Host.Set(itemBag, "selectedIndex", -1);
        if (Host.Get(itemBag, "dataProvider") is { } dp && !Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");   // FUN_1422dd1d0
        Host.Set(itemBag, "bagId", Bag);
        Host.Invoke(itemBag, "invalidate");                                                                  // vfunc_24(true)
        Host.Invoke(itemBag, "validateNow");
    }

    /// <summary>StartAppraise @0x140c563d0: the select state with a picked map only; mesos short of the cost: notice 0x34;
    /// else the map's use with "free" (free) or nothing, and when sent: waiting, the map's uid kept, the descriptions hidden and
    /// the button disabled.</summary>
    public void StartAppraise()
    {
        if (State != 0 || target == null || board == null) return;
        if (Mesos() < Cost) { CommonNotice(NotEnoughMesoNotice); return; }
        if (!RequestUse(target, Free ? FreeArgument : "")) return;
        State = 1;
        targetUid = target.Uid;
        target = null;
        SetChild(board, "m_desc1", "visible", false);
        SetChild(board, "m_desc2", "visible", false);
        SetChild(board, "m_submit", "enabled", false);
    }

    /// <summary>ReturnToSelectPage @0x140c56670: the result state only; select state, no target, the shown slot released and
    /// m_slot emptied (its effects shown), the board on "wait", the descriptions hidden, the select page.</summary>
    void ReturnToSelectPage()
    {
        if (State != 2 || board == null) return;
        State = 0;
        target = null;
        Model.ItemSlots.Remove(Bind);
        if (Host.Child("m_slot", board) is { } slot)
        {
            Host.Invoke(slot, "setData", Host.Obj(("binding", Bind)));
            SetChild(slot, "eff1", "visible", true);
            SetChild(slot, "eff2", "visible", true);
        }
        Host.Invoke(board, "gotoAndPlay", "wait");
        SetChild(board, "m_desc1", "visible", false);
        SetChild(board, "m_desc2", "visible", false);
        ShowSelectPage();
    }

    /// <summary>
    /// OnAppraiseCost @0x140c56a50: the select state only; the cost kept; a held treasure map of the uid becomes the target,
    /// bound to the shown slot (0x415, 8) with its name; the cost text (s_item_treasure_map_appraise_cost with the grouped
    /// mesos), or for a negative cost the cannot text with the button disabled; m_desc1 the map's name, m_desc2 the cost text,
    /// and the choice sound.
    /// </summary>
    public void OnAppraiseCost(long uid, long cost)
    {
        if (State != 0 || board == null) return;
        Cost = cost;
        if (FindByUid(uid) is not { } item || !IsTreasureMap(item)) return;
        target = item;
        string name = ItemName(item);
        Model.ItemSlots[Bind] = new ItemInfoSlot(Bind, item, 0, IconType, BagFlags);
        if (Host.Child("m_slot", board) is { } slot)
        {
            Host.Invoke(slot, "setData", Host.Obj(("itemName", name), ("binding", Bind)));
            SetChild(slot, "eff1", "visible", true);
            SetChild(slot, "eff2", "visible", true);
        }
        string text = ClientFormat.Format(Common(CostKey), NumberText.Group(Cost.ToString(CultureInfo.InvariantCulture)));
        if (cost < 0)
        {
            text = Common(CantAppraiseKey);
            SetChild(board, "m_submit", "enabled", false);
        }
        else SetChild(board, "m_submit", "enabled", true);
        SetChild(board, "m_desc1", "visible", true);
        SetHtml(board, "m_desc1", name);
        SetChild(board, "m_desc2", "visible", true);
        SetHtml(board, "m_desc2", text);
        PlaySound(ChoiceSound);
    }

    /// <summary>
    /// ShowAppraisedItem @0x140c57300: an item of the id and rank bound to the shown slot (0x415, 0x800, its effects hidden),
    /// the board on "appraise" with m_eff1 played from frame 1, m_desc1 the item's name, the button enabled as "back", the
    /// stamp sound, the bag refreshed and the result state.
    /// </summary>
    public void ShowAppraisedItem(int itemId, int rank)
    {
        if (board == null) return;
        var item = Item.Create(Model.Tables.Items, itemId, rank);
        if (item != null)
        {
            Model.ItemSlots[Bind] = new ItemInfoSlot(Bind, item, 0, IconType, ResultFlags);
            if (Host.Child("m_slot", board) is { } slot)
            {
                Host.Invoke(slot, "setData", Host.Obj(("itemName", ItemName(item)), ("binding", Bind)));
                SetChild(slot, "eff1", "visible", false);
                SetChild(slot, "eff2", "visible", false);
            }
        }
        Host.Invoke(board, "gotoAndPlay", "appraise");
        if (Host.Child("m_eff1", board) is { } eff) Host.Invoke(eff, "gotoAndPlay", 1);
        SetChild(board, "m_desc1", "visible", true);
        SetHtml(board, "m_desc1", item != null ? ItemName(item) : "");
        SetChild(board, "m_submit", "enabled", true);
        SetChild(board, "m_submit", "label", Common(BackButtonKey));
        PlaySound(StampSound);
        RefreshTreasureMapBag();
        State = 2;
    }

    /// <summary>IsTreasureMap @0x140c57a70: an item with data whose function is "TreasureMap".</summary>
    public bool IsTreasureMap(Item item) => item != null && Model?.Tables.Items.Get(item.ItemId) is { } d && d.FunctionName == Function;

    void DestroyBag()
    {
        foreach (var n in bagSlots) Model?.ItemSlots.Remove(n);
        bagSlots.Clear();
    }

    string Common(string key) => Model?.Tables.Common(key) ?? "";

    string ItemName(Item item) => new ItemTooltip(Model, 0).Name(item);

    /// <summary>SetChildVisible / SetChildEnabled / SetButtonText: the named child of the clip, its member set; none: nothing.</summary>
    void SetChild(object parent, string name, string member, object value)
    {
        if (Host.Child(name, parent) is { } c) Host.Set(c, member, value);
    }

    /// <summary>SetChildHtmlText (gemstoneslot.cpp:299): the named child's htmlText.</summary>
    void SetHtml(object parent, string name, string text) => SetChild(parent, name, "htmlText", text);
}
