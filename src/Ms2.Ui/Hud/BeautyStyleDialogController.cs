using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The saved-styles window of the beauty shop, UIBeautyShopStyleDialog (client/ms2/uibeautyshopstyledialog.cpp, type name
/// "BeautyShopStyleDialog" :797, movie uibeautyshopstyledialog). The state is the shop packet's modes (inbound 0x31,
/// FUN_140f05a80 @0x140f05a80, :1205-1366: <see cref="BeautyStylePacket"/>): the shop (mode 2), the saved list (0xd clears,
/// 0xe the expected count, 0xf the items), the slot extend (0x14), the save (0x10, 0x11), the delete (0x12), the apply (0x15)
/// and the result (8). The movie callbacks onClickExtendSlot and onClickDeleteCharacterCreateItem ask first and send mode 0x13
/// / 0x12 on yes (SendBeautyMode19 @0x140f05640, SendBeautyMode18 @0x140f056e0).
/// <para>STILL-OPEN: the movie's item objects (FUN_140f096f0, FUN_140f09810: the calls updateItems, updateItem and
/// removeItemAndAdd and InitData's list build), the save path FUN_140f07310 (onClickSaveCurStyle with a free slot and the
/// callback FUN_140f05820), the bill (UIBillDialog 0x4e7b of OnClickSave), the camera dialog
/// 0x4ef2 and the per-frame state are not recovered or ported, so the paths that need them do nothing; the Customize is the
/// host-set <see cref="IBeautyCustomize"/>. The payment's "not enough" box (FUN_14134a2e0) is the host-set
/// <see cref="NotEnough"/>.</para>
/// </summary>
public sealed class BeautyStyleDialogController : UiController
{
    public override string Movie => "uibeautyshopstyledialog";
    /// <summary>The CommonDepth layer of the movie: UIBeautyShopStyleDialog's ctor sets m_2c to 0xc (uibeautyshopstyledialog.cpp:782).</summary>
    public override int LoadLayer => CommonDepth.Scene;
    protected override string EntityMethod => "initData";
    public override string TypeName => "BeautyShopStyleDialog";
    /// <summary>UIBeautyShopStyleDialog's ctor (@0x140f03090, uibeautyshopstyledialog.cpp:778) has no SetTweenEnabled: m_c4 stays 0, so CloseDialog
    /// unloads at once (FUN_141924330, uidialog.cpp:393) and a following OpenDialog loads it again.</summary>
    protected override bool TweenEnabled => false;
    /// <summary>The ctor's SetFlag(0, 1) (uibeautyshopstyledialog.cpp:786): always on, so the field Esc's open-dialog test and
    /// CloseAllDialogs pass it by.</summary>
    public override bool AlwaysOn => true;
    /// <summary>vfunc_13 (uibeautyshopstyledialog.cpp:848): UIDialog's passes an always-on dialog, then Esc runs
    /// <see cref="RequestClose"/> and takes the key.</summary>
    public override bool EscapeKey()
    {
        if (IsClosed) return false;
        RequestClose();
        return true;
    }

    /// <summary>FUN_140f082d0 (uibeautyshopstyledialog.cpp:1605), Esc and "OnClickClose": nothing without a Customize; in shop mode 2
    /// or with no change (Customize::FUN_140cd2c20, <see cref="IBeautyCustomize.HasStyleChanges"/>) <see cref="Leave"/> at once; else
    /// common string 0x271 with buttons ok/cancel, whose answer 0 leaves (FUN_140f05850).</summary>
    public void RequestClose()
    {
        if (Customize == null) return;
        if (ShopMode == 2 || !Customize.HasStyleChanges) { Leave(); return; }
        var box = MessageBoxQueue.CreateWithCallback(Model.Tables.Common(0x271), 1, CloseOnOk);
        box.SetButtonPreset(2);
        ShowData(box);
    }

    /// <summary>FUN_140f08220 (uibeautyshopstyledialog.cpp:1591): nothing without a Customize; else the worn slot of the shop kind is
    /// restored and the dialog ends. STILL-OPEN: the field's vfunc_149 and SetSubScene(0x620001d) of the same path are the host's
    /// (<see cref="Closed"/>).</summary>
    public void Leave()
    {
        if (Customize == null) return;
        Customize.RestoreSlot(SlotOfKind(ShopKind));
        Close();
    }

    // ---- host-set parts -----------------------------------------------------------------------------------------------
    public IBeautyCustomize Customize;
    public Action<IOutPacket> Send = _ => { };
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>PC::GetEquipByUid(uid)->GetItem()->Clone() of the local player (mode 0x10 and 0x11): the worn item by uid, copied; null without one.</summary>
    public Func<long, EquipItem> CloneWorn = _ => null;
    /// <summary>The name of an item id (the string table's item name, FUN_141811980(1, id)).</summary>
    public Func<int, string> ItemName = _ => "";
    /// <summary>FUN_1412752d0(key, type): a notice line and its type argument (1, or 3 for the apply, :1327). STILL-OPEN: what the
    /// type changes in FUN_1412752d0 is not recovered; the host decides.</summary>
    public event Action<string, int> NoticeText;
    /// <summary>FUN_14134a2e0 (not recovered): the "not enough" box of the payment.</summary>
    public Action NotEnough;
    /// <summary>FUN_14134a120(4, 0, 0) of result 0x14 (a purchase shortcut, not ported).</summary>
    public Action Unavailable;
    /// <summary>PlayHairApplyEffect (mode 0x15): not ported, set by the host.</summary>
    public Action PlayHairApplyEffect;
    /// <summary>The constants BeautyStyleExpandSlotPrice, BeautyStyleExpandSlotCount1time, BeautyStyleDefaultSlotCount and
    /// BeautyStyleMaxSlotCount (clientconstants.h:428-431), set from <see cref="GameTables"/>.</summary>
    public long ExpandSlotPrice = 790;
    public int ExpandSlotCount1Time = 3, DefaultSlotCount = 1, MaxExtendSlotCount = 12;

    /// <summary>Takes the four constants from the loaded tables (the host calls it when it creates the dialog).</summary>
    public void ApplyTables(GameTables t)
    {
        ExpandSlotPrice = t.BeautyStyleExpandSlotPrice;
        ExpandSlotCount1Time = t.BeautyStyleExpandSlotCount1time;
        DefaultSlotCount = t.BeautyStyleDefaultSlotCount;
        MaxExtendSlotCount = t.BeautyStyleMaxSlotCount;
    }
    /// <summary>The page was left (the close path, not ported here): the host runs it.</summary>
    public event Action Closed;

    // ---- state --------------------------------------------------------------------------------------------------------
    public int ShopMode;                                     // m_26c
    public BeautyShopData Shop;                              // m_1b8
    public int OpenSlots;                                    // m_290
    public int ExpectedStyles;                               // m_294
    public bool ListArrived;                                 // m_260
    public readonly List<EquipItem> Saved = new();           // m_298
    /// <summary>m_2b0 / FUN_1412761e0(m_30, 1): a send awaits its answer; every mode after the list ones resets it.</summary>
    public bool Waiting;
    /// <summary>The saved list is complete (m_298.size() == m_294): InitData runs.</summary>
    public bool DataReady { get; private set; }
    public bool Closing;

    public int SaleEventId => Shop?.ShopId ?? 0;
    public int ShopKind => Shop?.Category ?? 0;

    // ---- slot math ----------------------------------------------------------------------------------------------------

    /// <summary>GetMaxSlotCount: FUN_140f011f0() + the max-extend constant.</summary>
    public int MaxSlotCount => DefaultSlotCount + MaxExtendSlotCount;

    /// <summary>FUN_140f08f20: m_290 = min(n, GetMaxSlotCount).</summary>
    public void SetOpenSlots(int n) => OpenSlots = Math.Min(n, MaxSlotCount);

    /// <summary>FUN_140f01470 / FUN_140f09230: the slots are at the maximum.</summary>
    public bool AtMaxSlots => OpenSlots >= MaxSlotCount;

    /// <summary>GetRemainSlotCount @0x140f09260: m_290 - m_298.size().</summary>
    public int RemainSlots => OpenSlots - Saved.Count;

    /// <summary>FUN_140f09020: the saved item of the index, or null outside the list.</summary>
    public EquipItem SavedAt(int index) => (uint)index < (uint)Saved.Count ? Saved[index] : null;

    /// <summary>FUN_140f01070: the worn slot of a shop kind (1 -> 1, 2 -> 3, 3 -> 2, else 0).</summary>
    public static int SlotOfKind(int kind) => kind switch { 1 => 1, 2 => 3, 3 => 2, _ => 0 };

    // ---- load / close -------------------------------------------------------------------------------------------------

    public override void Close()
    {
        if (Closing) return;
        Closing = true;
        base.Close();
        Closed?.Invoke();
    }

    /// <summary>FUN_140f08f70: drop the selection and restore the worn hair (RestoreSlot of the shop kind's slot).</summary>
    public void DropSelection() => Customize?.RestoreSlot(SlotOfKind(ShopKind));

    // ---- packet 0x31 --------------------------------------------------------------------------------------------------

    /// <summary>FUN_140f05a80 (:1205): the first part updates the state; from the movie on (vfunc_55 false and the movie there) every
    /// mode resets the pending send, then modes 0x10, 0x11, 0x12, 0x15 and 8 act.</summary>
    public void OnPacket(BeautyStylePacket p)
    {
        switch (p.Mode)
        {
            case 2:
                ShopMode = p.ShopMode;
                Shop = p.Shop;
                break;
            case 0xd:
                Saved.Clear();
                DataReady = false;
                break;
            case 0xe:
                ExpectedStyles = p.ExpectedStyles;
                ListArrived = true;
                // FUN_1422dc430 -> vfunc_8(1): a window show, not ported (STILL-OPEN).
                break;
            case 0x14:
                OnExtended(p);
                break;
            case 0xf:
                foreach (var s in p.Styles)
                {
                    s.Item.Slot = (EquipSlot)s.StyleSlot;   // CItem::SetSlot(i32 style slot), :1205-1366
                    Saved.Add(s.Item);
                }
                if (Saved.Count == ExpectedStyles) DataReady = true;   // InitData: its list build is STILL-OPEN (FUN_140f09810)
                break;
        }
        if (Closing || Host == null) return;
        Waiting = false;
        switch (p.Mode)
        {
            case 0x10:
            case 0x11:
                OnSaved(p);
                break;
            case 0x12:
                OnRemoved(p);
                break;
            case 0x15:
                NoticeText?.Invoke(Model.Tables.Common("s_beauty_msg_complete_apply_saved_style"), 3);
                PlayHairApplyEffect?.Invoke();
                DropSelection();                          // FUN_140f092b0: m_2b0 = 0, FUN_140f08f70
                break;
            case 8:
                OnResult(p.Result);
                break;
        }
    }

    /// <summary>Mode 0x14: FUN_140f08f20(extended + default); with the movie and the list there, the added slots show
    /// "s_beauty_msg_complete_extend_style_slot" with their count (the movie's updateItems call is STILL-OPEN).</summary>
    void OnExtended(BeautyStylePacket p)
    {
        int old = OpenSlots;
        SetOpenSlots(p.ExtendedSlots + DefaultSlotCount);
        if (Host == null || !ListArrived) return;
        int added = Math.Max(0, OpenSlots - old);
        if (added > 0) NoticeText?.Invoke(ClientFormat.Format(Model.Tables.Common("s_beauty_msg_complete_extend_style_slot"), added), 1);
    }

    /// <summary>Modes 0x10 and 0x11: the worn item by uid is copied into the new saved slot with the packet's uid, slot and time; no
    /// worn item or copy: nothing (the client returns before the notice).</summary>
    void OnSaved(BeautyStylePacket p)
    {
        var copy = CloneWorn(p.Uid);
        if (copy == null) return;
        copy.Uid = p.NewUid;
        copy.CreationTime = p.Time;
        copy.Slot = (EquipSlot)p.Slot;   // CItem::SetSlot(byte slot)
        Saved.Add(copy);
        NoticeText?.Invoke(Model.Tables.Common("s_beauty_msg_complete_save_hair_style"), 1);
    }

    /// <summary>Mode 0x12: the item with the uid leaves the list (the first one), the notice shows and the selection drops.</summary>
    void OnRemoved(BeautyStylePacket p)
    {
        int at = Saved.FindIndex(s => s != null && s.Uid == p.Uid);
        if (at >= 0) Saved.RemoveAt(at);
        NoticeText?.Invoke(Model.Tables.Common("s_beauty_msg_complete_remove_saved_hair_style"), 1);
        DropSelection();
    }

    /// <summary>Mode 8, the result (:1332): 2 the payment's box, 0x14 the shortcut, 0x18 and 0x19 a notice, 0x13 and 0x17 a plain
    /// box, 7 common string 0x273 and any other common string 0x272 with the code; the last two end the dialog on ok.</summary>
    void OnResult(int code)
    {
        switch (code)
        {
            case 2: NotEnough?.Invoke(); break;
            case 0x14: Unavailable?.Invoke(); break;
            case 0x18: NoticeText?.Invoke(Model.Tables.Common("s_beauty_msg_error_style_apply_already"), 1); break;
            case 0x19: NoticeText?.Invoke(Model.Tables.Common("s_beauty_msg_error_style_save_cant_not_beauty_day"), 1); break;
            case 0x13: Plain("s_beauty_msg_error_style_slot_extend_max"); break;
            case 0x17: Plain("s_beauty_msg_error_style_disable_gender"); break;
            case 7: ShowData(MessageBoxQueue.CreateWithCallback(Model.Tables.Common(0x273), 0, CloseOnOk)); break;
            default: ShowData(MessageBoxQueue.CreateWithCallback(ClientFormat.Format(Model.Tables.Common(0x272), code), 0, CloseOnOk)); break;
        }
    }

    void Plain(string key) => ShowData(MessageBoxQueue.CreateWithCallback(Model.Tables.Common(key), 0, (_, _) => true));

    /// <summary>FUN_140f05850 (uibeautyshopstyledialog.cpp:1173): the answer 0 runs FUN_140f08220 (<see cref="Leave"/>).</summary>
    bool CloseOnOk(int button, MessageBoxData _)
    {
        if (button == 0) Leave();
        return true;
    }

    // ---- movie callbacks (vfunc_28) -----------------------------------------------------------------------------------

    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onclickextendslot": ClickExtendSlot(); return true;
            case "onclickclose": RequestClose(); return true;   // :899
            case "onclickdeletecharactercreateitem":
                if (args.Length > 0) ClickDelete((int)Rt.ToNumber(Host.Get(args[0], "index")));
                return true;
        }
        return false;
    }

    /// <summary>onClickExtendSlot (:998): at the maximum a plain box; else the ask (ExpandSlotCount1Time, the price) whose answer
    /// 0 is SendBeautyMode19.</summary>
    public void ClickExtendSlot()
    {
        if (AtMaxSlots) { Plain("s_beauty_msg_error_style_slot_extend_max"); return; }
        var box = MessageBoxQueue.CreateWithCallback(
            ClientFormat.Format(Model.Tables.Common("s_beauty_msg_ask_save_style_extend"), ExpandSlotCount1Time, ExpandSlotPrice), 1, SendExtend);
        box.EnterAllowed = false;                         // FUN_141a024a0(0)
        ShowData(box);
    }

    /// <summary>SendBeautyMode19 @0x140f05640: answer 0 sends mode 0x13 and locks the input until the answer.</summary>
    bool SendExtend(int button, MessageBoxData _)
    {
        if (button == 0)
        {
            Send(new BeautyExtendStylesRequest());
            Waiting = true;
        }
        return true;
    }

    /// <summary>onClickDeleteCharacterCreateItem (:1014): an empty slot a plain box; else the ask with the item's name whose
    /// answer 0 is SendBeautyMode18 with the index as the box's first argument.</summary>
    public void ClickDelete(int index)
    {
        var item = SavedAt(index);
        if (item == null) { Plain("s_beauty_msg_error_already_empty_slot"); return; }
        var box = MessageBoxQueue.CreateWithCallback(
            ClientFormat.Format(Model.Tables.Common("s_beauty_msg_ask_delete_style"), ItemName(item.Id)), 1, SendDelete);
        box.Append(index);
        ShowData(box);
    }

    /// <summary>SendBeautyMode18 @0x140f056e0: answer 0 sends mode 0x12 with the item's uid and the index byte (no item: nothing).</summary>
    bool SendDelete(int button, MessageBoxData data)
    {
        if (button == 0)
        {
            var item = SavedAt(data.ArgInt(0, 0));
            if (item == null) return true;
            Send(new BeautyDeleteStyleRequest(item.Uid, (byte)data.ArgInt(0, 0)));
            Waiting = true;
        }
        return true;
    }
}
