using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The own player as the Customize class sees it (PC, customize.cpp): the worn items and the two skin colours, and the two calls
/// that put an item on (vfunc_127 of the PC, "equip") and take it off (vfunc_128, "unequip"). The host binds it to the avatar
/// that is drawn; the state of <see cref="BeautyCustomize"/> does not depend on how.
/// </summary>
public interface IBeautyAvatar
{
    /// <summary>The worn items (FUN_140f014b0 over the PC's equipment).</summary>
    IReadOnlyList<EquipItem> Worn { get; }
    /// <summary>A copy of an item (FUN_140ccac60: CItem::Clone, keeping the variant).</summary>
    EquipItem Clone(EquipItem item);
    /// <summary>PC::vfunc_127(item, 1): puts the item on; an item already in its slot goes.</summary>
    void Equip(EquipItem item);
    /// <summary>PC::vfunc_128(item, 1): takes the item off.</summary>
    void Unequip(EquipItem item);
    /// <summary>FUN_141421690: the item is a real (owned) one; a preview item is not.</summary>
    bool IsRealItem(EquipItem item);
    /// <summary>FUN_1414239e0: skin colour <paramref name="index"/> (0 and 1) of the player.</summary>
    int GetSkinColor(int index);
    /// <summary>FUN_141423650: sets skin colour <paramref name="index"/>.</summary>
    void SetSkinColor(int index, int color);
    /// <summary>CEquipItem::AddTag / RemoveTag of the "Customize" tag (HideSlot / ShowSlot, @0x140cd0980, @0x140cd08c0).</summary>
    void SetSlotHidden(EquipSlot slot, bool hidden);
    /// <summary>FUN_140cd3fe0(pc, 0) (customize.cpp:527): the following puts on and takes off are one change of the worn items.</summary>
    void BeginChange() { }
    /// <summary>FUN_140cd3fe0(pc, 1) (customize.cpp:552): ends the change begun by <see cref="BeginChange"/>.</summary>
    void EndChange() { }
}

/// <summary>
/// The own-avatar side of the beauty shop and the style dialog: the client's Customize (customize.cpp, @0x140cd1570) with its
/// Equipment, EditItems and CloneItems. <see cref="Init"/> copies the worn items (CloneItems::Init @0x140ccf010); the shop's
/// previews equip other items on the <see cref="IBeautyAvatar"/>; what differs from the copies is what the shop adds, changes or
/// removes (GetAdded @0x140ccdc90, GetModified @0x140ccded0, GetRemoved @0x140cce0c0); a commit sends it, a restore puts the copies
/// back (RestoreSlot @0x140ccc4c0).
/// <para>The comparisons of two items' extra data (FUN_140cd4310 / FUN_140cd4460 / FUN_140cd4560 call CItemExtraData::vfunc_1,
/// vfunc_2 and HasSameKindAndRgb) are done on the record's own fields: the dye (<see cref="EquipItem.Color"/>) and the appearance
/// (<see cref="EquipItem.Appearance"/>). STILL-OPEN: the bodies of those three virtual methods per extra-data type, and the
/// two-slot item tests (FUN_14053d2b0, FUN_14053d460), the item's slot by id (FUN_141420f10), the avatar colour item test
/// (IsAvatarColorItem) and a new item of an id (CItem::FUN_140523c40) are host-set (<see cref="IsTopBottom"/>,
/// <see cref="IsTwoHanded"/>, <see cref="SlotOfId"/>, <see cref="IsAvatarColorId"/>, <see cref="NewItem"/>); a path that needs one
/// does nothing without it. The item controllers of EditItems (morph and drag: FUN_1414131e0), the palette colour tables
/// (ApplyColor, ApplyPalette) and the hidden-slot tag effects are not ported.</para>
/// </summary>
public sealed class BeautyCustomize : IBeautyCustomize
{
    readonly IBeautyAvatar pc;
    readonly bool preview;

    // CloneItems: m_mapItems (by uid), m_mapSlots (by slot, the first clone of a slot), m_anColor.
    readonly Dictionary<long, EquipItem> items = new();
    readonly Dictionary<int, EquipItem> slots = new();
    readonly int[] colors = new int[3];

