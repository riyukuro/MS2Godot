using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;

namespace Ms2.Ui.Items;

/// <summary>
/// CItemBreakManager (client commonfiles/itembreakmanager.cpp): the item dismantle list (100 slots), the checks before each add or
/// remove, the sends of SendOp ItemDismantle and OnItemDismantlePacket's changes. The dialogs (UIItemBreakDialog 0x4e8d, the
/// donation dialog 0x4e8e) are the host's; they are reached through the events and <see cref="DialogOpen"/>.
/// STILL-OPEN (injected, default as shown): the break ingredient table (<see cref="HasIngredients"/>, no entry), the skin reward
/// calculation (<see cref="SkinHasReward"/>), the gem breakable check (<see cref="GemsBreakable"/>), the content shutdown 0x71
/// and the JP socket rule (not ported), FUN_141902660 (<see cref="BlockAdd"/>), the high SA option test (<see cref="HighSaOption"/>).
/// </summary>
public sealed class ItemBreakManager
{
    public const int Slots = 100;
    /// <summary>The notice ids ShowSystemNotice gets: refused removal / add of the break type, the donation type, a result 3,
    /// and the unknown result (the code is the argument).</summary>
    public const int NoticeBreak = 0x8f6, NoticeDonation = 0x8f7, NoticeResult3 = 0x43, NoticeUnknown = 0x8f8;
    /// <summary>The message box type of the count input (5) and of the high SA option confirm (0x11).</summary>
    public const int CountBoxType = 5, HighSaBoxType = 0x11;

    readonly long[] uids = new long[Slots];
    readonly uint[] counts = new uint[Slots];
    readonly HashSet<long> pending = new();

    /// <summary>m_type: 0 closed, 1 the break dialog, 2 the donation dialog.</summary>
    public byte Type { get; private set; }

    // ---- the host -----------------------------------------------------------------------------------------------------
    public Func<long, Item> Find { get; set; } = _ => null;
    /// <summary>The inventory holds the uid, without building an Item (the per-frame RemoveMissingItems asks it). Null: Find is used.</summary>
    public Func<long, bool> Exists { get; set; }
    bool InInventory(long uid) => Exists != null ? Exists(uid) : Find(uid) != null;
    public Action<IOutPacket> Send { get; set; }
    /// <summary>GetStringCommonByKey: the common table's text of a key (itembreakmanager.cpp:436, :458, :467).</summary>
    public Func<string, string> Text { get; set; } = k => k;
    public Func<int, string> Common { get; set; } = _ => "";
    public Func<long> ServerTime { get; set; } = () => 0;
    /// <summary>ShowSystemNotice of a common string id; the argument is int.MinValue when there is none.</summary>
    public Action<int, int> NoticeId { get; set; }
    /// <summary>ShowSystemNotice of a string key.</summary>
    public Action<string> NoticeKey { get; set; }
    public Action<MessageBoxData> ShowBox { get; set; }
    public Func<MessageBoxData, string> InputText { get; set; } = _ => "";
    public Action<string> Sound { get; set; }
    /// <summary>The item's display name.</summary>
    public Func<Item, string> ItemName { get; set; } = _ => "";
    /// <summary>The uid of the summoned pet (FUN_140531630): 0 when none.</summary>
    public Func<long> SummonedPetUid { get; set; } = () => 0;
    /// <summary>ItemLockManager.CanUseItem: false refuses (it shows its own message box).</summary>
    public Func<Item, bool> CanUseItem { get; set; } = _ => true;
    /// <summary>The break ingredient table has an entry for the item id (HasBreakReward's skin test).</summary>
    public Func<int, bool> HasIngredients { get; set; } = _ => false;
    /// <summary>The skin reward calculation finds a reward (FUN_140633bf0's skin side).</summary>
    public Func<Item, bool> SkinHasReward { get; set; } = _ => false;
    /// <summary>The socket gems may be broken (CheckGemsBreakable).</summary>
    public Func<Item, bool> GemsBreakable { get; set; } = _ => true;
    /// <summary>Feature 0x3a (skins).</summary>
    public Func<bool> SkinFeature { get; set; } = () => false;
    /// <summary>FUN_141902660 != 0: the item is not added.</summary>
    public Func<Item, bool> BlockAdd { get; set; } = _ => false;
    /// <summary>FUN_1409e4950: the option text of an item with a high SA option, null without.</summary>
    public Func<Item, string> HighSaOption { get; set; } = _ => null;
    /// <summary>FUN_140531510 == 1: the item is a gacha box that can be dismantled for its coin (the path that skips the other tests).</summary>
    public Func<Item, bool> GachaBox { get; set; } = _ => false;
    /// <summary>The client's PC bang item, FUN_140635870: the item's lock owner refuses.</summary>
    public Func<Item, bool> TransferLocked { get; set; } = _ => false;
    /// <summary>IsBreakDialogOpen's dialog half: 0x4e8d or 0x4e8e exists.</summary>
    public bool DialogOpen { get; set; }
    /// <summary>The break dialog's m_188: a break is in progress.</summary>
    public bool BreakInProgress { get; set; }
    /// <summary>The dialog is open and vfunc_86 (not breaking, the bag visible) (FUN_140532ab0).</summary>
    public bool CanSendOptions { get; set; }

