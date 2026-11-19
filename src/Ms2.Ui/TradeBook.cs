using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2.Ui;

/// <summary>
/// The trade item table, Unk_TradeItemTable (trademanagerex_1.cpp:131-228, the manager's DAT_143ab2e80): my items in the
/// trade, by uid and by slot. The slot is the item's own (CItem::GetSlot), which OnTradeItemAdded sets from the packet.
/// </summary>
public sealed class TradeItemTable
{
    readonly Dictionary<long, Item> byUid = new();
    readonly List<long> order = new();                  // the uid map's walk (GetAllItems)
    Item[] bySlot = Array.Empty<Item>();

    /// <summary>m_nSlots.</summary>
    public int SlotCount { get; private set; }

    /// <summary>Init @0x14185b1b0: 5 slots, then Reset.</summary>
    public void Init()
    {
        SlotCount = 5;
        bySlot = new Item[SlotCount];
        Reset();
    }

    /// <summary>Reset @0x14185b200: empty the uid map and every slot.</summary>
    public void Reset()
    {
        byUid.Clear();
        order.Clear();
        bySlot = new Item[SlotCount];
    }

    /// <summary>AddItem @0x14185b2a0: 0 when the uid is in already, the slot is past the last or taken; slot -1 takes the
    /// first free slot (none: 0) and sets it on the item.</summary>
    public bool AddItem(Item item)
    {
        if (byUid.ContainsKey(item.Uid)) return false;
        int slot = item.Slot;
        if (slot >= SlotCount) return false;
        if (slot != -1)
        {
            if (bySlot[slot] != null) return false;
        }
        else
        {
            int free = FindFreeSlot();
            if (free == -1) return false;
            slot = free;
            item.Slot = slot;
        }
        byUid[item.Uid] = item;
        order.Add(item.Uid);
        bySlot[slot] = item;
        return true;
    }

    /// <summary>RemoveItem @0x14185b3d0: drop the uid and clear its slot (unless the slot is past the last).</summary>
    public void RemoveItem(long uid)
    {
        if (!byUid.Remove(uid, out var item)) return;
        order.Remove(uid);
        if (item == null) return;
        int slot = item.Slot;
        if (slot >= SlotCount) return;
        bySlot[slot] = null;
    }

    /// <summary>FUN_14185b4a0: the item of a uid, or null.</summary>
    public Item Find(long uid) => byUid.TryGetValue(uid, out var item) ? item : null;

    /// <summary>GetAllItems @0x14185b520.</summary>
    public IReadOnlyList<Item> AllItems => order.Select(u => byUid[u]).ToList();

    /// <summary>FUN_14185b5c0: the first empty slot, or -1.</summary>
    public int FindFreeSlot()
    {
        for (int i = 0; i < SlotCount; i++)
            if (bySlot[i] == null) return i;
        return -1;
    }
}

/// <summary>What OnTradeStarted needs of the partner (WorldService::FUN_141bd1530(id)): PCBase::vfunc_77, the name, and
/// PC::FUN_1416612e0, the character key the dialog's profile image uses.</summary>
public sealed record TradePartner(string Name, string CharacterKey);

/// <summary>The trade dialog (UITradeDialogEx, id 0x4e83) as the manager calls it.</summary>
public interface ITradeDialog
{
    /// <summary>Unk_TradeDialog::GetPartner: the partner's name.</summary>
    string Partner { get; }
    /// <summary>vfunc_67: close the dialog.</summary>
    void Close();
    /// <summary>SetTradeSlotItem(mine, slot, item).</summary>
    void SetTradeSlotItem(bool mine, int slot, Item item);
    /// <summary>ClearTradeSlot(mine, slot).</summary>
    void ClearTradeSlot(bool mine, int slot);
    /// <summary>SetSideMeso(side, mesos).</summary>
    void SetSideMeso(int side, long meso);
    /// <summary>FUN_141b28290(mine, on): latch (on) or unlatch a side.</summary>
    void SetLatch(bool mine, bool on);
    /// <summary>FUN_141b297e0(result): a failed request's result.</summary>
    void RequestFailed(int result);
}

