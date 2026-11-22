using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Items;

/// <summary>What the dialog does about a request: <see cref="Sent"/> went out; an Ask needs the player's box answer (then the
/// matching Confirm method); the rest are refusals that send nothing.</summary>
public enum EnchantStep { Sent, NoItem, AskRefund, Reset, AskEnchantedIngredient, AskBindingCharacter, AskProtector, AskPayback, NotReady }

/// <summary>
/// UIItemEnchantDialog's model and the client's checks before each send (client ms2/uiitemenchantdialog.cpp; UI id 0x4e92).
/// The recv side is <see cref="ItemEnchantMessage"/> through <see cref="Apply"/>.
/// <para>STILL-OPEN: the SWF fill (the dialog is not drawn), the over-enchanted ingredient box (FUN_1419c9490), the protector
/// rank warnings (FUN_1419c4360 / FUN_1419c5890), the peachy binding rule's inputs (feature 0x87, FUN_1419c89b0), the
/// movie's own bound of the fail stack buttons (SetFailStackInfo @0x1419c7610), and the three wiring delegates of the
/// ingredient test (CanUseItem, OriginOrItemId, HasStringTag).</para>
/// </summary>
public sealed class ItemEnchantBook
{
    public const int ExpOphelia = 1, ExpPeachy = 2;
    /// <summary>CItemEnchantInfo::Version (itemenchantinfo.h:57).</summary>
    public const int ChargeVersion = 1;
    /// <summary>The notice ids of mode 12 (OnItemEnchant): the code 1, 2, 3; any other is NoticeOther with the code.</summary>
    public const int Notice1 = 0x94d, Notice2 = 0x94f, Notice3 = 0x950, NoticeOther = 0x94c;
    public const string RefundOkKey = "s_itemenchant_enchant_exp_refund_ok";

    public byte ExpMode { get; set; } = ExpOphelia;
    /// <summary>m_mode: 3 (ophelia) or 4 (peachy), set with ExpMode by the talk script's argument.</summary>
    public int Mode { get; private set; } = 3;

    /// <summary>UIItemEnchantDialog::SetModeByName (uiitemenchantdialog.cpp:621, @0x1419c02e0): "expMode" is the peachy
    /// mode (expMode 2, mode 4); any other argument the ophelia (1, 3).</summary>
    public void SetModeByName(string name)
    {
        if (name == "expMode") { ExpMode = ExpPeachy; Mode = 4; }
        else { ExpMode = ExpOphelia; Mode = 3; }
    }
    /// <summary>CItemEnchantLimitTable::CanEnchant (@0x14058df90): <see cref="ItemOptionTables.CanEnchant"/> in the game; default none.</summary>
    public Func<Item, bool> CanEnchantItem { get; set; } = _ => false;
    /// <summary>The dialog is open (FUN_141924630, not closed): a result body is then kept in <see cref="Shown"/> and the
    /// inventory item stays until <see cref="Restore"/>.</summary>
    public bool IsOpen { get; set; }
    /// <summary>The clone the open dialog shows (UpdateItemFromPacket's m_item); null when none.</summary>
    public EquipItem Shown { get; private set; }
    /// <summary>PC.CanUseItem of FUN_1419c9260; default true. STILL-OPEN: not wired to the player's use test.</summary>
    public Func<Item, bool> CanUseItem { get; set; } = _ => true;
    /// <summary>GetOriginOrItemId of FUN_1419c9260; default the item id. STILL-OPEN: the origin id is not in the item data.</summary>
    public Func<Item, int> OriginOrItemId { get; set; } = i => i.ItemId;
    /// <summary>CItem::HasStringTag (@0x1405277E0) -> CItemData::HasStringTag (@0x14053c6b0, itemdata.cpp:535): the item data's
    /// stringTag attribute (&lt;basic stringTag&gt;, CItemData Load itemdata.cpp:338) read through CItemDataTable::GetTagByName
    /// (itemdatatable.cpp:578) equals the key. The keys FUN_1419c93a0 asks for, 0xcb..0xd0 (203..208), are the AddItemTag names
    /// EnchantJockerItemNormal..Epic (itemdatatable.cpp:1446-1451); see <see cref="ItemTags"/>.</summary>
    public Func<Item, int, bool> HasStringTag { get; set; } = (item, key) => item?.Data != null && key != 0 && ItemTags.ByName(item.Data.StringTag) == key;
    public Func<long, Item> Find { get; set; } = _ => null;
    public Action<IOutPacket> Send { get; set; }
    /// <summary>The talk the dialog opened from: (talking, npc id), and the client constant m_69a0.</summary>
    public Func<(bool Talking, int NpcId)> Talk { get; set; } = () => (false, 0);
    public int TalkConstant { get; set; }
    /// <summary>The peachy path's binding box test (feature 0x87 and the item not owned by my character); default none.</summary>
    public Func<Item, bool> NeedsBindingBox { get; set; } = _ => false;
    public Action<EquipItem> StoreItem { get; set; }