    // EditItems: m_spItem, m_spData's item id.
    EquipItem editItem;
    int editDataId;

    // Customize: m_lstHiddenSlots.
    readonly List<EquipSlot> hidden = new();

    public BeautyCustomize(IBeautyAvatar pc, bool preview = false)
    {
        this.pc = pc; this.preview = preview;
        Init();
    }

    /// <summary>Customize::IsPreview (@0x140cd3800).</summary>
    public bool IsPreview => preview;

    /// <summary>FUN_14053d2b0: a top-and-bottom item (slots 8 and 9 go together).</summary>
    public Func<EquipItem, bool> IsTopBottom = _ => false;
    /// <summary>FUN_14053d460: a two-slot item of slots 4 and 5.</summary>
    public Func<EquipItem, bool> IsTwoHanded = _ => false;
    /// <summary>FUN_141420f10: the slot an item id goes on (0 when unknown).</summary>
    public Func<int, EquipSlot> SlotOfId = _ => 0;
    /// <summary>CItemData::IsAvatarColorItem of an item id.</summary>
    public Func<int, bool> IsAvatarColorId = _ => false;
    /// <summary>FUN_14053f870(id) != null: the item data of an id exists (CloneItems::CreateItem checks it first, customize.cpp:992-995).</summary>
    public Func<int, bool> HasItemData = _ => false;
    /// <summary>A new preview item of an id (CloneItems::CreateItem's CItem::FUN_140523c40 with the grade of FUN_141420f10), or null.</summary>
    public Func<int, EquipItem> NewItem = _ => null;
    /// <summary>The next preview uid (GenerateUniqueId).</summary>
    public Func<long> NextUid = () => 0;
    /// <summary>Equipment::SendColors (@0x140cd0b30): the two skin colours, sent as mode 6; null does nothing.</summary>
    public Action<int, int> SendColors;
    /// <summary>Equipment::SendAdded (@0x140cd0ca0): mode 3.</summary>
    public Action<EquipItem> SendAdded;
    /// <summary>Equipment::SendModified (@0x140cd0ea0): mode 5.</summary>
    public Action<EquipItem> SendModified;
    /// <summary>Equipment::SendRemoved (@0x140cd0fd0): mode 4.</summary>
    public Action<EquipItem> SendRemoved;

    static bool Same(EquipItem a, EquipItem b) => ReferenceEquals(a, b);

    // ---- CloneItems ----

    /// <summary>CloneItems::Init @0x140ccf010: copies the worn items and the colours. False without a worn item.</summary>
    public bool Init()
    {
        items.Clear(); slots.Clear();
        SaveColors();
        foreach (var worn in pc.Worn)
        {
            if (worn == null) continue;
            var clone = pc.Clone(worn);
            if (clone == null) continue;
            items[worn.Uid] = clone;
            int slot = (int)clone.Slot;
            if (!slots.ContainsKey(slot)) slots[slot] = clone;
        }
        return items.Count > 0;
    }

    /// <summary>CloneItems::SaveColors @0x140ccef20.</summary>
    public void SaveColors()
    {
        for (int i = 0; i < 3; i++) colors[i] = i < 2 ? pc.GetSkinColor(i) : 0;
    }

    /// <summary>CloneItems::ClearSlots @0x140ccefd0.</summary>
    public void ClearSlots() => slots.Clear();

    /// <summary>CloneItems::IsColorChanged @0x140ccd210: a skin colour differs from the saved one.</summary>
    public bool IsColorChanged() => colors[0] != pc.GetSkinColor(0) || colors[1] != pc.GetSkinColor(1);

    EquipItem WornByUid(long uid)
    {
        foreach (var w in pc.Worn) if (w != null && w.Uid == uid) return w;
        return null;
    }

    EquipItem WornBySlot(int slot)
    {
        foreach (var w in pc.Worn) if (w != null && (int)w.Slot == slot) return w;
        return null;
    }

    // The colours of two items are the same (FUN_140cd4560).
    static bool SameColors(EquipItem a, EquipItem b) => a != null && b != null && a.Color == b.Color;