/// <summary>
/// The trade manager, CTradeManagerEx (client/ms2/trademanagerex_1.cpp): the partner, my side's lock and the last mesos
/// sent, the sends (SendOp::Trade 0x49, <see cref="TradeRequest"/>) and what OnTrade (@0x14185bd20) does on each Trade
/// packet (0x82, <see cref="TradeMessage"/>). The UI calls come out as hooks and events, as in <see cref="BuddyBook"/>.
/// </summary>
public sealed class TradeBook
{
    /// <summary>The trade dialog's UI id.</summary>
    public const int DialogId = 0x4e83;

    /// <summary>The trade item table (DAT_143ab2e80).</summary>
    public readonly TradeItemTable Items = new();

    /// <summary>GetStringCommon: the common string of an id.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>The common string of a key (FUN_14028dfa0's key lookup).</summary>
    public Func<string, string> CommonKey = _ => "";
    /// <summary>FUN_140710590: the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>CGameOption's refuse trade option (m_bRefuseTrade).</summary>
    public Func<bool> RefuseTrade = () => false;
    /// <summary>CBlockManager::FindBlockedAction(0x20) with GetBlockActionMessage(.., 0x20): the block's message, or null
    /// when trade is not blocked. STILL-OPEN: the block manager is not ported, so null.</summary>
    public Func<string> BlockedMessage = () => null;
    /// <summary>FUN_14151f820: a message box is up.</summary>
    public Func<bool> BoxShown = () => MessageBoxQueue.Current() != null;
    /// <summary>FUN_140d73c60: show a box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>WorldService::FUN_141bd1530: the field's player of a character id, or null.</summary>
    public Func<long, TradePartner> FindPlayer = _ => null;
    /// <summary>The item data (FUN_140523c40's template lookup).</summary>
    public ItemDataTable ItemData;
    /// <summary>CStringTable::FUN_141811980(1, id): an item's name.</summary>
    public Func<int, string> ItemName = _ => "";
    /// <summary>The trade dialog when open (Unk_InvGetDialogById(0x4e83)), else null.</summary>
    public Func<ITradeDialog> Dialog = () => null;
    /// <summary>The request box's timer seconds: Client::Constants TradeRequestDuration.</summary>
    public Func<int> RequestDuration = () => 20;

    /// <summary>FUN_141275260 / FUN_140f8ff40 (id, 1, 1), Unk_AkoMessage(id, 1, 0, x) and ShowSystemNotice: a notice of a
    /// text, its type and whether it also goes to the chat log.</summary>
    public event Action<string, int, bool> Notice;
    /// <summary>FUN_1412751b0(text, kind): a chat log line (kind 0x12).</summary>
    public event Action<string, int> ChatLine;
    /// <summary>PlaySystemSound(key).</summary>
    public event Action<string> Sound;
    /// <summary>CUIService::OpenDialog(id).</summary>
    public event Action<int> OpenDialog;
    /// <summary>CUIService::CloseDialog(id).</summary>
    public event Action<int> CloseDialog;
    /// <summary>The item event dispatcher's vfunc_17 (added, true) and vfunc_18 (removed, false) of my item.</summary>
    public event Action<Item, bool> ItemEvent;

    /// <summary>m_bConfirmed: my side is locked.</summary>
    public bool Confirmed { get; private set; }
    /// <summary>m_uidPartner.</summary>
    public long Partner { get; private set; }
    /// <summary>m_strName: the partner's name.</summary>
    public string PartnerName { get; private set; } = "";
    /// <summary>m_strChar: the partner's character key.</summary>
    public string PartnerKey { get; private set; } = "";
    /// <summary>GetUidLastSent @0x14185d920: the mesos sent last.</summary>
    public long LastMesoSent { get; private set; }

    /// <summary>The constructor (@0x14185b780): the table's Init.</summary>
    public TradeBook() { Items.Init(); }

    // ---- sends ------------------------------------------------------------------------------------------------------

