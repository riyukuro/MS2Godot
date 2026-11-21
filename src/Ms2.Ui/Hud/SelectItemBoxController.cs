using System.Globalization;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The decisions of UISelectItemBoxDialog (client/ms2/uiselectitemboxdialog.cpp): how many rewards the player may pick, which of
/// the listed rewards the picks are, and what the OK and the list click do with the list's selection.
/// </summary>
public sealed class SelectItemBoxState
{
    /// <summary>The rows the list always shows (OnLoadComplete @0x140f99550: nMinRows 0x12); the rest are empty entries.</summary>
    public const int MinRows = 0x12;
    /// <summary>The selected-reward slots of the movie (m_selectedSlot0..5).</summary>
    public const int SlotCount = 6;

    public int SelectCount;                                   // m_nSelectCount
    public readonly List<ItemDropEntry> Rewards = new();      // m_rewards

    /// <summary>What the OK handler decides.</summary>
    public enum OkKind { Ignored, Wrong, Open }

    /// <summary>
    /// FUN_140f9a230 (@0x140f9a230, :208): the picks must be exactly SelectCount; one past the rewards is the "wrong" notice;
    /// else the indices joined by "," are what OpenItemBoxContents is given.
    /// </summary>
    public OkKind Ok(IReadOnlyList<uint> selected, out string indices)
    {
        indices = "";
        if (selected.Count != SelectCount) return OkKind.Ignored;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i] >= Rewards.Count) { indices = ""; return OkKind.Wrong; }
            sb.Append(selected[i].ToString(CultureInfo.InvariantCulture));
            if (i < selected.Count - 1) sb.Append(',');
        }
        indices = sb.ToString();
        return OkKind.Open;
    }

    /// <summary>FUN_140f9a5f0 (@0x140f9a5f0, :237): more picks than allowed deselects the clicked row.</summary>
    public bool TooMany(int selectedCount) => selectedCount > SelectCount;

    /// <summary>The OK button's text: "next" when the function's argument 4 (default 1) is 1 and the bag holds more than one.</summary>
    public static bool OkIsNext(Item item, int have) => item.Data.FunctionArgInt(4, 1) == 1 && have > 1;
}

/// <summary>
/// UISelectItemBoxDialog (client/ms2/uiselectitemboxdialog.cpp, UI id 0x4eff, type name "SelectItemBoxDialog", movie
/// uiselectitemboxdialog): lists the rewards of the item's individual drop box (the SelectItemBox function's parameter
/// "selectCount,dropBoxId"), the player picks selectCount of them and OK opens the box with the picked indices.
/// <para>STILL-OPEN: the xml form of the function parameter (selectCount / individualDropBoxId as xml values, the
/// client's m_props branch; ItemData leaves FunctionArgs empty for it, so such an item shows an empty list); the
/// "select_item_box_function" BigData log; the select_confirm sound; the movie's own tween on close.</para>
/// </summary>
public sealed class SelectItemBoxController : UiController
{
    public override string Movie => "uiselectitemboxdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "SelectItemBoxDialog";   // GetTypeName (@0x140f99500)

    public const int UiId = 0x4eff;
    /// <summary>The binders' icon type (the constructor, 0x411).</summary>
    public const int IconType = 0x411;

    public readonly SelectItemBoxState State = new();
    public long ItemUid;                                       // SetItemUid (@0x140f9a200)
    /// <summary>Unk_Inventory::FindItemByUid.</summary>
    public Func<long, Item> FindItem = _ => null;
    /// <summary>How many of an item id the bag holds (FUN_140a5f180).</summary>
    public Func<int, int> HaveCount = _ => 0;
    /// <summary>OpenItemBoxContents(item, indices) (@0x140d1d3f0).</summary>
    public Action<Item, string> OpenContents = (_, _) => { };
    /// <summary>The list the dialog shows (UISelectItemBoxDialog::OnLoadComplete): GetBoxItems with bSkipHidden false, so a
    /// showTooltip 2 row keeps its place and the index the dialog sends is the row's index in the server's list.</summary>
    public static List<ItemDropEntry> BoxRewards(GameTables tables, int boxId, int job, int gender)
    {
        var list = new List<ItemDropEntry>();
        tables.IndividualDrops.GetBoxItems(tables.Items, boxId, job, gender, false, list);
        return list;
    }

    /// <summary>ItemDropTable::GetBoxItems for the box, the player's job group and gender.</summary>
    public Func<int, List<ItemDropEntry>> GetBoxItems = _ => new();
    /// <summary>FUN_1412752d0: a system notice by common string key.</summary>
    public event Action<string> NoticeKey;
    /// <summary>The picks the list holds; the movie's getSelectedList unless set (a test).</summary>
    public Func<List<uint>> SelectedOverride;

    /// <summary>The dialog ran its close (vfunc_67); the host hides the movie when its tween ends.</summary>
    public event Action Closing;

    ItemSlotBinder binder, binderSel;
    object okButton, list, countText, noticeText;
    readonly object[] slots = new object[SelectItemBoxState.SlotCount];
    // the handlers are held: the host keeps no strong reference to a delegate it was given
    Func<object[], object> okHandler, clickHandler, closeHandler;

    /// <summary>vfunc_26 (@0x140f9a140): the binds released, the members dropped.</summary>
    public override void Close()
    {
        binder?.Clear();
        binderSel?.Clear();
        okButton = list = countText = noticeText = null;
        Array.Clear(slots);
        State.Rewards.Clear();
        base.Close();
        Closing?.Invoke();
    }