    // The appearance extra data of two items is the same (FUN_140cd4460).
    static bool SameAppearance(EquipItem a, EquipItem b) =>
        a != null && b != null && a.Appearance.AsSpan().SequenceEqual(b.Appearance);

    // The two items are the same (FUN_140cd4310): the id, then the extra data.
    static bool SameItem(EquipItem a, EquipItem b) =>
        a != null && b != null && a.Id == b.Id && SameColors(a, b) && SameAppearance(a, b);

    /// <summary>CloneItems::GetAdded @0x140ccdc90: the worn items that are not among the copies, by uid.</summary>
    public Dictionary<long, EquipItem> GetAdded()
    {
        var o = new Dictionary<long, EquipItem>();
        foreach (var w in pc.Worn)
            if (w != null && !items.ContainsKey(w.Uid)) o[w.Uid] = pc.Clone(w);
        return o;
    }

    /// <summary>CloneItems::GetModified @0x140ccded0: the worn items that differ from their copy, by uid.</summary>
    public Dictionary<long, EquipItem> GetModified()
    {
        var o = new Dictionary<long, EquipItem>();
        foreach (var c in items.Values)
        {
            var cur = WornByUid(c.Uid);
            if (cur == null || SameItem(c, cur)) continue;
            o[cur.Uid] = cur;
        }
        return o;
    }

    /// <summary>CloneItems::GetRemoved @0x140cce0c0: the copies whose item is no longer worn, by uid.</summary>
    public Dictionary<long, EquipItem> GetRemoved()
    {
        var o = new Dictionary<long, EquipItem>();
        foreach (var c in items.Values)
            if (WornByUid(c.Uid) == null) o[c.Uid] = c;
        return o;
    }

    /// <summary>CloneItems::IsAddedUid @0x140ccd300.</summary>
    public bool IsAddedUid(long uid) => GetAdded().ContainsKey(uid);
    /// <summary>CloneItems::IsModifiedUid @0x140ccd440.</summary>
    public bool IsModifiedUid(long uid) => GetModified().ContainsKey(uid);
    /// <summary>CloneItems::IsRemovedUid @0x140ccd580.</summary>
    public bool IsRemovedUid(long uid) => GetRemoved().ContainsKey(uid);
    /// <summary>CloneItems::IsAddedSlot @0x140ccda10.</summary>
    public bool IsAddedSlot(int slot) => GetAdded().Values.Any(i => (int)i.Slot == slot);
    /// <summary>CloneItems::IsModifiedSlot @0x140ccdb50.</summary>
    public bool IsModifiedSlot(int slot) => GetModified().Values.Any(i => (int)i.Slot == slot);

    /// <summary>
    /// CloneItems::IsSameUid @0x140ccd6c0: the worn item equals its copy (the copy of the same id and slot); the flags say what
    /// differs (1 colour, 2 appearance). Without a copy the result is false, with flag 1 when a skin colour changed.
    /// </summary>
    public bool IsSameUid(long uid, out int flags)
    {
        flags = 0;
        var cur = WornByUid(uid);
        if (cur != null)
        {
            foreach (var c in items.Values)
            {
                if (cur.Id != c.Id) continue;
                if (cur.Slot != c.Slot) continue;
                if (!SameColors(c, cur)) flags |= 1;
                if (!SameAppearance(c, cur)) flags |= 2;
                return flags == 0;
            }
        }
        if (IsColorChanged()) { flags = 1; return false; }
        return false;
    }

    /// <summary>CloneItems::GetChangeFlags @0x140ccebc0: 1 colour, 2 appearance of the worn item against its copy; the skin colour counts as 1 without one.</summary>
    public int GetChangeFlags(long uid)
    {
        var cur = WornByUid(uid);
        if (cur != null && items.TryGetValue(uid, out var c))
        {
            int f = 0;
            if (!SameColors(c, cur)) f |= 1;
            if (!SameAppearance(c, cur)) f |= 2;
            return f;
        }
        return IsColorChanged() ? 1 : 0;
    }