    // ---- what the dialogs show ----------------------------------------------------------------------------------------
    public event Action<int> SlotChanged;            // UpdateActiveDialogSlot(index)
    public event Action<long> InventoryRefresh;      // the inventory slot of the uid
    public event Action<ItemDismantlePacket> ResultForwarded;   // mode 3: dialog vfunc_84 (rewards)
    public event Action<ItemDismantlePacket> PreviewReceived;   // mode 5
    public event Action RefreshActive;               // FUN_140532220: vfunc_85(true, 0), after a mode 4 result code
    public event Action RefreshDialog;               // RefreshActiveDialog: the bag bound again and vfunc_85(true, 0), after a mode 3
    public event Action ResetBreak;                  // mode 4 with a break in progress
    public event Action<int> ShopCoinRefresh;        // mode 3 type 1: shop 0x4e3b UpdateGachaCoin (ShopController.FollowItemBreak)

    public long Uid(int index) => (uint)index < Slots ? uids[index] : 0;
    public uint Count(int index) => (uint)index < Slots ? counts[index] : 0;
    public bool Contains(long uid) => uid != 0 && Array.IndexOf(uids, uid) >= 0;

    /// <summary>HasBreakItems: a slot holds an item that is still in the inventory.</summary>
    public bool HasBreakItems
    {
        get { for (int i = 0; i < Slots; i++) if (uids[i] != 0 && InInventory(uids[i])) return true; return false; }
    }

    /// <summary>GetMaxItemRarity: the highest rarity of the listed items found in the inventory.</summary>
    public int MaxItemRarity
    {
        get
        {
            int r = 0;
            for (int i = 0; i < Slots; i++) if (uids[i] != 0 && Find(uids[i]) is { } it && it.Rarity > r) r = it.Rarity;
            return r;
        }
    }

    /// <summary>GetLastItemIndex: the index of the last filled slot, 0 without.</summary>
    public int LastItemIndex
    {
        get { for (int i = Slots - 1; i >= 0; i--) if (uids[i] != 0) return i; return 0; }
    }

    // ---- the checks ---------------------------------------------------------------------------------------------------

    /// <summary>FUN_140528e20: locked (state 1) or unlocking (state 0 before its unlock time).</summary>
    bool IsLocked(Item it) => it.LockState == 1 || (it.LockState == 0 && ServerTime() < it.UnlockTime);

    /// <summary>CItemTransfer::CanBreak (itemsocketsystemutil.cpp:2993).</summary>
    bool TransferCanBreak(Item it)
    {
        var tr = it.Record?.Transfer;
        if (tr != null && tr.BreakDisabled) return false;
        if (it.Data.PCBang) return false;
        if (TransferLocked(it)) return false;
        if (it.Data.HasSkinType && !SkinFeature()) return false;
        return true;
    }

    /// <summary>HasBreakReward: true without a skin; a skin needs an ingredient table entry or the skin reward calculation.</summary>
    bool HasBreakReward(Item it) => !it.Data.HasSkinType || HasIngredients(it.ItemId) || SkinHasReward(it);