    /// <summary>OnLoadComplete (@0x140f99550, :114).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_close");
        var item = FindItem(ItemUid);
        if (item == null) return;
        binder = new ItemSlotBinder(Model, TypeName) { IconType = IconType };
        binderSel = new ItemSlotBinder(Model, TypeName + "_Sel") { IconType = IconType };
        okButton = Host.Child("m_okButton");
        if (okButton != null)
        {
            okHandler = _ => { OnOk(); return null; };
            Host.Invoke(okButton, "addEventListener", "buttonClick", okHandler, false, 0, false);
        }
        list = Host.Child("m_selectItemList");
        if (list != null)
        {
            clickHandler = a => { OnItemClick(a); return null; };
            Host.Invoke(list, "addEventListener", "itemClick", clickHandler, false, 0, false);
        }
        for (int i = 0; i < slots.Length; i++) slots[i] = Host.Child("m_selectedSlot" + i.ToString(CultureInfo.InvariantCulture));
        countText = Host.Child("m_selectedCount");
        noticeText = Host.Child("m_selectNotice");
        int dropBoxId = 0;
        // the xml-parameter branch (m_props) is not ported: see the class doc
        State.SelectCount = item.Data.FunctionArgInt(0, 0);
        dropBoxId = item.Data.FunctionArgInt(1, 0);
        if (dropBoxId == 0 || State.SelectCount == 0) return;
        if (!Model.HasLocalPlayer) return;
        State.Rewards.Clear();
        State.Rewards.AddRange(GetBoxItems(dropBoxId));
        binder.Clear();
        int rows = Math.Max(State.Rewards.Count, SelectItemBoxState.MinRows);
        Host.Invoke(list, "resetRenderer");
        var dp = Host.Get(list, "dataProvider");
        Host.Invoke(dp, "cleanUp");
        for (int i = 0; i < rows; i++)
        {
            if (i >= State.Rewards.Count) { Host.Invoke(dp, "push", Host.Obj()); continue; }
            var r = State.Rewards[i];
            string bind = binder.BindNewItem(r.ItemId, r.Rank, r.Count);
            if (bind.Length > 0) Host.Invoke(dp, "push", Host.Obj(("bindID", bind)));
        }
        Host.Invoke(list, "invalidate");
        Host.Invoke(list, "validateNow");
        bool next = SelectItemBoxState.OkIsNext(item, HaveCount(item.Data.Id));
        Host.Set(okButton, "label", Model.Tables.Common(next ? "s_selectItemBox_select_next" : "s_selectItemBox_select_ok") ?? "");
        Refresh();
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the child's click handler runs the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("select item box dialog: " + name); return; }
        closeHandler = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", closeHandler, false, 0, false);
    }

    List<uint> Selected()
    {
        if (SelectedOverride != null) return SelectedOverride();
        var r = new List<uint>();
        if (list == null) return r;
        var arr = Host.Invoke(list, "getSelectedList");
        if (arr is AsArray a) foreach (var v in a.Items) r.Add(Rt.ToUint32(v));
        return r;
    }

    int SelectedCount() => SelectedOverride != null ? SelectedOverride().Count : (int)Rt.ToUint32(Host.Invoke(list, "getSelectedCount"));

    /// <summary>FUN_140f9a230: OK.</summary>
    public void OnOk()
    {
        var item = FindItem(ItemUid);
        if (item == null) return;
        switch (State.Ok(Selected(), out string indices))
        {
            case SelectItemBoxState.OkKind.Wrong: NoticeKey?.Invoke("s_selectItemBox_select_wrong"); return;
            case SelectItemBoxState.OkKind.Ignored: return;
        }
        OpenContents(item, indices);
        Close();
    }

    /// <summary>FUN_140f9a5f0: a row was clicked.</summary>
    void OnItemClick(object[] args)
    {
        if (State.TooMany(SelectedCount()))
        {
            var ev = args is { Length: > 0 } ? args[0] : null;
            var row = ev == null ? null : Host.Get(ev, "itemRenderer");
            if (row != null) Host.Set(row, "selected", false);
            NoticeKey?.Invoke("s_selectItemBox_cant_select_more");
        }
        else Refresh();
    }

    /// <summary>FUN_140f9a700 (@0x140f9a700, :251): the notice or the count and the picked slots; OK only with all picked.</summary>
    public void Refresh()
    {
        if (FindItem(ItemUid) == null) return;
        var selected = Selected();
        if (selected.Count == 0)
        {
            Host.SetVisible(countText, false);
            foreach (var s in slots) Host.SetVisible(s, false);
            Host.SetVisible(noticeText, true);
            Host.Set(noticeText, "text", ClientFormat.Format(Model.Tables.Common("s_selectItemBox_select_notice") ?? "", State.SelectCount));
        }
        else
        {
            Host.SetVisible(noticeText, false);
            Host.SetVisible(countText, true);
            Host.Set(countText, "text", ClientFormat.Format(Model.Tables.Common("s_selectItemBox_select_itemCount") ?? "", selected.Count, State.SelectCount));
            binderSel.Clear();
            for (int i = 0; i < slots.Length; i++)
            {
                if (i >= selected.Count)
                {
                    Host.Invoke(slots[i], "setData", "");
                    Host.SetVisible(slots[i], false);
                    continue;
                }
                uint idx = selected[i];
                if (idx >= State.Rewards.Count) continue;
                Host.SetVisible(slots[i], true);
                var r = State.Rewards[(int)idx];
                Host.Invoke(slots[i], "setData", binderSel.BindNewItem(r.ItemId, r.Rank, r.Count));
            }
        }
        Host.Set(okButton, "enabled", selected.Count == State.SelectCount);
    }
}