    /// <summary>CloneItems::CreateItem @0x140cce7d0: a copy of the saved item of the id, else a new preview item (host-set).</summary>
    public EquipItem CreateItem(int itemId)
    {
        if (!HasItemData(itemId)) return null;
        foreach (var c in items.Values)
            if (c.Id == itemId) return pc.Clone(c);
        var n = NewItem(itemId);
        if (n == null) return null;
        n.Uid = NextUid();
        return n;
    }

    /// <summary>CloneItems::Restore @0x140ccbfd0: every preview item off, the copies back on, the colours back.</summary>
    public void Restore(IReadOnlyCollection<EquipSlot> tagged)
    {
        pc.BeginChange();
        try { RestoreItems(tagged); } finally { pc.EndChange(); }
    }

    void RestoreItems(IReadOnlyCollection<EquipSlot> tagged)
    {
        foreach (var w in pc.Worn.ToArray())
            if (w != null && !pc.IsRealItem(w)) pc.Unequip(w);
        foreach (var c in items.Values)
        {
            pc.Equip(c);
            if (tagged.Count > 0 && tagged.Contains(c.Slot)) pc.SetSlotHidden(c.Slot, true);
        }
        items.Clear();
        pc.SetSkinColor(0, colors[0]);
        pc.SetSkinColor(1, colors[1]);
    }

    /// <summary>CloneItems::RestoreUid @0x140ccc250: the copy of the uid back on, else the worn item off (an avatar colour item stays).</summary>
    public void RestoreUid(long uid)
    {
        if (items.TryGetValue(uid, out var c)) { pc.Equip(pc.Clone(c)); return; }
        var cur = WornByUid(uid);
        if (cur != null && !IsAvatarColorId(cur.Id)) pc.Unequip(cur);
    }

    /// <summary>
    /// CloneItems::RestoreSlot @0x140ccc4c0: slot 0 puts the two skin colours back; else the copy of that slot is put on, else the
    /// worn item of the slot comes off.
    /// </summary>
    public void RestoreSlot(int slot)
    {
        if (slot == 0)
        {
            pc.SetSkinColor(0, colors[0]);
            pc.SetSkinColor(1, colors[1]);
            return;
        }
        foreach (var c in items.Values)
        {
            if ((int)c.Slot == slot) { pc.Equip(pc.Clone(c)); return; }
        }
        var worn = WornBySlot(slot);
        if (worn != null) pc.Unequip(worn);
    }

    /// <summary>
    /// CloneItems::Apply @0x140ccc860: before an item is previewed on a slot, the copies of what the slot (and its pair, 8/9 and 4/5)
    /// held are put back, so a two-slot item does not leave half of itself on.
    /// </summary>
    public void Apply(EquipItem item, int slot)
    {
        pc.BeginChange();
        try { ApplyCopies(item, slot); } finally { pc.EndChange(); }
    }

    void ApplyCopies(EquipItem item, int slot)
    {
        int orig = slot;
        slots.TryGetValue(orig, out var old);
        bool newTwoSlot = item != null && (IsTopBottom(item) || IsTwoHanded(item));
        int pair = 0;
        if (slot == 9 || slot == 8) pair = slot == 9 ? 8 : 9;
        else if (slot == 4 || slot == 5) pair = slot == 4 ? 5 : 4;
        var list = new List<EquipItem>();
        if (old == null && pair != 0)
        {
            list.Add(null);
            slots.TryGetValue(pair, out old);
        }
        if (old != null) list.Add(old);
        bool oldTwoSlot = old != null && (IsTopBottom(old) || IsTwoHanded(old));
        if (!oldTwoSlot && pair != 0 && orig != pair && slots.TryGetValue(pair, out var pairItem))
        {
            if (!Same(old, pairItem)) list.Add(pairItem);
        }
        if (list.Count == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            var cur = list[i];
            if (cur == null) continue;
            bool equip = i == 0;
            int other = 0;
            if (IsTopBottom(cur)) other = orig == 9 ? 8 : 9;
            else if (IsTwoHanded(cur)) other = orig == 4 ? 5 : 4;
            else if (!equip) equip = newTwoSlot;
            if (other != 0)
            {
                var worn = WornBySlot(other);
                if (worn != null)
                {
                    if (slots.TryGetValue(other, out var otherClone)) equip = otherClone.Uid == worn.Uid;
                    else equip = IsTwoHanded(worn) || IsTopBottom(worn);
                }
                else equip = true;
            }
            else
            {
                var worn = WornByUid(cur.Uid);
                if (worn != null && worn.Id == cur.Id) equip = false;
            }
            if (equip) pc.Equip(pc.Clone(cur));
        }
    }