    // The model (members at +0x1a0..+0x318 of the dialog).
    public long SelectedUid { get; private set; }
    /// <summary>m_state: 0 idle, 1 success, 2 failure.</summary>
    public int State { get; private set; }
    public IReadOnlyList<EnchantIngredient> Ingredients { get; private set; } = Array.Empty<EnchantIngredient>();
    public IReadOnlyDictionary<int, int> Options { get; private set; } = new Dictionary<int, int>();
    public EnchantRates Rates { get; private set; }
    public long Protector0 { get; private set; }
    public long Protector1 { get; private set; }
    public byte FailStackMode { get; private set; }
    public int FailStack { get; private set; }
    public int FailStackCount { get; private set; }
    public IReadOnlyList<long> FailStackUids { get; private set; } = Array.Empty<long>();
    public int ResultItemId { get; private set; }
    public short ResultRank { get; private set; }
    public int ResultCount { get; private set; }
    /// <summary>The failure type of the last mode 11 packet (FUN_1419c2e30 reads it for failDesc); 0 after a success.</summary>
    public int FailType { get; private set; }
    bool infoRead;
    long refundUid;

    /// <summary>The dialog must close (an info packet for another mode): FUN_..vfunc_67 and SendNpcTalkMode0.</summary>
    public event Action CloseRequested;
    /// <summary>A notice id (mode 12) with its argument; the refund notice is <see cref="RefundOkKey"/> (id 0, arg 0).</summary>
    public event Action<int, int> Notice;
    /// <summary>The slot reset (initEnchantSlot): the selected item cannot be enchanted.</summary>
    public event Action SlotReset;
    /// <summary>The model changed (UpdateEnchantInfo / UpdateEnchantIngredient / ingredient counts).</summary>
    public event Action Changed;
    /// <summary>setEnchantResult: success, uid, the level before and now (the movie plays its result animation).</summary>
    public event Action<bool, long, int, int> Result;
    /// <summary>The failure reward page (FUN_1419c90b0): item id, count, meso.</summary>
    public event Action<int, int, long> Reward;

    /// <summary>Every applied message, after the model changed: the dialog redraws the part that message feeds.</summary>
    public event Action<ItemEnchantMessage> Applied;
    /// <summary>The charge before the last <see cref="ItemEnchantMessage.ChargeChanged"/> (FUN_1419c3950's oldLevel).</summary>
    public int PrevCharge { get; private set; }

    void Out(IOutPacket p) => Send?.Invoke(p);

    /// <summary>vfunc_85(type) (@0x1419bfca0): the NpcTalk step report.</summary>
    public void Step(short type)
    {
        var (talking, npc) = Talk();
        Out(new ItemEnchantStepRequest(talking, npc, TalkConstant, type));
    }

    /// <summary>onRetry (@0x1419c11e0): the result is dropped (FUN_1419c77a0) and step 4 reported.</summary>
    public void Retry() => Step(4);

    /// <summary>onComplete (@0x1419c10e0): step 3, then RestoreItem. The caller plays the sound for <see cref="State"/>.</summary>
    public void Complete() { Step(3); Restore(); }

    /// <summary>FUN_141902660: a charge of an older version asks the refund first.</summary>
    static bool NeedsRefund(Item i) => i.Record?.Enchant is { } e && e.Version != ChargeVersion && e.Charge != 0;

    /// <summary>OnLoadComplete: mode 0.</summary>
    public void Load() => Out(new ItemEnchantLoadRequest());

    /// <summary>OnSelectItem (@0x1419c0d40).</summary>
    public EnchantStep Select(long uid)
    {
        var item = Find(uid);
        if (item == null) return EnchantStep.NoItem;
        if (NeedsRefund(item)) { refundUid = uid; return EnchantStep.AskRefund; }
        if (item.Record?.Enchant == null) return EnchantStep.NoItem;
        EnchantStep r;
        // CItem::FUN_140527030 -> CItemEnchantInfo::FUN_14058c8a0 always returns 0 (itemenchantinfo.cpp:138).
        if (CanEnchantItem(item)) { Out(new ItemEnchantSelectRequest(ExpMode, item.Uid)); r = EnchantStep.Sent; }
        else { SlotReset?.Invoke(); r = EnchantStep.Reset; }
        var (talking, npc) = Talk();
        Out(new ItemEnchantTalkRequest(talking, npc, TalkConstant, item.Uid));
        return r;
    }