    /// <summary>CanBreakItem (@0x140531270).</summary>
    public bool CanBreakItem(Item it)
    {
        if (it == null || IsLocked(it)) return false;
        if (GachaBox(it)) return true;
        long summon = SummonedPetUid();
        if (summon != 0 && summon == it.Uid) return false;
        if (!TransferCanBreak(it)) return false;
        return HasBreakReward(it);
    }

    /// <summary>CanAddItem: breakable and not listed yet.</summary>
    public bool CanAddItem(Item it) => CanBreakItem(it) && !Contains(it.Uid);

    // ---- sends --------------------------------------------------------------------------------------------------------

    /// <summary>SendItemDismantleMode0: the type is set and the list and pending cleared before the send.</summary>
    public void SendMode0(byte type)
    {
        Type = type;
        pending.Clear();
        Array.Clear(uids);
        Array.Clear(counts);
        Send?.Invoke(ItemDismantleRequest.SetMode(type));
    }

    /// <summary>ResetBreakMode: Mode0(0).</summary>
    public void ResetBreakMode() => SendMode0(0);

    /// <summary>SendItemDismantleMode3: the start of the break.</summary>
    public bool SendMode3() { Send?.Invoke(ItemDismantleRequest.Start()); return true; }

    /// <summary>SendItemDismantleMode6: the bundle register, only with the dialog open and idle (FUN_140532ab0).</summary>
    public void SendMode6(byte a, byte b)
    {
        if (!CanSendOptions) return;
        Send?.Invoke(ItemDismantleRequest.Options(a, b));
    }

    void SendMode1(long uid, int count, int arg) => Send?.Invoke(ItemDismantleRequest.AddItem(uid, count, arg));

    // ---- add / remove -------------------------------------------------------------------------------------------------

    /// <summary>TryAddItem (@0x140532de0): the refusals in the client's order; 1 when the item went on to AddItemWithConfirm.</summary>
    public int TryAddItem(Item it, int arg)
    {
        if (it == null) return 0;
        if (!CanUseItem(it)) return 0;
        if (Contains(it.Uid)) { NoticeKey?.Invoke("s_itembreak_selected_item"); return 0; }
        if (!GemsBreakable(it))
        {
            var box = MessageBoxQueue.Create(0, Text("s_itembreak_usedgemstone_item"), null, "");
            ShowBox?.Invoke(box);
            return 0;
        }
        if (Type == 2)
        {
            if (!CanBreakItem(it)) { NoticeId?.Invoke(NoticeDonation, int.MinValue); return 0; }
            if (!it.Data.IsEquip && !it.Data.HasSkinType) { NoticeId?.Invoke(NoticeDonation, int.MinValue); return 0; }   // FUN_14053cbc0 || HasSkin
        }
        else if (!CanBreakItem(it)) { NoticeId?.Invoke(NoticeBreak, int.MinValue); return 0; }
        AddItemWithConfirm(it, arg);
        return 1;
    }

