using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UILapenshardEquipTab's equip slots panel (LapenshardEquipSlotPanel, uilapenshardsystemdialog.cpp): the six worn slots
/// m_equipSlot1..6 of the page's movie. vfunc_3 (@0x140c5d680) binds the bag "LapenshardEquipSlotBag" and gives every slot
/// setData{bindID "<bag>.<slot-1>", slotIndex, isEquip}; SetEquipEffect (@0x140c5d9c0) lights m_equipEffect of the slots
/// whose kind is the selected one. OnUnmountEquipSlot (UILapenshardEquipTab::vfunc_6 @0x140c5e140) takes the worn lapenshard
/// off (mode 2) behind the use check FUN_140c62520. Inbound modes 0 to 2 rebuild the panel (events 2, 0, 1).
/// <para>STILL-OPEN: the item bag panel (LapenshardItemSlotBagPanel) and equip by drop or right-click; the slot tooltip's
/// title strings (0x751, 0x75a).</para>
/// </summary>
public sealed class LapenshardEquipTabPage : LapenshardTabPage
{
    public const string Bag = "LapenshardEquipSlotBag";
    public const int Slots = 6;
    /// <summary>The slot panel's slot flags and the tooltip's flags, as the other item bags use.</summary>
    public const int SlotFlags = 0x1401f, TooltipFlags = 0x20;

    public override string Name => "equip";
    public override string LabelKey => "s_lapenshard_tabname_equip";

    UiHost host;
    object root;
    /// <summary>The page's movie root (the body frame's first child).</summary>
    public object Root => root;

    /// <summary>The kind (1 red, 2 blue, 4 green) a slot (1..6) takes: InitEquipParts.</summary>
    public static int KindOfSlot(int slot) => slot is 1 or 2 ? 1 : slot is 3 or 4 ? 2 : 4;

    public static string SlotName(int index) => $"{Bag}.{index}";

    static bool ParseSlot(string name, out int index)
    {
        index = -1;
        var prefix = Bag + ".";
        return name != null && name.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(prefix.Length), out index) && index is >= 0 and < Slots;
    }

    public override void Show(UiHost h, object r)
    {
        host = h;
        root = r;
        Build();
    }

    public override void Leave() { }

    public override void Destroy() { root = null; host = null; }

    /// <summary>LapenshardEquipSlotPanel::vfunc_3: a local player, then setData on every slot.</summary>
    void Build()
    {
        if (host == null || root == null || Owner?.Model == null || !Owner.Model.HasLocalPlayer) return;
        for (int i = 1; i <= Slots; i++)
        {
            var slot = host.Get(root, $"m_equipSlot{i}");
            if (slot == null) continue;
            host.Invoke(slot, "setData", host.Obj(("bindID", SlotName(i - 1)), ("slotIndex", i), ("isEquip", Owner.WornItemId(i) != 0)));
            host.Push(SlotName(i - 1));
        }
        SetEquipEffect(Owner.SelectedKind != 0);
    }

    /// <summary>SetEquipEffect (@0x140c5d9c0): m_equipEffect of the slots of the selected kind.</summary>
    void SetEquipEffect(bool on)
    {
        if (host == null || root == null) return;
        for (int i = 1; i <= Slots; i++)
        {
            if (host.Get(root, $"m_equipSlot{i}") is not { } slot) continue;
            if (host.Get(slot, "m_equipEffect") is not { } fx) continue;
            host.SetVisible(fx, on && KindOfSlot(i) == Owner.SelectedKind);
        }
    }

    public override void Notify(int ev)
    {
        switch (ev)
        {
            case 0 or 1 or 2: Build(); break;
            case 10: SetEquipEffect(true); break;
            case 11: SetEquipEffect(false); break;
        }
    }

    /// <summary>vfunc_9: worn list (mode 0) is event 2, equipped (1) is event 0, unequipped (2) is event 1.</summary>
    public override void OnPacket(LapenshardMessage m)
    {
        switch (m)
        {
            case LapenshardMessage.Worn: Notify(2); break;
            case LapenshardMessage.Equipped: Notify(0); break;
            case LapenshardMessage.Unequipped: Notify(1); break;
        }
    }

    /// <summary>OnUnmountEquipSlot: the use check, then mode 2 for a slot that has a lapenshard worn.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!name.Equals("OnUnmountEquipSlot", StringComparison.OrdinalIgnoreCase)) return false;
        if (Owner == null || !Owner.CanUse()) return true;
        int slot = args is { Length: > 0 } ? (int)Rt.ToUint32(args[0]) : 0;
        if (Owner.WornItemId(slot) != 0) Owner.Send?.Invoke(new LapenshardUnequip(slot));
        return true;
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == Bag ? Enumerable.Range(0, Slots).Select(SlotName) : Array.Empty<string>();

    Item WornItem(int index) =>
        Owner.WornItemId(index + 1) is int id and not 0 ? Item.Create(Owner.Model.Tables.Items, id, 0) : null;

    public override object GetBinding(string name)
    {
        if (!ParseSlot(name, out int i) || Owner?.Model == null) return null;
        var item = WornItem(i);
        if (item == null) return null;
        var slot = new List<(string, object)>();
        var m = Owner.Model;
        return ItemSlot.Build(slot, item, m.HasLocalPlayer, SlotFlags, m, m.ItemSlotSources) ? host?.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!ParseSlot(name, out int i) || Owner?.Model == null) return null;
        var item = WornItem(i);
        var tip = new ItemTooltip(Owner.Model, TooltipFlags) { Localize = k => host?.Player.Translate?.Invoke(k) ?? k };
        return item != null && tip.Fill(item) ? tip.Data : new AsTooltip();
    }
}