    /// <summary>The refund box's Yes (SendRequestItemEnchant, uicommon.cpp:1993).</summary>
    public void ConfirmRefund() { if (refundUid != 0) Out(new ItemEnchantRefundRequest(refundUid)); refundUid = 0; }

    /// <summary>FUN_1419c1d60: the answer is for this mode and a result item is set.</summary>
    public bool Ready => infoRead && ResultItemId != 0;

    /// <summary>FUN_1419c9260 (@0x1419c9260): the player may use the item, it is not locked (CItem::FUN_140528e20; gems too, as
    /// CanUseItem's lock test), not the selected item, and either the result's id and rank or the tag material
    /// (FUN_1419c93a0: the base item's rank 1..6 picks the tag 0xcb..0xd0, a stack of exactly 1).</summary>
    public bool IsIngredientCandidate(Item item, long now) =>
        item != null && CanUseItem(item) && ItemTradeRules.NotLocked(item, now, out _) && item.Uid != SelectedUid
        && ((OriginOrItemId(item) == ResultItemId && item.Rarity == ResultRank) || IsTagMaterial(Find(SelectedUid), item));

    /// <summary>FUN_1419c93a0 (@0x1419c93a0).</summary>
    bool IsTagMaterial(Item baseItem, Item other)
    {
        if (baseItem == null || baseItem.Rarity < 1 || baseItem.Rarity > 6) return false;
        return HasStringTag(other, 0xcb + baseItem.Rarity - 1) && other.Count == 1;
    }

    /// <summary>OnSelectEquipIngredient (@0x1419c1220) with the mode 2 send (FUN_1419c16c0): an enchanted ingredient asks first.</summary>
    public EnchantStep SelectIngredient(long uid, byte flag)
    {
        if (!Ready) return EnchantStep.NotReady;
        var item = Find(uid);
        if (item == null) return EnchantStep.NoItem;
        if (flag != 0 && item.EnchantLevel > 0) { pendingIngredient = uid; return EnchantStep.AskEnchantedIngredient; }
        Out(new ItemEnchantIngredientRequest(uid, flag));
        return EnchantStep.Sent;
    }
    long pendingIngredient;
    /// <summary>The enchanted ingredient box's Yes: mode 2 with flag 1.</summary>
    public void ConfirmEnchantedIngredient() { if (pendingIngredient != 0) Out(new ItemEnchantIngredientRequest(pendingIngredient, 1)); pendingIngredient = 0; }

    /// <summary>OnClickFailStackUp / Down (@0x1419c15c0, @0x1419c1630): mode 3 with the stack +- 1, sent unconditionally; the
    /// movie bounds its buttons (STILL-OPEN, see the class note).</summary>
    public void ChangeFailStack(int delta) => Out(new ItemEnchantFailStackRequest(FailStack + delta));

    /// <summary>onEnchant (@0x1419c0440): the item must exist and need no refund; the peachy path sends mode 6 (after its binding
    /// box); the ophelia path with a protection rate asks which protector, without one sends mode 4 with none.</summary>
    public EnchantStep Enchant()
    {
        var item = Find(SelectedUid);
        if (item == null) return EnchantStep.NoItem;
        if (NeedsRefund(item)) { refundUid = item.Uid; return EnchantStep.AskRefund; }
        if (ExpMode == ExpPeachy)
        {
            if (NeedsBindingBox(item)) return EnchantStep.AskBindingCharacter;
            Out(new ItemEnchantExpRequest(item.Uid));
            Step(2);
            return EnchantStep.Sent;
        }
        if (Rates.Protect != 0) return EnchantStep.AskProtector;
        var step = ConfirmProtector(0);
        State = 0;
        return step;
    }

    /// <summary>The binding box's Yes: mode 6 (SendRequestItemEnchantMode6 @0x1419c0c70 sends only for an existing item).</summary>
    public void ConfirmBinding() { if (Find(SelectedUid) != null) { Out(new ItemEnchantExpRequest(SelectedUid)); Step(2); } }

    long paybackProtector;
    /// <summary>FUN_1419c7a30(uid, v) (@0x1419c7a30): no item, nothing; a charge above 0 asks the payback box first
    /// (<see cref="ConfirmPayback"/>), else mode 4 (v 1 uses the first protector, 2 the second, 0 none).</summary>
    public EnchantStep ConfirmProtector(int v)
    {
        var item = Find(SelectedUid);
        if (item == null) return EnchantStep.NoItem;
        paybackProtector = v == 1 ? Protector0 : v == 2 ? Protector1 : 0;
        if (item.Record?.Enchant?.Charge > 0) return EnchantStep.AskPayback;
        Out(new ItemEnchantRequest(item.Uid, paybackProtector));
        Step(2);
        return EnchantStep.Sent;
    }

