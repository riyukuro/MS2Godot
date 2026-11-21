using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The bank window, UIStoreDialog (client/ms2/uistatuseventlistener.cpp, UI id 0x4e47, type name "StoreDialog", movie
/// uistoredialog) with its StoreSlotBag of StoreSlots (storeslot.cpp), named "StoreDialog.%d". The store itself is
/// <see cref="StorageBook"/> (CStore, store.cpp, global DAT_143ab2d80); this controller shows it and keeps the window in
/// step with its events: the bag is rebuilt (FUN_141b110e0) and the "m_expand" text refreshed (UpdateExpandText) from
/// <see cref="Process"/> by the pending bits m_198 (4 = bag, 2 = expand text), as vfunc_15 does.
/// <para>This is the show-and-fill half. The sends (RequestLoad, RequestSort and SendNpcTalkMode0 on close, deposit,
/// withdraw, expand, sort, and the slot drag / right-click paths) are not ported here: their callbacks raise
/// <see cref="Button"/> and send nothing.</para>
/// <para>STILL-OPEN: vfunc_il13 (m_198 = 6) is mapped to the ready event; the platform dictionary check (vfunc_37) that
/// gates the search is taken as 1; the open / close sounds; the search candidate list (UpdateSearchCandidates).</para>
/// </summary>
public sealed class StoreDialogController : UiController, IDragSlotOwner
{
    public override string Movie => "uistoredialog";
    protected override string EntityMethod => "initData";
    /// <summary>GetTypeName.</summary>
    public override string TypeName => "StoreDialog";

    public const int UiId = 0x4e47;
    public const string Bag = "StoreDialog";
    /// <summary>The flags StoreSlot::vfunc_8 (@0x14180ab10) builds its ASSlot(2) with.</summary>
    public const int SlotFlags = 0x1c01f;
    /// <summary>m_198 bits: 2 = the expand text, 4 = the bag.</summary>
    public const int PendingText = 2, PendingBag = 4;

    public StorageBook Book;
    /// <summary>A callback whose client path is not ported: raised by its name, nothing is sent.</summary>
    public event Action<string> Button;
    /// <summary>The dialog closed.</summary>
    public event Action Closed;
    /// <summary>The inventory window (0x4e2d) when it is open, else null.</summary>
    public Func<UiController> OpenInventory = () => null;
    /// <summary>vfunc_66 with the inventory closed: the "openWindowPos" message to the inventory with this id.</summary>
    public event Action<int> InventoryWindowPos;
    /// <summary>FUN_141b109a0 on an expansion: s_strExpandBankSlot.</summary>
    public event Action ExpandEffect;
    /// <summary>ShowMessageBox(text, type, 0, "").</summary>
    public Action<string, int> ShowBox = (t, type) => MessageBoxQueue.ShowMessageBox(t, type, null, "");

    int pending;                 // m_198
    string searchText = "";      // m_1a0
    object expandText, expandEffect;
    bool closing;

    /// <summary>The pending bits (m_198).</summary>
    public int Pending => pending;

    public static string SlotName(int i) => Bag + "." + i.ToString(CultureInfo.InvariantCulture);

    static bool ParseSlot(object name, out int index)
    {
        index = -1;
        return name is string n && n.StartsWith(Bag + ".", StringComparison.Ordinal)
            && int.TryParse(n.AsSpan(Bag.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
    }

    static string Group(long n) => NumberText.Group(n.ToString(CultureInfo.InvariantCulture));

    // ---- load / events -----------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete: m_198 = 6 and initData(m_188, the bank's meso); the status and item listeners register.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Book == null) return;
        pending = PendingText | PendingBag;
        Book.ItemAdded += OnItemChanged;
        Book.ItemRemoved += OnItemChanged;
        Book.ItemChanged += OnItemChanged;
        Book.ListLoaded += OnList;
        Book.BecameReady += OnList;
        Book.MesoChanged += OnMeso;
        Book.Expanded += OnExpanded;
        Book.CloseReceived += OnCloseReceived;
        Host.Invoke("initData", new object[] { Bag, Group(Book.Meso) });
    }

    void Unsubscribe()
    {
        if (Book == null) return;
        Book.ItemAdded -= OnItemChanged;
        Book.ItemRemoved -= OnItemChanged;
        Book.ItemChanged -= OnItemChanged;
        Book.ListLoaded -= OnList;
        Book.BecameReady -= OnList;
        Book.MesoChanged -= OnMeso;
        Book.Expanded -= OnExpanded;
        Book.CloseReceived -= OnCloseReceived;
    }

    /// <summary>vfunc_9 / vfunc_10 (an item added / removed or changed): the slot pushed again and m_198 |= 2.</summary>
    void OnItemChanged(EquipItem e)
    {
        pending |= PendingText;
        if (Host?.Entity != null) Host.Push(SlotName(e.InventorySlot));
    }

    /// <summary>The list loaded / the store ready (vfunc_il13): m_198 = 6.</summary>
    void OnList() => pending = PendingText | PendingBag;

    /// <summary>vfunc_oa0_13: Invoke("changeMoney", the grouped meso).</summary>
    void OnMeso(long meso)
    {
        if (Host?.Entity != null) Host.Invoke("changeMoney", new object[] { Group(meso) });
    }

    /// <summary>FUN_141b109a0: shown, m_198 = 6 and "m_expandEff" plays; the expand sound.</summary>
    void OnExpanded()
    {
        if (Host?.Entity == null) return;
        pending = PendingText | PendingBag;
        expandEffect ??= Host.Child("m_expandEff");
        if (expandEffect != null)
        {
            Host.SetVisible(expandEffect, true);
            Host.Invoke(expandEffect, "gotoAndPlay", 1);
        }
        ExpandEffect?.Invoke();
    }

    void OnCloseReceived() => Close();

    /// <summary>
    /// vfunc_15, once per frame: while open and m_198 is set, bit 4 rebuilds the bag (FUN_141b110e0: Invoke("updateBag"))
    /// and bit 2 refreshes the "m_expand" text (UpdateExpandText). Nothing is allocated while no bit is set.
    /// </summary>
    public void Process()
    {
        if (pending == 0 || closing || Host?.Entity == null || Book == null) return;
        if ((pending & PendingBag) != 0)
        {
            pending &= ~PendingBag;
            Host.Invoke("updateBag");
        }
        if ((pending & PendingText) != 0)
        {
            pending &= ~PendingText;
            expandText ??= Host.Child("m_expand");
            if (expandText != null)
            {
                Host.Set(expandText, "text", Book.Count.ToString(CultureInfo.InvariantCulture) + "/" + Book.Capacity.ToString(CultureInfo.InvariantCulture));
                Host.Invoke(expandText, "invalidate");
            }
        }
    }

    /// <summary>vfunc_66 (@0x141b0ef40): the inventory open, the two side by side (FUN_141275c30); closed, the
    /// "openWindowPos" message with this id.</summary>
    public void PlaceBesideInventory()
    {
        if (Host?.Entity == null) return;
        var place = OpenPosition();
        if (OpenInventory() is { } inv) PlaceSideBySide(this, place, inv);
        else InventoryWindowPos?.Invoke(UiId);
    }

    /// <summary>vfunc_67 (@0x141b0f0c0): the listeners dropped and UIDialog's close; m_198 = 0, the search text cleared.
    /// STILL-OPEN: SendNpcTalkMode0 and RequestSort are sends (item 2).</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        Unsubscribe();
        base.Close();
        pending = 0;
        searchText = "";
        expandText = expandEffect = null;
        Closed?.Invoke();
    }

    // ---- callbacks ---------------------------------------------------------------------------------------------------

    /// <summary>vfunc_28: the search button (OnItemSearch) and the list pick (OnSelected) scroll; the buttons that send
    /// raise <see cref="Button"/>.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onitemsearch":
            case "onselected":
                ScrollToItem(args.Length > 0 ? Rt.ToString(args[0]) : "");
                return true;
            case "onclickdeposit":
            case "onclickwithdraw":
            case "onclickexpandslot":
            case "onclicksort":
            case "notifyslotuse":
            case "notifyslotshiftclick":
                Button?.Invoke(name);
                return true;
        }
        return false;
    }

    /// <summary>
    /// ScrollToItem: m_1a0 = the text; the bag's FindSlotIndexByItemName; found, Invoke("moveScroll", index); not found,
    /// the search input emptied and the box s_store_no_item (Common 0x937).
    /// </summary>
    public void ScrollToItem(string text)
    {
        searchText = text ?? "";
        int index = FindSlotIndexByItemName(searchText);
        if (index > -1)
        {
            Host.Invoke("moveScroll", index);
            return;
        }
        var input = Host.Child("m_inputSearch");
        if (input != null) Host.Set(input, "text", "");
        ShowBox(Model.Tables.Common(0x937) ?? "", 0);
    }

    static string Normalize(string s) => s.Trim().ToLowerInvariant();

    /// <summary>StoreSlotBag search: each slot's updateIconSearchOn is refreshed and the lowest matching slot index is
    /// returned (-1 for none, and for empty text).</summary>
    public int FindSlotIndexByItemName(string text)
    {
        string key = Normalize(text);
        int best = -1;
        if (key.Length == 0 || Book == null) return best;
        for (int i = 0; i < Book.SlotCount; i++)
        {
            var e = Book.Slot(i);
            if (e == null || Normalize(Model.Tables.ItemName(e.Id) ?? "") != key) continue;
            if (best < 0) best = i;
            Host.Push(SlotName(i));
        }
        return best;
    }

    // ---- the slots (StoreSlot) ---------------------------------------------------------------------------------------

    public override IEnumerable<string> GetBagSlotBindings(string bag)
    {
        if (bag != Bag || Book == null) return Array.Empty<string>();
        return Enumerable.Range(0, Book.Capacity).Select(SlotName);
    }

    /// <summary>StoreSlot::vfunc_8 @0x14180ab10: none without an item; ASSlot(2) built with flags 0x1c01f and the bool
    /// updateIconSearchOn (the search text equals the item's lowercased name).</summary>
    public override object GetBinding(string name)
    {
        if (!ParseSlot(name, out int i)) return base.GetBinding(name);
        var e = Book?.Slot(i);
        if (e == null || Model == null) return null;
        var item = InventoryController.ToItem(e, Model.Tables.Items);
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources)) return null;
        slot.Add(("updateIconSearchOn", searchText.Length > 0 && Normalize(searchText) == Normalize(Model.Tables.ItemName(e.Id) ?? "")));
        return Host.Obj(slot.ToArray());
    }

    /// <summary>StoreSlot::vfunc_0 @0x14180a970: ASTooltip(the local player, 0x80, 2); enablePuton Common 0x754; the preview.</summary>
    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!ParseSlot(name, out int i)) return base.GetSlotTooltip(name);
        var e = Book?.Slot(i);
        var tip = new ItemTooltip(Model, 0x80) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        if (e == null) return new AsTooltip();
        var item = InventoryController.ToItem(e, Model.Tables.Items);
        if (!tip.Fill(item)) return new AsTooltip();
        tip.SetEnablePuton(Model.Tables.Common(0x754));
        tip.SetEnablePreview(item);
        return tip.Data;
    }

    // ---- IDragSlotOwner ----------------------------------------------------------------------------------------------

    public bool IsDragSlot(string name) => ParseSlot(name, out _);

    public void RefreshSlot(string name)
    {
        if (ParseSlot(name, out _)) Host.Push(name);
    }

    public void SlotDragStarted(string name) { }

    /// <summary>STILL-OPEN: StoreSlot::vfunc_18 (a drop onto the bank) is a send (item 2); the source slot is refreshed.</summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) => sourceOwner?.RefreshSlot(source);

    public void SlotDropUnresolved(string name) => RefreshSlot(name);
}