    /// <summary>SendTradeMode0: ask a character to trade.</summary>
    public void SendAsk(long characterId) => Send(TradeRequest.Ask(characterId));
    /// <summary>FUN_14185b8e0: answer a request (3 accept, 4 decline).</summary>
    public void SendAnswer(long requester, bool accept) => Send(TradeRequest.Answer(requester, accept));
    /// <summary>SendTradeMode7: the dialog closed.</summary>
    public void SendClose() => Send(TradeRequest.Close());
    /// <summary>SendTradeMode8: put an item in.</summary>
    public void SendAddItem(long uid, int amount, int slot) => Send(TradeRequest.AddItem(uid, amount, slot));
    /// <summary>SendTradeMode9: take an item out.</summary>
    public void SendRemoveItem(long uid, int slot) => Send(TradeRequest.RemoveItem(uid, slot));
    /// <summary>SendTradeMode11: lock.</summary>
    public void SendLock() => Send(TradeRequest.Lock());
    /// <summary>SendTradeMode13: confirm.</summary>
    public void SendConfirm() => Send(TradeRequest.Confirm());

    /// <summary>SendTradeMode10 @0x14185baf0: the mesos, unless they are the ones sent last; then kept as the last.</summary>
    public void SendMeso(long meso)
    {
        if (meso == LastMesoSent) return;
        Send(TradeRequest.SetMeso(meso));
        LastMesoSent = meso;
    }

    // ---- state ------------------------------------------------------------------------------------------------------

    /// <summary>FUN_14185d7b0: empty the table, no partner, my side not locked.</summary>
    public void Reset()
    {
        Items.Reset();
        Partner = 0;
        PartnerName = "";
        PartnerKey = "";
        Confirmed = false;
    }

    /// <summary>FUN_14185d820: Reset, then the partner.</summary>
    public void Begin(long id, string name, string key)
    {
        Reset();
        Partner = id;
        PartnerName = name ?? "";
        PartnerKey = key ?? "";
    }

    /// <summary>UIDialog::vfunc_73 of the trade dialog (uidialog.cpp:666, through CUIService::FUN_141adc7f0): the dialog may
    /// open (not limited on the map, no tutorial in the way, its own vfunc_75); false shows its own notice.</summary>
    public Func<bool> CanOpenDialog = () => true;
    /// <summary>CBlockManager::FindBlockedAction(0x20) with ShowBlockNotice(block, 0x20, 0): true when trading is blocked
    /// (the host shows the notice). STILL-OPEN: no block list is decoded, so the host gives false.</summary>
    public Func<bool> TradeBlocked = () => false;

    /// <summary>
    /// UIPopUpMenu::SendTradeRequest (@0x141a85680, uipartydialog.cpp:3261), the popup's Trade (500): the dialog may not
    /// open, nothing; the dialog open, notice 0x841 (FUN_140f8ff40(0x841, 1, 1)); trading blocked, nothing more; else with a
    /// character id (unsigned &gt; 0) SendTradeMode0 (trademanagerex_1.cpp:243: u8 0, i64 id).
    /// </summary>
    public void RequestTrade(long characterId)
    {
        if (!CanOpenDialog()) return;
        if (Dialog() != null)
        {
            Notice?.Invoke(Common(0x841), 1, true);
            return;
        }
        if (TradeBlocked()) return;
        if ((ulong)characterId > 0) SendAsk(characterId);
    }

    /// <summary>CanStartTrade @0x14185d8b0: the dialog open or a partner set: notice 0x841 (FUN_140f8ff40(0x841, 1, 1)), 0.</summary>
    public bool CanStartTrade()
    {
        if (Dialog() != null || (ulong)Partner > 0)
        {
            Notice?.Invoke(Common(0x841), 1, true);
            return false;
        }
        return true;
    }

    // ---- OnTrade ----------------------------------------------------------------------------------------------------