    /// <summary>The payback box's Yes (OnConfirmEnchantMode4MsgBox): SendRequestItemEnchantMode4 (@0x1419c7ce0).</summary>
    public void ConfirmPayback()
    {
        if (Find(SelectedUid) is { } item) { Out(new ItemEnchantRequest(item.Uid, paybackProtector)); Step(2); }
    }

    /// <summary>OnItemEnchant's modes (scenestategame.cpp:6101).</summary>
    public void Apply(ItemEnchantMessage m)
    {
        Dispatch(m);
        Applied?.Invoke(m);
    }

    void Dispatch(ItemEnchantMessage m)
    {
        switch (m)
        {
            case ItemEnchantMessage.Info x:
                Reset();
                if (x.ExpMode != ExpMode) { CloseRequested?.Invoke(); return; }
                SelectedUid = x.Uid; Ingredients = x.Ingredients; Options = x.Options; Rates = x.Rates;
                Protector0 = x.Protector0; Protector1 = x.Protector1; FailStackMode = x.FailStackMode;
                ResultItemId = x.ResultItemId; ResultRank = x.ResultRank; ResultCount = x.ResultCount;
                infoRead = true; Changed?.Invoke();
                break;
            case ItemEnchantMessage.ChargeChanged x:
                if (Find(x.Uid)?.Record?.Enchant is not { } e) break;
                FailStackUids = Array.Empty<long>(); FailStackCount = 0;
                PrevCharge = e.Charge;
                e.Charge = x.Charge;
                Changed?.Invoke();
                break;
            case ItemEnchantMessage.FailStackInfo x:
                FailStack = x.FailStack; FailStackCount = x.FailStackCount; FailStackUids = x.Uids; Rates = x.Rates; Changed?.Invoke();
                break;
            case ItemEnchantMessage.FailStackList x:
                FailStackCount = x.FailStackCount; FailStackUids = x.Uids; Changed?.Invoke();
                break;
            case ItemEnchantMessage.Refunded:
                Notice?.Invoke(0, 0);
                if (IsOpen) Out(new ItemEnchantSelectRequest(ExpMode, SelectedUid));
                break;
            case ItemEnchantMessage.Success x:
                if (x.Item == null) return;
                {
                    int prev = Find(x.Uid)?.EnchantLevel ?? 0;
                    Keep(x.Item);
                    Options = x.Options; State = 1; FailType = 0;
                    Result?.Invoke(true, x.Uid, prev, x.Item.Enchant?.Level ?? prev);
                }
                break;
            case ItemEnchantMessage.Failure x:
                if (x.Item == null) return;
                {
                    int prev = Find(x.Uid)?.EnchantLevel ?? 0;
                    Keep(x.Item);
                    State = 2; FailType = x.FailType;
                    Result?.Invoke(false, x.Uid, prev, x.Item.Enchant?.Level ?? prev);
                    if (x.B > 0 || x.C != 0) Reward?.Invoke(x.A, x.B, x.C);
                }
                break;
            case ItemEnchantMessage.Error x:
                if (x.Code == 1) Notice?.Invoke(Notice1, 0);
                else if (x.Code == 2) Notice?.Invoke(Notice2, 0);
                else if (x.Code == 3) Notice?.Invoke(Notice3, 0);
                else Notice?.Invoke(NoticeOther, x.Code);
                break;
            case ItemEnchantMessage.ItemUpdate x:
                if (x.Item != null) Keep(x.Item);
                Changed?.Invoke();
                break;
            case ItemEnchantMessage.IngredientCounts:
                Changed?.Invoke();
                break;
        }
    }

    /// <summary>UpdateItemFromPacket (@0x1419c37f0): open, the body goes to the shown clone and the inventory item stays;
    /// closed, it replaces the inventory item.</summary>
    void Keep(EquipItem body)
    {
        if (IsOpen) Shown = body; else StoreItem?.Invoke(body);
    }

    /// <summary>RestoreItem (@0x1419c4050), on the dialog's completion or close: the clone replaces the inventory item.</summary>
    public void Restore()
    {
        if (Shown != null) StoreItem?.Invoke(Shown);
        Shown = null;
    }

    void Reset()
    {
        SelectedUid = 0; State = 0; infoRead = false; Ingredients = Array.Empty<EnchantIngredient>();
        Options = new Dictionary<int, int>(); Rates = default; Protector0 = Protector1 = 0; FailStack = 0; FailStackCount = 0;
        FailStackUids = Array.Empty<long>(); ResultItemId = 0; ResultRank = 0; ResultCount = 0;
    }
}
