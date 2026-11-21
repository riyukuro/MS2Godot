using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui.Items;

/// <summary>CItemEnchantLimitTable (commonfiles/itemenchantlimittable.cpp): per equip slot, the item level band and the top grade
/// (table/enchantLimitTable.xml, limit rows: slot, minLv, maxLv, maxGrade).</summary>
public sealed class ItemEnchantLimitTable
{
    public readonly record struct Limit(int MinLv, int MaxLv, int MaxGrade);
    readonly Dictionary<int, Limit> map = new();
    public void Add(int slot, Limit l) => map[slot] = l;
    public bool TryGet(int slot, out Limit l) => map.TryGetValue(slot, out l);

    /// <summary>CanEnchant (@0x14058df90): an equip item (type 1) without a period, no limit break level, a limit row for its
    /// slot with the item's required level inside the band, an enchant block below the top grade, not exceptEnchant.</summary>
    public bool CanEnchant(Item item)
    {
        if (item?.Data == null || item.Data.Type != 1) return false;
        if (ItemTradeRules.HasPeriod(item)) return false;
        if (item.UnlimitedEnchantLevel > 0) return false;
        if (!map.TryGetValue(item.Data.EquipSubtype, out var l)) return false;
        if (item.Data.LevelLimit < l.MinLv || item.Data.LevelLimit > l.MaxLv) return false;
        if (item.Record?.Enchant == null) return false;
        if (item.EnchantLevel >= l.MaxGrade) return false;
        return !item.Data.ExceptEnchant;
    }

    /// <summary>FUN_14058e230 (@0x14058e230): the band test alone (the dialog's list of items it may show).</summary>
    public bool InBand(Item item)
    {
        if (item?.Data == null || !map.TryGetValue(item.Data.EquipSubtype, out var l)) return false;
        return item.Data.LevelLimit >= l.MinLv && item.Data.LevelLimit <= l.MaxLv && !item.Data.ExceptEnchant;
    }
}

/// <summary>What the dialog does about a request: <see cref="Sent"/> went out; an Ask needs the player's box answer (then the
/// matching Confirm method); the rest are refusals that send nothing.</summary>
public enum EnchantStep { Sent, NoItem, AskRefund, Reset, AskEnchantedIngredient, AskBindingCharacter, AskProtector, NotReady, OutOfRange }