    /// <summary>OnTrade @0x14185bd20: the handler of the code.</summary>
    public void OnTrade(TradeMessage m)
    {
        switch (m.Code)
        {
            case 0: OnTradeRequest(m.Name, m.Id); break;
            case 1: OnTradeRequestFailed(m.Flag, m.Name, m.ItemId, m.Level); break;
            case 2: OnTradeRequestSent(); break;
            case 4: OnTradeDeclined(m.Name); break;
            case 5: OnTradeStarted(m.Id); break;
            case 6: OnTradeFinished(m.Flag != 0); break;
            case 8: OnTradeItemAdded(m); break;
            case 9: OnTradeItemRemoved(m.Flag != 0, m.TradeSlot, m.Id); break;
            case 10: Dialog()?.SetSideMeso(m.Flag, m.Meso); break;     // FUN_14185d4f0
            case 11: OnTradeConfirm(m.Flag != 0); break;
            case 12: OnTradeUnlock(m.Flag != 0); break;
            case 13: break;                                             // FUN_14185d720: reads the dialog, nothing more
            case 14: Chat(Common(0x843)); break;                        // FUN_14185d750
        }
    }

    void Chat(string text) => ChatLine?.Invoke(text, 0x12);
    string F(int id, params object[] args) => ClientFormat.Format(Common(id), args);

    /// <summary>OnTradeRequest @0x14185bec0: refused (the option or a block: the decline, the block's chat line), a box
    /// already up (mode 14), else the request box (type 1, 0x823 with the name, tag "requestTradeEx", the id, the
    /// TradeRequestDuration timer with Common 0x2b8, modaless, Enter does not answer it) and mode 2.</summary>
    void OnTradeRequest(string name, long id)
    {
        if (RefuseTrade())
        {
            SendAnswer(id, false);
            return;
        }
        if (BlockedMessage() is { } blocked)
        {
            SendAnswer(id, false);
            Chat(blocked);
            return;
        }
        if (BoxShown())
        {
            Send(TradeRequest.Busy(id));
            return;
        }
        var box = MessageBoxQueue.Create(1, F(0x823, name), null, "requestTradeEx");
        box.Append(id);
        box.SetTimer(RequestDuration(), 1, Common(0x2b8));
        box.NoInputBlock = true;            // FUN_141a02460(true)
        box.Modaless = true;
        box.EnterAllowed = false;           // FUN_141a024a0(false)
        ShowBox(box);
        Send(TradeRequest.Shown(id));
    }

    /// <summary>OnTradeRequestSent @0x14185c230: notice 0x822 and sound PC_InviteSuccess.</summary>
    void OnTradeRequestSent()
    {
        Notice?.Invoke(Common(0x822), 1, true);
        Sound?.Invoke("PC_InviteSuccess");
    }

    /// <summary>The text OnTradeRequestFailed @0x14185c2a0 shows for a result ("" for 0x11).</summary>
    public string RequestFailedText(int code, string name, int itemId, ushort level) => code switch
    {
        0 => F(0x827, name),
        0xc => F(0x829, name),
        1 => F(0x828, name),
        2 => F(0x82a, name),
        3 => F(0x82b, name),
        5 => F(0x82d, name),
        6 => F(0x82e, name),
        0xe => F(0x82c),
        7 => F(0x82f),
        8 => F(0x830),
        9 => F(0x831),
        10 => F(0x832),
        0xb => F(0x833),
        0xd => F(0x834),
        0xf => F(0x835, name),
        0x10 => F(0x836, name),
        0x11 => "",
        0x12 => ClientFormat.Format(CommonKey("s_trade_error_restricted_userlevel_max"), level),
        0x13 => ClientFormat.Format(CommonKey("s_trade_error_restricted_target_userlevel_max"), level),
        0x14 => F(0x93f, ItemName(itemId), level),
        0x15 => F(0x940, ItemName(itemId), level),
        _ => F(0x837, code),
    };

    /// <summary>OnTradeRequestFailed @0x14185c2a0: the result's text to the chat log (0x12) and the notice
    /// (ShowSystemNotice(text, 1, 0)), then the open dialog's FUN_141b297e0(result).</summary>
    void OnTradeRequestFailed(int code, string name, int itemId, ushort level)
    {
        string text = RequestFailedText(code, name, itemId, level);
        if (text.Length > 0)
        {
            Chat(text);
            Notice?.Invoke(text, 1, false);
        }
        Dialog()?.RequestFailed(code);
    }

