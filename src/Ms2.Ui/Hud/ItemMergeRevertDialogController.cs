using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The merge revert window, UIItemMergeRevertDialog (client/ms2/uiitemmergerevertdialog.cpp, UI id 0x4f3d, type name
/// "ItemMergeRevertDialog", movie uiitemmergerevertdialog). It is opened by using an "ItemMergeRevert" scroll (the scroll's uid
/// is the dialog's m_nItemUid, ItemMergeRevert @0x140d21480). One ItemScrollSlot, named by the type name, holds the item to
/// revert; it is picked by a drop from the inventory or by the inventory's right-click (use mode 17), checked by
/// <see cref="CheckItem"/>, and onOk sends ItemMerge mode 6 (scroll uid, item uid). The server's mode 6 answer
/// (<see cref="ShowReverted"/>) is a notice and the close.
/// <para>STILL-OPEN: the revert price (CLua::CalcItemMergeRevertMeso is a game Lua function: <see cref="RevertMeso"/> is the
/// host's), the exact text of GetMergeOptionDescription (here every merged option on its own line), CItemData::FUN_14053d460's
/// type 0x17 branch, the notice kind 3 look, the slot's drag-out (FUN_1422aeed0 unrecovered), the inventory slot redraws.</para>
/// </summary>
public sealed class ItemMergeRevertDialogController : UiController, IDragSlotOwner
{
    public override string Movie => "uiitemmergerevertdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemMergeRevertDialog";

    public const int UiId = 0x4f3d;
    /// <summary>ItemScrollSlot's flags (m_nFlags(1)) and its tooltip's (ASTooltip 0x20).</summary>
    public const int SlotFlags = 1, TooltipFlags = 0x20;

    /// <summary>CheckItem's answers.</summary>
    public const int CheckOk = 0, CheckInvalidItem = 3, CheckNoScroll = 5, CheckRank = 6, CheckNoOption = 7, CheckSlot = 8;

    /// <summary>FUN_140710590: the packet is sent.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>Unk_Inventory::FindItemByUid: an inventory item by uid.</summary>
    public Func<long, Item> Find = _ => null;
    /// <summary>ShowSystemNotice(text, 1, 1) and ShowSystemNotice(text, 3, 1).</summary>
    public Action<string> Notice = _ => { }, DoneNotice = _ => { };
    /// <summary>CLua::CalcItemMergeRevertMeso(rarity, equip subtype).</summary>
    public Func<int, int, long> RevertMeso = (_, _) => 0;
    /// <summary>The common string of a key and the item's name (CItem::FUN_140528160 / FUN_140527b10).</summary>
    public Func<string, string> Text;
    public Func<Item, string> ItemName = _ => "";
    public event Action Closed;

    /// <summary>m_nItemUid: the scroll (SetItemUid).</summary>
    public long ScrollUid { get; private set; }
    /// <summary>The item in the slot (ItemScrollSlot::m_spItem), null when empty.</summary>
    public Item Picked { get; private set; }
    bool closing;

    string T(string key) => Text != null ? Text(key) ?? key : Model?.Tables.Common(key) ?? key;

    /// <summary>UIItemMergeRevertDialog::SetItemUid(CItem*): the scroll that opened the dialog.</summary>
    public void SetItemUid(Item scroll) => ScrollUid = scroll?.Uid ?? 0;

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        ShowEmpty();
    }

    /// <summary>vfunc_67: the scroll forgotten, the slot released.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        ScrollUid = 0;
        Picked = null;
        base.Close();
        Closed?.Invoke();
    }

    // ---- the picks ---------------------------------------------------------------------------------------------------

    /// <summary>CheckItem: 3 no item, no data or not fusionable; 5 the scroll is gone or has no option kind; 6 the item's rarity
    /// differs from the scroll's; 7 the item holds no merged options; 8 the scroll's kind (its first argument) is not the item's.</summary>
    public int CheckItem(Item item)
    {
        if (item?.Data == null || item.Data.Fusionable == 0) return CheckInvalidItem;
        if (Find(ScrollUid) is not { } scroll) return CheckNoScroll;
        if (scroll.Rarity != item.Rarity) return CheckRank;
        if (!ItemMergeOptionDialogController.HasMergedOptions(item)) return CheckNoOption;
        if (scroll.Data == null) return CheckNoScroll;
        int kind = scroll.Data.FunctionArgInt(0, 0);
        if (kind == 0) return CheckNoScroll;
        return kind != item.Data.MergeRevertKind ? CheckSlot : CheckOk;
    }

    /// <summary>ShowErrorMessage: the notice of the code (4 is the material one; an unknown code shows nothing).</summary>
    public void ShowErrorMessage(int error)
    {
        string key = error switch
        {
            5 => "s_item_merge_option_error_invalid_merge_scroll",
            6 => "s_item_merge_option_error_invalid_revert_rank",
            7 => "s_item_merge_option_error_invalid_revert_option",
            4 => "s_item_merge_option_error_invalid_material",
            8 => "s_item_merge_option_error_invalid_revert_slot",
            3 => "s_item_merge_option_error_invalid_mergeitem",
            _ => null,
        };
        if (key != null) Notice(T(key));
    }

    /// <summary>InvenSlot use mode 17 (the right-click, invenslot.cpp:1080): a refused item is its notice, else it is put in the slot.</summary>
    public void UseFromInventory(Item item)
    {
        int error = CheckItem(item);
        if (error != CheckOk) ShowErrorMessage(error);
        else ShowItem(item);
    }

    /// <summary>FUN_140e4c440: the slot emptied, setData {isReady 1, binding, price 0}.</summary>
    void ShowEmpty()
    {
        Picked = null;
        if (Host?.Entity == null || closing) return;
        Host.Push(TypeName);
        Host.Invoke(Host.Entity, "setData", Host.Obj(("isReady", 1), ("binding", TypeName), ("price", 0)));
    }

    /// <summary>FUN_140e4c7d0: the item in the slot, setData {isReady 0, binding, name, optionStr, enabled, itemGUID, price}.</summary>
    void ShowItem(Item item)
    {
        Picked = item;
        if (Host?.Entity == null || closing || Model == null) return;
        Host.Push(TypeName);
        string text = Ms2.Formats.World.ReservedWords.StripTags(ClientFormat.Format("{0}{1}", T("s_tooltip_itemoption_kinds_merge"), OptionDescription(item)));
        Host.Invoke(Host.Entity, "setData", Host.Obj(
            ("isReady", 0), ("binding", TypeName), ("name", ItemName(item)), ("optionStr", text),
            ("enabled", CheckItem(item) == CheckOk), ("itemGUID", item.Uid.ToString(CultureInfo.InvariantCulture)),
            ("price", RevertMeso(item.Rarity, item.Data.EquipSubtype))));
    }

    /// <summary>GetMergeOptionDescription(item, 1): the merged (group 4) options, one per line.</summary>
    string OptionDescription(Item item)
    {
        var o = item.Record?.Options;
        if (o == null) return "";
        var lines = new List<string>();
        foreach (var (stat, v) in o.Stat[4]) lines.Add((Model.Tables.StatName(stat) + " " + StatValue(stat, v)).Trim());
        foreach (var (sp, v) in o.Special[4]) lines.Add((Model.Tables.SpecialAbilityName(sp) + " " + SpecialValue(sp, v)).Trim());
        return string.Join("\n", lines);
    }

    static string Percent(float rate) => ClientFormat.Format("{0:0.1f}", rate * 100.0f) + "%";
    string StatValue(int stat, int v) => Model.Tables.ItemOptions.IsStatRate(stat) ? Percent(v / 1000.0f) : v.ToString(CultureInfo.InvariantCulture);
    string SpecialValue(int sp, int v) => Model.Tables.ItemOptions.IsSpecialRate(sp) ? Percent(v / 1000.0f) : v.ToString(CultureInfo.InvariantCulture);

    // ---- the movie's commands and the answer -------------------------------------------------------------------------

    /// <summary>vfunc_28's onOk: the uid text of the argument is looked up (none found: nothing), checked, and sent as mode 6.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!name.Equals("onOk", StringComparison.OrdinalIgnoreCase)) return false;
        if (args == null || args.Length == 0) return true;
        long uid = args[0] is string s ? (long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out long v) ? v : 0) : args[0] == null ? 0 : (long)Rt.ToNumber(args[0]);
        if (Find(uid) is not { } item || CheckItem(item) != CheckOk) return true;
        Send(ItemMergeRequest.Revert(ScrollUid, item.Uid));
        return true;
    }

    /// <summary>FUN_140e4cda0 (OnItemFusion mode 6): the item's refreshed, the notice with its name, the close.</summary>
    public void ShowReverted(Item item)
    {
        if (closing || item == null) return;
        DoneNotice(ClientFormat.Format(T("s_item_merge_option_revert"), ItemName(item)));
        Close();
    }

    // ---- the slot (ItemScrollSlot) ------------------------------------------------------------------------------------

    public override IEnumerable<string> GetBagSlotBindings(string bag) => Array.Empty<string>();

    public override object GetBinding(string name)
    {
        if (name != TypeName || Picked == null || Model == null) return base.GetBinding(name);
        var slot = new List<(string, object)>();
        return ItemSlot.Build(slot, Picked, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources) ? Host.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (name != TypeName) return base.GetSlotTooltip(name);
        var tip = new ItemTooltip(Model, TooltipFlags) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        return Picked != null && tip.Fill(Picked) ? tip.Data : new AsTooltip();
    }

    public bool IsDragSlot(string name) => name == TypeName;
    public void RefreshSlot(string name) { if (name == TypeName) Host?.Push(name); }
    public void SlotDragStarted(string name) { }
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>ItemScrollSlot::vfunc_14 / vfunc_17 (the remove action): the slot emptied and OnItemRemoved's FUN_140e4c440.</summary>
    public void RemoveFromSlot() => ShowEmpty();

    /// <summary>OnItemDropped: an inventory slot's item that the inventory has not is the invalid-item notice; a refused item its
    /// notice; else it is shown. The dragged slot is refreshed either way (DragSlot::vfunc_10).</summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (name == TypeName && Host?.Entity != null && sourceOwner is IInvenSlotOwner inv && inv.InvenSlotUid(source) is long uid)
        {
            if (Find(uid) is not { } item) Notice(T("s_item_merge_option_error_invalid_mergeitem"));
            else UseFromInventory(item);
        }
        sourceOwner?.RefreshSlot(source);
    }
}
