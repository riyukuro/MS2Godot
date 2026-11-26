using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Items;

/// <summary>What a drop on the scroll slot did (EnchantScrollSlot::vfunc_18, enchantscrollslot.cpp:95).</summary>
public enum EnchantScrollPut { None, Sent, AskRefund }

/// <summary>
/// UIEnchantScrollDialog's model (client ms2/uienchantscrolldialog.cpp, UI id 0x4ee0) and its EnchantScrollSlot
/// (enchantscrollslot.cpp): the scroll the server opened the window for, the item put on the slot, and the client's checks
/// before each send. The recv side is <see cref="EnchantScrollMessage"/> through <see cref="Apply"/>; the window is
/// EnchantScrollDialogController. The dialog object always exists in the client (CSceneStateGame::OnEnchantScroll finds
/// it, scenestategame.cpp:8084), so every message is applied whether the window is open or not.
/// <para>STILL-OPEN: mode 4 (UIRewardNotify::FUN_141abf6e0 per reward and UIMasteryDialog::FUN_140eb4ba0) is read and
/// dropped.</para>
/// </summary>
public sealed class EnchantScrollBook
{
    public const int UiId = 0x4ee0;
    /// <summary>GetTypeName: the slot's binding name and the movie's bindID.</summary>
    public const string TypeName = "EnchantScrollDialog";
    /// <summary>FUN_141275260's notice of a result code 1..8 (FUN_140f3c6c0, uienchantscrolldialog.cpp:287-300).</summary>
    public const int FirstNotice = 0xb72;

