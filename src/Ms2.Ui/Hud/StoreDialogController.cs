using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The bank window, UIStoreDialog (client/ms2/uistatuseventlistener.cpp, UI id 0x4e47, type name "StoreDialog", movie
/// uistoredialog) with its StoreSlotBag of StoreSlots (storeslot.cpp), named "StoreDialog.%d". The store itself is
/// <see cref="StorageBook"/> (CStore, store.cpp, global DAT_143ab2d80); this controller shows it and keeps the window in
/// step with its events: the bag is rebuilt (FUN_141b110e0) and the "m_expand" text refreshed (UpdateExpandText) from
/// <see cref="Process"/> by the pending bits m_198 (4 = bag, 2 = expand text), as vfunc_15 does.
/// <para>The sends go through <see cref="Sender"/> (CStore's requests and the client's checks before them): RequestLoad
/// when the window opens on a store not ready, RequestSort on close, the meso and expand buttons, and the drops and
/// right-clicks. Nothing changes here before the server answers.</para>
/// <para>STILL-OPEN: SendNpcTalkMode0 on close; the count boxes (ShowUseStoreCountInput / ShowSwapStoreCountInput) of a
/// stack the client asks about: such an item is not sent; StoreSlot::vfunc_26's binding boxes and CItem::FUN_14052a580
/// (the right-click withdraw and its bag slot search FUN_140a612b0): the withdraw path is not sent; FUN_141275260(0x42).</para>
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
    /// <summary>CStore's requests with the client's checks (see <see cref="StorageSender"/>); null sends nothing.</summary>
    public StorageSender Sender;
    /// <summary>The expand price item's number as FUN_14028dee0(0x2a8, DAT_143ab2d80.Value4500(), 6) formats it.</summary>
    public Func<long> ExpandPrice = () => 0;
    /// <summary>FUN_141275260(id, 1, 1): a system notice of a resolved text.</summary>
    public event Action<string> NoticeText;
    /// <summary>The box is shown (FUN_140d73c60).</summary>
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>The server time (the item lock's clock).</summary>
    public Func<long> Now = () => 0;
    /// <summary>The keys DIK 0x2a / 0x36: either Shift is down.</summary>
    public Func<bool> ShiftHeld = () => false;
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
    /// <summary>UITalkManager::GetTalking() != 0, and SendNpcTalkMode0.</summary>
    public Func<bool> IsTalking;
    public Action EndTalk;
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
        Book.CloseReceived += OnExpandNotice;     // type 7 (OnClose) is the expand notice; type 0xd (OnExtend) raises no dialog event
        Host.Invoke("initData", new object[] { Bag, Group(Book.Meso) });
        if (!Book.Ready) Sender?.RequestLoad();     // OnLoadInit (@0x141b0f430): a store not ready is asked for
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
        Book.CloseReceived -= OnExpandNotice;
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
    void OnExpandNotice()
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
    /// SendNpcTalkMode0 first when a talk is open (:541), then CStore::RequestSort (:543).</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        Unsubscribe();
        if (IsTalking?.Invoke() == true) EndTalk?.Invoke();   // vfunc_67 :541: SendNpcTalkMode0 while a talk is open, before the sort
        Sender?.RequestSort();
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
                AskMesos(true);
                return true;
            case "onclickwithdraw":
                AskMesos(false);
                return true;
            case "onclickexpandslot":
                AskExpand();
                return true;
            case "onclicksort":
                Sender?.RequestClose();           // vfunc_28 :733, CStore::RequestClose (mode 8)
                return true;
            case "notifyslotuse":
                SlotUse(args);
                return true;
            case "notifyslotshiftclick":
                Button?.Invoke(name);
                return true;
        }
        return false;
    }

    /// <summary>The own character id (DAT_143ab2828), the binding check's.</summary>
    public Func<long> SelfId = () => 0;
    /// <summary>The bag the withdraw lands in (g_143ab2a90's tabs); null without a live inventory.</summary>
    public Func<InventoryView> InventoryBag = () => null;
    /// <summary>CStringTable::GetInventoryTabName.</summary>
    public Func<int, string> TabName = _ => "";

    /// <summary>
    /// NotifyDragSlot::SlotUse (notifydragslot.cpp:33) on a store slot: args = slot name, button. The double click (0) runs
    /// vfunc_13 (unless Shift), the right click (1) vfunc_14, both <see cref="WithdrawSlot"/>; vfunc_19 is 0 for a store slot.
    /// </summary>
    void SlotUse(object[] args)
    {
        if (args.Length < 1 || args[0] == null) return;
        int button = args.Length == 1 ? 0 : InventoryController.ArgInt(args[1]);
        if (!ParseSlot(Rt.ToString(args[0]), out int slot)) return;
        if (button == 0 && ShiftHeld()) return;
        if (button == 0 || button == 1) WithdrawSlot(slot);
    }

    /// <summary>
    /// StoreSlot::vfunc_14 (storeslot.cpp:244, @0x141808d90): the store item of the slot; the binding (CItem::FUN_14052a580: an
    /// item bound to another character: box 0xff); with the inventory window open, its tab's slot for one (the whole stack with
    /// Shift) by FUN_140a612b0 (-1: notice 0x44 with the tab name); a stack that asks no count or Shift sends the withdraw of the
    /// whole stack. A stack that asks a count opens the count box (ShowUseStoreCountInput). STILL-OPEN: vfunc_26's
    /// restriction and binding boxes, Unk_Inventory::FUN_140a661e0 (a constant 1), the error sound.
    /// </summary>
    public bool WithdrawSlot(int bankSlot)
    {
        var rec = Book?.Slot(bankSlot);
        if (rec == null || Sender == null || Model == null) return false;
        if (!StorageSender.BindingAllows(rec, SelfId()))
        {
            ShowBox(Model.Tables.Common(0xff) ?? "", 0);
            return false;
        }
        if (OpenInventory() == null || InventoryBag() is not { } bag) return true;
        int tab = bag.TabOfItem(rec.Id);
        bool all = ShiftHeld();
        int slot = bag.SlotForIncoming(tab, rec, all ? rec.Amount : 1);
        if (slot == -1)
        {
            NoticeText?.Invoke(ClientFormat.Format(Model.Tables.Common(0x44), TabName(tab)));
            return false;
        }
        var item = InventoryController.ToItem(rec, Model.Tables.Items);
        if (item != null && ItemTradeRules.AsksCount(item, Now()) && !all)
            ShowUseStoreCountInput(rec.Uid, rec.Amount, (int)item.Data.SlotMax, true, slot);
        else Sender.Withdraw(rec.Uid, slot, rec.Amount);
        return true;
    }

    public const string UseStoreInBox = "usestorein", UseStoreOutBox = "usestoreout";

    /// <summary>ShowUseStoreCountInput (@0x1414043e0, invenslot.cpp:379): a count box (type 5; Common 0x2ac out / 0x2ab in;
    /// tag usestoreout / usestorein) with the item uid and the slot; the input (mode 1) starts at the count and takes as many
    /// digits as the slot max has (FUN_1412773a0).</summary>
    public void ShowUseStoreCountInput(long uid, int count, int max, bool take, int slot)
    {
        var box = MessageBoxQueue.Create(5, Model.Tables.Common(take ? 0x2ac : 0x2ab), null, take ? UseStoreOutBox : UseStoreInBox);
        box.ItemUid = uid;
        box.InventorySlot = slot;
        box.SetInput(1, count.ToString(System.Globalization.CultureInfo.InvariantCulture), InventoryController.DigitCount(max), 0, 0);
        ShowData(box);
    }

    /// <summary>IsItemCountValid (@0x141a00180, uimessagebox.cpp:2390): a count of 0 or less or above the stack shows the
    /// notice Common 0xee (1, have). A uid of 0 or no such item passes (FUN_141a00250 / FUN_141a002b0).</summary>
    bool CountValid(EquipItem have, int count)
    {
        if (have == null || count > 0 && count <= have.Amount) return true;
        NoticeText?.Invoke(ClientFormat.Format(Model.Tables.Common(0xee), 1, have.Amount));
        return false;
    }

    /// <summary>HandleUseStoreInButton's yes (@0x1419fe240): the count checked against the bag item (FUN_141a002b0), then
    /// FUN_140de2dc0(uid, slot 0, count): the deposit request. STILL-OPEN: its score / summoned-pet refusals.</summary>
    public void UseStoreIn(long uid, int count)
    {
        if (Sender == null || Model == null) return;
        if (uid != 0 && !CountValid(InventoryBag?.Invoke()?.FindByUid(uid), count)) return;
        Sender.Deposit(uid, 0, count);
    }

    /// <summary>HandleUseStoreOutButton's yes (@0x1419fe310): the count checked against the bank item (FUN_141a00250); the
    /// item gone: nothing; the slot is FUN_140a612b0(tab, item, count), -1 the notice 0x44 with the tab name; then
    /// FUN_140de31f0 (the binding rule, then Withdraw(uid, slot, count)).</summary>
    public void UseStoreOut(long uid, int count)
    {
        if (Sender == null || Model == null || Book == null) return;
        var rec = Book.Find(uid);
        if (uid != 0 && !CountValid(rec, count)) return;
        if (rec == null || InventoryBag?.Invoke() is not { } bag) return;
        int tab = bag.TabOfItem(rec.Id);
        int slot = bag.SlotForIncoming(tab, rec, count);
        if (slot == -1) { NoticeText?.Invoke(ClientFormat.Format(Model.Tables.Common(0x44), TabName(tab))); return; }
        if (!StorageSender.BindingAllows(rec, SelfId())) { ShowBox(Model.Tables.Common(0xff) ?? "", 0); return; }
        Sender.Withdraw(uid, slot, count);
    }

    // ---- the meso and expand buttons ---------------------------------------------------------------------------------

    /// <summary>onClickDeposit / onClickWithDraw (vfunc_28 :675): with FUN_141b11490 true (store id 0, the player alive) an
    /// input box (type 7; Common 0x2a9 / 0x2aa; tag "deposit" / "withdraw") of as many characters as the grouped maximum
    /// has; its answer is <see cref="OnMesoAnswer"/>. Refused: the notice Common 0x42.</summary>
    void AskMesos(bool deposit)
    {
        if (Sender == null || Model == null) return;
        if (!Sender.CanStartMeso()) { Sender.Notify?.Invoke(new StorageErrorText(StorageErrorKind.Notice, 0x42, null)); return; }   // FUN_141275260(0x42, 1, 1)
        var box = MessageBoxQueue.Create(7, Model.Tables.Common(deposit ? 0x2a9 : 0x2aa), null, deposit ? "deposit" : "withdraw");
        long max = deposit ? Sender.DepositMax : Sender.WithdrawMax;
        box.SetInput(1, "", Group(max).Length, 0, 0);
        box.Callback = (button, b) => OnMesoAnswer(button, b.ClickedTag);
        ShowData(box);
    }

    /// <summary>UIStoreDialog::OnMessage (@0x141b0f7c0) for a message box answered yes: the tag "cmd,amount"; an empty
    /// amount does nothing; deposit / withdraw call DepositMesos / WithdrawMesos (_wtoi64: garbage is 0). True closes
    /// the box, false keeps it (the refused amount's notice is shown).</summary>
    public bool OnMesoAnswer(int button, string tag)
    {
        if (button != 0 || Sender == null) return true;
        var parts = (tag ?? "").Split(',', 2);
        if (parts.Length < 2 || parts[1].Length == 0) return true;
        long amount = Atoi64(parts[1]);
        return parts[0] switch
        {
            "deposit" => Sender.DepositMesos(amount),
            "withdraw" => Sender.WithdrawMesos(amount),
            _ => true,
        };
    }

    /// <summary>_wtoi64: leading blanks, a sign and digits; anything else ends it (0 when none).</summary>
    static long Atoi64(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) v = unchecked(v * 10 + (s[i] - '0'));
        return neg ? -v : v;
    }

    /// <summary>onClickExpandSlot (vfunc_28 :716): below the maximum a confirm box (Common 0x2a8 with the price) whose
    /// yes is ExpandSlots; at it the box of Common 0x2b0 with the current maximum.</summary>
    void AskExpand()
    {
        if (Book == null || Sender == null || Model == null) return;
        if (!Book.WithinCapacity)
        {
            var box = MessageBoxQueue.CreateWithCallback(Ms2.Quest.ClientFormat.Format(Model.Tables.Common(0x2a8), Group(ExpandPrice())), 1,
                (button, _) => { if (button == 0) Sender.ExpandSlots(); return true; });
            ShowData(box);
        }
        else ShowBox(Ms2.Quest.ClientFormat.Format(Model.Tables.Common(0x2b0), Book.CurrentCapacity ?? 0), 0);
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

    /// <summary>
    /// StoreSlot::vfunc_18 (storeslot.cpp:314). An inventory slot dropped: its item (gone from the bag: the source is
    /// pushed again); a held stack in this slot with a max stack of 1 needs room (AvailableSlot -1: notice 0xf2); an item
    /// the bank refuses: notice 0xf1; a stack the client asks a count of is not sent (STILL-OPEN); else the deposit
    /// (the store slot when this one is empty, 0 when it holds an item). A store slot dropped: onto itself nothing, else
    /// the move (mode 2). The source is pushed again either way: nothing moves until the server answers.
    /// </summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (ParseSlot(name, out int to) && Sender != null && Book != null)
        {
            if (sourceOwner is IInvenSlotOwner inv && inv.InvenSlotUid(source) is long uid)
            {
                var item = inv.FindItem(uid);
                if (item?.Record != null)
                {
                    int slot = to;
                    bool refused = false;
                    if (Book.Slot(to) != null)
                    {
                        slot = 0;
                        if (item.Data.SlotMax == 1 && Book.AvailableSlot(item.Record, (int)item.Count) == -1)
                        {
                            NoticeText?.Invoke(Model.Tables.Common(0xf2));
                            refused = true;
                        }
                    }
                    if (!refused && !Book.IsStorable(item.Record.Id))
                    {
                        NoticeText?.Invoke(Model.Tables.Common(0xf1));
                        refused = true;
                    }
                    if (!refused && !ItemTradeRules.AsksCount(item, Now())) Sender.Deposit(item.Uid, slot, (int)item.Count);
                }
            }
            else if (sourceOwner == this && ParseSlot(source, out int from)) Sender.MoveSlot(from, to);
        }
        sourceOwner?.RefreshSlot(source);
    }

    /// <summary>
    /// InvenSlot::vfunc_14 case 3 (invenslot.cpp:923), the right-click with this window at the head of the dialogs: the
    /// target slot is AvailableSlot(item, 1, or the whole stack with Shift); -1: notice 0xf2; an item the bank refuses:
    /// notice 0xf1; a stack that asks a count without Shift opens the count box (ShowUseStoreCountInput, with the bag slot); else the whole
    /// stack is deposited.
    /// </summary>
    public void DepositFromInventory(Item item)
    {
        if (item?.Record == null || Sender == null || Book == null || Model == null) return;
        bool all = ShiftHeld();
        int slot = Book.AvailableSlot(item.Record, all ? (int)item.Count : 1);
        if (slot == -1) { NoticeText?.Invoke(Model.Tables.Common(0xf2)); return; }
        if (!Book.IsStorable(item.Record.Id)) { NoticeText?.Invoke(Model.Tables.Common(0xf1)); return; }
        if (!ItemTradeRules.AsksCount(item, Now()) || all) Sender.Deposit(item.Uid, slot, (int)item.Count);
        else ShowUseStoreCountInput(item.Uid, (int)item.Count, (int)item.Data.SlotMax, false, item.Record.InventorySlot);
    }

    /// <summary>CStore::Allowed (bank, type 0), for the inventory's use mode.</summary>
    public bool Storable(Item item) => Book?.IsStorable(item.Data.Id) ?? true;

    public void SlotDropUnresolved(string name) => RefreshSlot(name);
}