    // ---- EditItems ----

    /// <summary>EditItems::Select @0x140ccb340: the worn item becomes the edit item (a copy of it); the item controller is STILL-OPEN.</summary>
    void Select(EquipItem worn)
    {
        if (worn != null)
        {
            editItem = pc.Clone(worn);
            editDataId = editItem.Id;
        }
    }

    /// <summary>EditItems::SelectSlot @0x140ccb5f0.</summary>
    public void SelectSlot(int slot) => Select(WornBySlot(slot));

    /// <summary>EditItems::SelectUid @0x140ccb7d0.</summary>
    public void SelectUid(long uid) => Select(WornByUid(uid));

    /// <summary>EditItems::SelectId @0x140ccb680: an avatar colour item has no item, else the id's slot is selected.</summary>
    public void SelectId(int itemId)
    {
        if (IsAvatarColorId(itemId))
        {
            editItem = null; editDataId = itemId;
            return;
        }
        SelectSlot((int)SlotOfId(itemId));
    }

    /// <summary>
    /// The worn item of the uid with its colour replaced (FUN_141423cf0's SetSlotColor / EditItems::SetColors' SetSlotRgbComponent
    /// on the PC's worn item, then the redraw): the worn item is copied, dyed and put on, which replaces the one in its slot. False
    /// when nothing of the uid is worn. STILL-OPEN: the multi-slot colours of a CItemExtraData with m_bMulti (only slot 0 exists
    /// on the record).
    /// </summary>
    public bool DyeWorn(long uid, Func<EquipColor, EquipColor> dye)
    {
        var cur = WornByUid(uid);
        if (cur == null) return false;
        var c = pc.Clone(cur);
        c.Color = dye(c.Color);
        pc.Equip(c);
        return true;
    }

    /// <summary>The worn item of the uid when it differs from its copy (Equipment::GetModified(uid), customize.cpp:1253), else null.</summary>
    public EquipItem GetModifiedByUid(long uid) => GetModified().TryGetValue(uid, out var i) ? i : null;

    /// <summary>The edit item's worn colour (the edit item is a copy; the worn item of the uid has the live colour), null without one.</summary>
    public EquipItem WornEditItem() => WornByUid(EditItemUid);

    /// <summary>Customize::ResetEdit @0x140cd27a0: a fresh EditItems, nothing selected.</summary>
    public void ResetEdit() { editItem = null; editDataId = 0; }

    /// <summary>Customize::GetEditItemId @0x140cd17e0 (EditItems::GetItemId @0x140ccafa0).</summary>
    public int EditItemId => editItem != null ? editItem.Id : editDataId;
    /// <summary>Customize::GetEditItemUid @0x140cd1980.</summary>
    public long EditItemUid => editItem?.Uid ?? 0;
    /// <summary>Customize::IsDragging @0x140cd27f0: the item controller's drag; STILL-OPEN (no controller), so never.</summary>
    public bool IsDragging => false;
    /// <summary>Customize::UpdateEditItems @0x140cd1bf0: EditItems::Update only drives the item controller (STILL-OPEN), so it does nothing.</summary>
    public void UpdateEditItems() { }

    // ---- Customize ----

    /// <summary>Customize::IsEditItemAdded @0x140cd28a0.</summary>
    public bool IsEditItemAdded => IsAddedUid(EditItemUid);

    /// <summary>Customize::FUN_140cd2a00 @0x140cd2a00: anything of the edit item, or the skin colour, changed.</summary>
    public bool HasChanges
    {
        get
        {
            long uid = EditItemUid;
            return IsColorChanged() || IsModifiedUid(uid) || IsAddedUid(uid) || IsRemovedUid(uid);
        }
    }

    /// <summary>Customize::IsEditItemColorChanged @0x140cd2ea0: the change flags of the edit item have the colour bit.</summary>
    public bool IsEditItemColorChanged => (GetChangeFlags(EditItemUid) & 1) != 0;