    // The model (FUN_140f3b920 fills it, FUN_140f3d210 clears it).
    public long ScrollUid { get; private set; }
    public long ItemUid { get; private set; }
    /// <summary>m_nType: 1 enchant, 2 restore, 3 random, 5 repair.</summary>
    public int Type { get; private set; }
    public bool DisableBreak { get; private set; }
    public int TargetLevel { get; private set; }
    /// <summary>m_nFailRate, already / 100.</summary>
    public uint FailRate { get; private set; }
    public ushort LevelMin { get; private set; }
    public ushort LevelMax { get; private set; }
    public IReadOnlyList<int> EquipTypes { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<int> Ranks { get; private set; } = Array.Empty<int>();
    public int RandomMin { get; private set; }
    public int RandomMax { get; private set; }

    /// <summary>The slot "EnchantScrollDialog" is registered (FUN_140cd73b0); unregistered by the close (FUN_140cd74a0).</summary>
    public bool SlotRegistered { get; private set; }
    /// <summary>The registered slot's m_nOwner: the scroll uid it was registered with.</summary>
    public long SlotOwner { get; private set; }
    /// <summary>The registered slot's item (m_spItem) uid; 0 for none.</summary>
    public long SlotItemUid { get; private set; }

    /// <summary>CUIService::IsDialogOpen(0x4ee0); the window sets it.</summary>
    public bool IsOpen { get; set; }
    /// <summary>g_143ab2a90.FindItemByUid: the inventory item of an uid (no worn equips), null for none.</summary>
    public Func<long, EquipItem> Find { get; set; } = _ => null;
    public Action<IOutPacket> Send { get; set; }
    /// <summary>The item's ReadFromStream: the read body replaces the inventory item.</summary>
    public Action<EquipItem> StoreItem { get; set; }

    /// <summary>FUN_140f3d210's Invoke("clear") on the movie.</summary>
    public event Action Cleared;
    /// <summary>OpenDialog(0x4ee0): mode 0 while the window is closed.</summary>
    public event Action OpenRequested;
    /// <summary>UpdateScrollInfo's setData (the slot was registered with no item).</summary>
    public event Action ScrollInfoChanged;
    /// <summary>FUN_140f3bd10's setData, when both the scroll and the item are in the inventory.</summary>
    public event Action<EnchantScrollMessage.Preview> Previewed;
    /// <summary>FUN_140f3c6c0 code 0: the inventory refresh (FUN_141999e00), then the "enchant_ok" box of the uid's item.</summary>
    public event Action<long> Succeeded;
    /// <summary>FUN_141275260(id, 1, 1): a result code's notice id.</summary>
    public event Action<int> Notice;
    /// <summary>vfunc_67: the window closes (code 1, or the box's OK).</summary>
    public event Action CloseRequested;
    /// <summary>The source slot or this slot needs a redraw (DragSlot::vfunc_10).</summary>
    public event Action SlotChanged;

    readonly EnchantScrollReadContext context = new();

    /// <summary>What <see cref="EnchantScrollMessage.Read(Maple2.PacketLib.Tools.ByteReader, EnchantScrollReadContext, ProtocolProfile)"/>
    /// needs from this dialog now.</summary>
    public EnchantScrollReadContext Context
    {
        get
        {
            context.ScrollUid = ScrollUid;
            context.TargetLevel = TargetLevel;
            context.Find = Find;
            return context;
        }
    }

    public void Apply(EnchantScrollMessage m)
    {
        switch (m)
        {
            case EnchantScrollMessage.Open o: Open(o); break;
            case EnchantScrollMessage.Preview p: Preview(p); break;
            case EnchantScrollMessage.Result r: Result(r); break;
        }
    }

    /// <summary>FUN_140f3b920 (uienchantscrolldialog.cpp:199): reset, the scroll's limits, then UpdateScrollInfo when the window
    /// is open, else OpenDialog (whose OnLoadComplete calls UpdateScrollInfo).</summary>
    void Open(EnchantScrollMessage.Open o)
    {
        Reset();
        ScrollUid = o.ScrollUid; Type = o.Type; DisableBreak = o.DisableBreak; TargetLevel = o.TargetLevel; FailRate = o.FailRate;
        LevelMin = o.LevelMin; LevelMax = o.LevelMax; EquipTypes = o.EquipTypes; Ranks = o.Ranks;
        RandomMin = o.RandomMin; RandomMax = o.RandomMax;
        if (IsOpen) UpdateScrollInfo();
        else OpenRequested?.Invoke();
    }

    /// <summary>FUN_140f3bd10 (:227): m_nItemUid is kept either way; with both items in the inventory the slot is registered
    /// again with the item, then the setData.</summary>
    void Preview(EnchantScrollMessage.Preview p)
    {
        ItemUid = p.ItemUid;
        if (!p.Shown) return;
        Register(p.ItemUid);
        Previewed?.Invoke(p);
    }

    /// <summary>FUN_140f3c6c0 (:268): code 0 with the item in the inventory stores its body and raises the box; codes 1..8 a
    /// notice (ids 0xb72..0xb79), code 1 then the close; other codes nothing.</summary>
    void Result(EnchantScrollMessage.Result r)
    {
        if (r.Code == 0)
        {
            if (r.Item == null) return;     // :277
            StoreItem?.Invoke(r.Item);
            SlotChanged?.Invoke();
            Succeeded?.Invoke(r.ItemUid);
            return;
        }
        if (r.Code is >= 1 and <= 8) Notice?.Invoke(FirstNotice + r.Code - 1);
        if (r.Code == 1) Close();
    }

    /// <summary>UpdateScrollInfo (@0x140f3cb10, :307): the slot registered again with the scroll and no item, then the
    /// window's setData.</summary>
    public void UpdateScrollInfo()
    {
        Register(0);
        ScrollInfoChanged?.Invoke();
    }

    void Register(long itemUid)
    {
        SlotRegistered = true;
        SlotOwner = ScrollUid;
        SlotItemUid = itemUid;
        SlotChanged?.Invoke();
    }

    /// <summary>vfunc_67 (@0x140f3b5b0, :136): FUN_140f3d210, the slot unregistered, then the window closes.</summary>
    public void Close()
    {
        Reset();
        SlotRegistered = false;
        SlotOwner = 0;
        SlotItemUid = 0;
        CloseRequested?.Invoke();
    }

    /// <summary>FUN_140f3d210 (:342): the members to 0 (not m_nType), then the movie's clear.</summary>
    void Reset()
    {
        TargetLevel = 0; FailRate = 0; ScrollUid = 0; ItemUid = 0; DisableBreak = false; LevelMin = 0; LevelMax = 0;
        EquipTypes = Array.Empty<int>(); Ranks = Array.Empty<int>(); RandomMin = 0; RandomMax = 0;   // m_nType is kept
        Cleared?.Invoke();
    }

    /// <summary>
    /// EnchantScrollSlot::vfunc_18 (enchantscrollslot.cpp:95): an inventory item dropped on the registered slot. An item with
    /// a charge of an older version asks the refund first (FUN_141902660, uicommon.cpp:2009) and sends nothing here; else
    /// mode 1 with the slot's owner and the item. Nothing for an uid not in the inventory or no slot.
    /// </summary>
    public EnchantScrollPut Put(long itemUid)
    {
        if (!SlotRegistered || Find(itemUid) is not { } item) return EnchantScrollPut.None;
        if (ItemEnchantBook.NeedsRefund(item)) return EnchantScrollPut.AskRefund;
        Send?.Invoke(new EnchantScrollPutRequest(SlotOwner, item.Uid));
        return EnchantScrollPut.Sent;
    }

    /// <summary>SendRequestItemEnchant (@0x1419028d0, uicommon.cpp:1993): the refund box's yes sends RequestItemEnchant mode 9.</summary>
    public void ConfirmRefund(long itemUid) => Send?.Invoke(new ItemEnchantRefundRequest(itemUid));

    /// <summary>vfunc_28 "onEnchant" (:157): mode 2 with the scroll and the uid, only when the inventory holds the uid.</summary>
    public bool Enchant(long itemUid)
    {
        if (Find(itemUid) == null) return false;
        Send?.Invoke(new EnchantScrollEnchantRequest(ScrollUid, itemUid));
        return true;
    }

    /// <summary>EnchantScrollSlot::vfunc_17 (:80) / vfunc_14 (:115): the slot's item dropped, then UpdateScrollInfo.</summary>
    public void RemoveSlotItem()
    {
        SlotItemUid = 0;
        UpdateScrollInfo();
    }
}