    /// <summary>FUN_14185cc60: the partner declined: 0x824 with the name to the chat log and Unk_AkoMessage(0x824, 1, 0).</summary>
    void OnTradeDeclined(string name)
    {
        string text = F(0x824, name);
        Chat(text);
        Notice?.Invoke(text, 1, false);
    }

    /// <summary>OnTradeStarted @0x14185cd10: with the partner in the field: Begin, close dialogs 0x4e3b, 0x4ec1 and 0x4e47,
    /// no mesos sent, open the trade dialog, chat 0x846 with the name.</summary>
    void OnTradeStarted(long id)
    {
        var p = FindPlayer(id);
        if (p == null) return;
        Begin(id, p.Name, p.CharacterKey);
        CloseDialog?.Invoke(0x4e3b);
        CloseDialog?.Invoke(0x4ec1);
        CloseDialog?.Invoke(0x4e47);
        LastMesoSent = 0;
        OpenDialog?.Invoke(DialogId);
        Chat(F(0x846, p.Name));
    }

    /// <summary>OnTradeFinished @0x14185cee0: with the dialog open: close it, chat 0x826 (done) or 0x825 with the partner;
    /// no mesos sent either way.</summary>
    void OnTradeFinished(bool accepted)
    {
        if (Dialog() is { } dlg)
        {
            string partner = dlg.Partner;
            CloseDialog?.Invoke(DialogId);
            Chat(F(accepted ? 0x826 : 0x825, partner));
        }
        LastMesoSent = 0;
    }

    /// <summary>OnTradeItemAdded @0x14185d020: with the dialog open: an unknown item closes it; else the item (uid, count
    /// clamped, the packet's slot), mine goes to the item event and the table, then the dialog's slot.</summary>
    void OnTradeItemAdded(TradeMessage m)
    {
        var dlg = Dialog();
        if (dlg == null) return;
        var item = m.Item == null || ItemData == null ? null : InventoryController.ToItem(m.Item, ItemData);
        if (item == null)
        {
            dlg.Close();
            return;
        }
        item.Uid = m.Id;
        item.SetCountClamped(m.Item.Amount);
        item.Slot = m.Item.InventorySlot;
        bool mine = m.Flag != 0;
        if (mine)
        {
            ItemEvent?.Invoke(item, true);
            Items.AddItem(item);
        }
        dlg.SetTradeSlotItem(mine, m.TradeSlot, item);
    }

    /// <summary>OnTradeItemRemoved @0x14185d380: with the dialog open: mine needs the uid in the table (else nothing), goes
    /// to the item event and out of the table; then the dialog's slot is cleared.</summary>
    void OnTradeItemRemoved(bool mine, int slot, long uid)
    {
        var dlg = Dialog();
        if (dlg == null) return;
        if (mine)
        {
            var item = Items.Find(uid);
            if (item == null) return;
            ItemEvent?.Invoke(item, false);
            Items.RemoveItem(uid);
        }
        dlg.ClearTradeSlot(mine, slot);
    }

    /// <summary>OnTradeConfirm @0x14185d590: with the dialog open: mine sets the lock; the side latches.</summary>
    void OnTradeConfirm(bool mine)
    {
        var dlg = Dialog();
        if (dlg == null) return;
        if (mine) Confirmed = true;
        dlg.SetLatch(mine, true);
    }

    /// <summary>FUN_14185d610: with the dialog open: mine clears the lock (chat 0x844), else chat 0x845; the side unlatches.</summary>
    void OnTradeUnlock(bool mine)
    {
        var dlg = Dialog();
        if (dlg == null) return;
        if (mine)
        {
            Confirmed = false;
            Chat(Common(0x844));
        }
        else Chat(Common(0x845));
        dlg.SetLatch(mine, false);
    }
}