/// <summary>
/// UIItemEnchantDialog's model and the client's checks before each send (client ms2/uiitemenchantdialog.cpp; UI id 0x4e92).
/// The recv side is <see cref="ItemEnchantMessage"/> through <see cref="Apply"/>.
/// <para>STILL-OPEN: the SWF fill (the dialog is not drawn), the over-enchanted ingredient box (FUN_1419c9490), the protector
/// rank warnings (FUN_1419c4360 / FUN_1419c5890), the peachy binding rule's inputs (feature 0x87, FUN_1419c89b0), the
/// MaxFailStackConst (client constant m_9828).</para>
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
    public ItemEnchantLimitTable Limits { get; set; } = new();
    public Func<long, Item> Find { get; set; } = _ => null;
    public Action<IOutPacket> Send { get; set; }
    /// <summary>The talk the dialog opened from: (talking, npc id), and the client constant m_69a0.</summary>
    public Func<(bool Talking, int NpcId)> Talk { get; set; } = () => (false, 0);
    public int TalkConstant { get; set; }
    /// <summary>The peachy path's binding box test (feature 0x87 and the item not owned by my character); default none.</summary>
    public Func<Item, bool> NeedsBindingBox { get; set; } = _ => false;
    /// <summary>min(history fail count, the client constant m_9828): the fail stack's top (SetFailStackInfo @0x1419c7610).</summary>
    public int MaxFailStackConst { get; set; } = int.MaxValue;
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
    public int N2BC { get; private set; }
    public IReadOnlyList<long> FailStackUids { get; private set; } = Array.Empty<long>();
    public int ResultItemId { get; private set; }
    public short ResultRank { get; private set; }
    public int ResultCount { get; private set; }
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

    void Out(IOutPacket p) => Send?.Invoke(p);

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
        if (Limits.CanEnchant(item)) { Out(new ItemEnchantSelectRequest(ExpMode, item.Uid)); r = EnchantStep.Sent; }
        else { SlotReset?.Invoke(); r = EnchantStep.Reset; }
        var (talking, npc) = Talk();
        Out(new ItemEnchantTalkRequest(talking, npc, TalkConstant, item.Uid));
        return r;
    }

    /// <summary>The refund box's Yes (SendRequestItemEnchant, uicommon.cpp:1993).</summary>
    public void ConfirmRefund() { if (refundUid != 0) Out(new ItemEnchantRefundRequest(refundUid)); refundUid = 0; }

    /// <summary>FUN_1419c1d60: the answer is for this mode and a result item is set.</summary>
    public bool Ready => infoRead && ResultItemId != 0;

    /// <summary>FUN_1419c9260: an item the equip ingredient bag lists: not locked (item and gems), not the selected item, the
    /// result's id and rank.</summary>
    public bool IsIngredientCandidate(Item item, long now) =>
        item != null && item.Uid != SelectedUid && ItemTradeRules.NotLocked(item, now, out _)
        && item.ItemId == ResultItemId && item.Rarity == ResultRank;

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

    /// <summary>OnClickFailStackUp / Down: mode 3 with the stack +- 1. The client sends unconditionally and the movie's buttons
    /// bound the stack by current and maximum (SetFailStackInfo); here the same bound refuses.</summary>
    public EnchantStep ChangeFailStack(int delta)
    {
        var item = Find(SelectedUid);
        if (item?.Record?.Enchant == null) return EnchantStep.NoItem;
        int top = Math.Min(item.Record.Enchant.FailStack, MaxFailStackConst);
        int next = FailStack + delta;
        if (next < 0 || next > top) return EnchantStep.OutOfRange;
        Out(new ItemEnchantFailStackRequest(next));
        return EnchantStep.Sent;
    }

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
            return EnchantStep.Sent;
        }
        if (Rates.Protect != 0) return EnchantStep.AskProtector;
        ConfirmProtector(0);
        State = 0;
        return EnchantStep.Sent;
    }

    /// <summary>The binding box's Yes: mode 6.</summary>
    public void ConfirmBinding() => Out(new ItemEnchantExpRequest(SelectedUid));

    /// <summary>FUN_1419c7a30(uid, v): v 1 uses protector 1 (m_210), 2 protector 2 (m_220), 0 none: mode 4.</summary>
    public void ConfirmProtector(int v) =>
        Out(new ItemEnchantRequest(SelectedUid, v == 1 ? Protector0 : v == 2 ? Protector1 : 0));

    /// <summary>OnItemEnchant's modes (scenestategame.cpp:6101).</summary>
    public void Apply(ItemEnchantMessage m)
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
                FailStackUids = Array.Empty<long>(); N2BC = 0;
                if (Find(x.Uid)?.Record?.Enchant is { } e) e.Charge = x.Charge;
                Changed?.Invoke();
                break;
            case ItemEnchantMessage.FailStackInfo x:
                FailStack = x.FailStack; N2BC = x.N2BC; FailStackUids = x.Uids; Rates = x.Rates; Changed?.Invoke();
                break;
            case ItemEnchantMessage.FailStackList x:
                N2BC = x.N2BC; FailStackUids = x.Uids; Changed?.Invoke();
                break;
            case ItemEnchantMessage.Refunded:
                Notice?.Invoke(0, 0);
                if (SelectedUid != 0 && Find(SelectedUid) != null) Out(new ItemEnchantSelectRequest(ExpMode, SelectedUid));
                break;
            case ItemEnchantMessage.Success x:
                if (x.Item == null) return;
                {
                    int prev = Find(x.Uid)?.EnchantLevel ?? 0;
                    StoreItem?.Invoke(x.Item);
                    Options = x.Options; State = 1;
                    Result?.Invoke(true, x.Uid, prev, x.Item.Enchant?.Level ?? prev);
                }
                break;
            case ItemEnchantMessage.Failure x:
                if (x.Item == null) return;
                {
                    int prev = Find(x.Uid)?.EnchantLevel ?? 0;
                    StoreItem?.Invoke(x.Item);
                    State = 2;
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
                if (x.Item != null) StoreItem?.Invoke(x.Item);
                Changed?.Invoke();
                break;
            case ItemEnchantMessage.IngredientCounts:
                Changed?.Invoke();
                break;
        }
    }

    void Reset()
    {
        SelectedUid = 0; State = 0; infoRead = false; Ingredients = Array.Empty<EnchantIngredient>();
        Options = new Dictionary<int, int>(); Rates = default; Protector0 = Protector1 = 0; FailStack = 0; N2BC = 0;
        FailStackUids = Array.Empty<long>(); ResultItemId = 0; ResultRank = 0; ResultCount = 0;
    }
}
