namespace Ms2.Ui.Items;

/// <summary>
/// ItemInfoSlot (ms2/client/ms2/itemcustomizecontroller_2.cpp), an item slot's binding: CreateItemInfoSlot (:184)
/// registers it under its name in the binding table the movies read (Unk_NotifyContext::RegisterBinding; here
/// <see cref="UiModel.ItemSlots"/>, which <see cref="UiController.GetBinding"/> reads).
/// </summary>
public sealed class ItemInfoSlot
{
    public readonly string Name;
    public readonly Item Item;
    public readonly int Index;
    /// <summary>m_nIconType: the ASSlot flags vfunc_8 builds the slot with (the constructor's 0x415 when 0).</summary>
    public readonly int IconType;
    /// <summary>m_nFlags (the constructor's 0x20 when 0), which the tooltip path reads.</summary>
    public readonly int Flags;
    /// <summary>m_strCount: a count text set over the slot's own (vfunc_8 writes it as itemCount when not empty).</summary>
    public string CountText = "";

    /// <summary>m_strSlotText and m_strTip (the CreateParam form): an icon path and a text tooltip for a slot without an item.</summary>
    public readonly string SlotText = "", Tip = "";
    /// <summary>m_nTipArg: the text tooltip's align, -1 for none.</summary>
    public readonly int TipAlign = -1;

    public ItemInfoSlot(string name, Item item, int index, int iconType, int flags)
    {
        Name = name;
        Item = item;
        Index = index;
        IconType = iconType != 0 ? iconType : 0x415;
        Flags = flags != 0 ? flags : 0x20;
    }

    /// <summary>
    /// The CreateParam form (@0x1414157d0, which FUN_141415560 registers; CreateParam's ctor @0x141415390 leaves the tip
    /// arg -1): no item, the slot text (an icon path), the tip, its align and the count text.
    /// </summary>
    public ItemInfoSlot(string name, string slotText, string tip, int flags, int tipAlign, string count)
        : this(name, null, 0, 0, flags)
    {
        SlotText = slotText ?? "";
        Tip = tip ?? "";
        TipAlign = tipAlign;
        CountText = count ?? "";
    }

    /// <summary>
    /// vfunc_0 (:314), the text tooltip of a slot without an item: ASTooltip(player, m_nFlags, 2).ApplyTextTooltip(m_strTip),
    /// then SetTooltipAlign(m_nTipArg) unless -1; null for an item slot or an empty tip (the item tooltip is the caller's).
    /// </summary>
    public AsTooltip TextTooltip()
    {
        if (Item != null || Tip.Length == 0) return null;
        var tip = new AsTooltip();
        if (!tip.ApplyTextTooltip(Tip)) return null;
        if (TipAlign != -1) tip.SetTooltipAlign(TipAlign);
        return tip;
    }

    /// <summary>
    /// vfunc_8 (:388): an ASSlot(2) object built by ASSlot::FUN_1409c3860 with the local player and the icon type, then the
    /// count text; null when the build fails. Without an item, a slot text gives FUN_1409c48b0 (:1472: the text as the
    /// icon path, SetIconPathString, and DisableDrag) and the count text.
    /// </summary>
    public object Value(UiHost host, UiModel model, ItemSlot.Sources sources = null)
    {
        if (Item == null)
        {
            if (SlotText.Length == 0) return null;
            var t = new List<(string, object)> { ("iconPath", SlotText), ("disableDrag", 1) };
            if (CountText.Length > 0) t.Add(("itemCount", CountText));
            return host.Obj(t.ToArray());
        }
        var d = new List<(string, object)>();
        if (!ItemSlot.Build(d, Item, model.HasLocalPlayer, IconType, model, sources)) return null;
        if (CountText.Length > 0) d.Add(("itemCount", CountText));
        return host.Obj(d.ToArray());
    }
}

/// <summary>
/// ItemSlotBinder (ms2/client/ms2/itemslotbinder.cpp): binds items under "&lt;owner&gt;_&lt;n&gt;" slot names, with icon type
/// 0x11 (the item and its count) and flags 0x800 unless set (:29).
/// </summary>
public sealed class ItemSlotBinder
{
    readonly UiModel model;
    readonly string owner;     // m_18
    int count;                 // m_20
    public int IconType = 0x11, Flags = 0x800;    // m_24 (SetIconType :40), m_28 (SetFlags :46)
    readonly SortedSet<string> slots = new(StringComparer.Ordinal);

    public ItemSlotBinder(UiModel model, string owner) { this.model = model; this.owner = owner; }

    /// <summary>BindNewItem (:52): a new item (FUN_14052b130 with no status amount), its count clamped; "" for an unknown
    /// item.</summary>
    public string BindNewItem(int itemId, int rarity, int amount)
    {
        var item = Item.Create(model.Tables.Items, itemId, rarity);
        if (item == null) return "";
        item.SetCountClamped(amount);
        return BindItem(item);
    }

    /// <summary>BindItem (:86): an existing item; "" for none.</summary>
    public string BindItem(Item item)
    {
        if (item == null) return "";
        count++;
        string name = $"{owner}_{count}";
        model.ItemSlots[name] = new ItemInfoSlot(name, item, 0, IconType, Flags);   // CreateItemInfoSlot
        slots.Add(name);
        return name;
    }

    /// <summary>FUN_140d438f0 (:98): unbinds one of this binder's slots.</summary>
    public void Unbind(string name)
    {
        if (slots.Remove(name)) model.ItemSlots.Remove(name);           // DestroyItemInfoSlot
    }

    /// <summary>FUN_140d43990 (:108), also the destructor's: unbinds all.</summary>
    public void Clear()
    {
        foreach (var s in slots) model.ItemSlots.Remove(s);
        slots.Clear();
    }
}