    /// <summary>AddItemWithConfirm: a stack asks the count, a high SA option item asks to confirm, else mode 1.</summary>
    void AddItemWithConfirm(Item it, int arg)
    {
        if (BlockAdd(it)) return;
        if (it.Data.IsStackable && it.Count > 1)
        {
            var box = MessageBoxQueue.Create(CountBoxType, Text("s_msg_item_break"), null, "");
            box.SetInput(1, it.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), (int)it.Count, 0, 0);
            box.Callback = OnCountBox;
            box.Append(arg);
            box.Append(it.Uid);
            box.Append(it.Count);
            ShowBox?.Invoke(box);
        }
        else if (it.Rarity >= 5 && HighSaOption(it) is { } option)
        {
            var box = MessageBoxQueue.Create(HighSaBoxType, option + "\n" + Text("s_itembreak_high_sa_option_item"), null, "");
            box.Callback = OnConfirmBox;
            box.ItemUid = it.Uid;
            box.SetItemEntry(it, ItemName(it), 1, (int)it.Count, (int)it.Count, 0, 0, null);
            box.SetButtonLabelIds(0x475, 0x476, 0);
            box.Append(arg);
            box.Append(it.Uid);
            box.Append(it.Count);
            ShowBox?.Invoke(box);
        }
        else SendMode1(it.Uid, (int)it.Count, arg);
    }

    /// <summary>OnDismantleCountMsgBox: the typed count (_wtoi, the commas dropped) must be 1..max, else the count notice 0xee.</summary>
    bool OnCountBox(int button, MessageBoxData box)
    {
        if (button != 0) return true;
        string typed = (InputText?.Invoke(box) ?? "").Replace(",", "").Trim();
        int count = MessageBoxData.WToI(typed);
        int max = box.ArgInt(2, 0);
        if (count == 0 || count > max)
        {
            NoticeId?.Invoke(0xee, max);
            return false;
        }
        SendMode1(box.ArgLong(1, 0), count, box.ArgInt(0, -1));
        return true;
    }

    /// <summary>OnConfirmMessageBoxResult: yes sends mode 1 with the arguments the box kept.</summary>
    bool OnConfirmBox(int button, MessageBoxData box)
    {
        if (button == 0) SendMode1(box.ArgLong(1, 0), box.ArgInt(2, 1), box.ArgInt(0, -1));
        return true;
    }

    /// <summary>RequestRemoveItem: not breakable gives the notice, else mode 2.</summary>
    public int RequestRemoveItem(Item it)
    {
        if (it == null) return 0;
        if (!CanBreakItem(it)) { NoticeId?.Invoke(NoticeBreak, int.MinValue); return 0; }
        Send?.Invoke(ItemDismantleRequest.RemoveItem(it.Uid));
        return 1;
    }

    /// <summary>RemoveMissingItems (per frame while the dialog is up): each listed uid gone from the inventory is removed once.</summary>
    public void RemoveMissingItems()
    {
        for (int i = 0; i < Slots; i++)
        {
            long uid = uids[i];
            if (uid == 0 || InInventory(uid) || pending.Contains(uid)) continue;
            Send?.Invoke(ItemDismantleRequest.RemoveItem(uid));
            pending.Add(uid);
        }
    }

    // ---- OnItemDismantlePacket ----------------------------------------------------------------------------------------

    /// <summary>OnItemDismantlePacket (@0x140533...): ignored unless a break dialog is open.</summary>
    public void OnPacket(ItemDismantlePacket p)
    {
        if (Type == 0 || !DialogOpen) return;
        switch (p.Mode)
        {
            case 1:
            case 6:
                bool any = false, found = false;
                foreach (var s in p.Slots ?? Array.Empty<DismantleSlot>())
                {
                    if (s.Index >= Slots) return;
                    if (InInventory(s.Uid))
                    {
                        uids[s.Index] = s.Uid;
                        counts[s.Index] = (uint)s.Amount;
                        SlotChanged?.Invoke(s.Index);
                        found = true;
                    }
                    InventoryRefresh?.Invoke(s.Uid);
                    any = true;
                }
                if (p.Mode == 1 ? found : any) Sound?.Invoke("Extract_ItemChoice");
                break;
            case 2:
                int i = Array.IndexOf(uids, p.Uid);
                if (i >= 0) { uids[i] = 0; counts[i] = 0; }
                pending.Remove(p.Uid);
                if (i >= 0) SlotChanged?.Invoke(i);
                InventoryRefresh?.Invoke(p.Uid);
                break;
            case 3:
                if (p.Type != Type) return;
                Array.Clear(uids);
                Array.Clear(counts);
                ResultForwarded?.Invoke(p);
                if (p.Type == 1) ShopCoinRefresh?.Invoke(0);
                RefreshDialog?.Invoke();
                break;
            case 4:
                switch (p.Result)
                {
                    case 1: NoticeId?.Invoke(NoticeBreak, int.MinValue); break;
                    case 2: NoticeId?.Invoke(NoticeDonation, int.MinValue); break;
                    case 3: NoticeId?.Invoke(NoticeResult3, int.MinValue); break;
                    case 4: break;
                    case 5: NoticeKey?.Invoke("s_itembreak_empty"); break;
                    default: NoticeId?.Invoke(NoticeUnknown, p.Result); break;
                }
                if (BreakInProgress) ResetBreak?.Invoke();
                RefreshActive?.Invoke();
                break;
            case 5:
                PreviewReceived?.Invoke(p);
                break;
        }
    }
}