    /// <summary>Customize::HasSelectedChangeFlag2 @0x140cd3000.</summary>
    public bool IsEditItemAppearanceChanged => (GetChangeFlags(EditItemUid) & 2) != 0;

    /// <summary>Customize::HideSlot @0x140cd2530.</summary>
    public void HideSlot(EquipSlot slot)
    {
        pc.SetSlotHidden(slot, true);
        if (!hidden.Contains(slot)) hidden.Add(slot);
    }

    /// <summary>Customize::ShowSlot @0x140cd24a0.</summary>
    public void ShowSlot(EquipSlot slot)
    {
        pc.SetSlotHidden(slot, false);
        hidden.Remove(slot);
    }

    /// <summary>Customize::ReapplyHiddenSlots: the hidden slots go back on after an edit.</summary>
    public void ReapplyHidden() => ReapplyHiddenSlots();

    void ReapplyHiddenSlots() { foreach (var s in hidden) pc.SetSlotHidden(s, true); }

    /// <summary>
    /// Customize::CommitSlot @0x140cd2280 (Equipment::CommitSlot @0x140ccfc90): the colours when they changed (mode 6), then what
    /// was added (mode 3) and modified (mode 5) on the slot is sent; the edit item is reset, the hidden slots go back on and the
    /// slot is selected again. What is sent is what CloneItems::CollectSlot @0x140ccc470 collects: GetAddedSlot (@0x140cce490) and
    /// GetModifiedSlot (@0x140cce630) each put the one item worn in that slot (PC::FUN_141655e10) into their map when the slot is
    /// added / modified, so there is at most one item per map.
    /// </summary>
    public void CommitSlot(int slot)
    {
        bool color = IsColorChanged();
        var added = new Dictionary<long, EquipItem>();
        var modified = new Dictionary<long, EquipItem>();
        var worn = WornBySlot(slot);
        if (worn != null && IsAddedSlot(slot)) added[worn.Uid] = worn;
        if (worn != null && IsModifiedSlot(slot)) modified[worn.Uid] = worn;
        ResetEdit();
        if (color) SendColors?.Invoke(pc.GetSkinColor(0), pc.GetSkinColor(1));
        foreach (var i in added.Values) SendAdded?.Invoke(i);
        foreach (var i in modified.Values) SendModified?.Invoke(i);
        ReapplyHiddenSlots();
        SelectSlot(slot);
    }

    /// <summary>Customize::RestoreSlot @0x140cd22f0 (Equipment::RestoreSlot @0x140ccfee0).</summary>
    void IBeautyCustomize.RestoreSlot(int slot) => RestoreSlotAndSelect(slot);

    /// <summary>Equipment::RestoreSlot, the hidden slots again, the slot selected.</summary>
    public void RestoreSlotAndSelect(int slot)
    {
        ResetEdit();
        RestoreSlot(slot);
        ReapplyHiddenSlots();
        SelectSlot(slot);
    }

    /// <summary>Customize::CommitAll @0x140cd1ff0 (Equipment::CommitAll @0x140ccf5a0): colours, added and modified items are sent.</summary>
    public void CommitAll()
    {
        int id = EditItemId;
        bool color = IsColorChanged();
        var added = GetAdded();
        var modified = GetModified();
        ResetEdit();
        if (color) SendColors?.Invoke(pc.GetSkinColor(0), pc.GetSkinColor(1));
        foreach (var i in added.Values) SendAdded?.Invoke(i);
        foreach (var i in modified.Values) SendModified?.Invoke(i);
        ReapplyHiddenSlots();
        SelectId(id);
    }

    /// <summary>Customize::RestoreAll @0x140cd20c0 (Equipment::RestoreAll @0x140ccf7e0): the copies back, fresh edit and clone state.</summary>
    public void RestoreAll()
    {
        int id = EditItemId;
        Restore(hidden);
        ResetEdit();
        Init();
        ReapplyHiddenSlots();
        SelectId(id);
    }

    /// <summary>The saved colour (CloneItems m_anColor), for the tests.</summary>
    public int SavedColor(int index) => colors[index];
}
