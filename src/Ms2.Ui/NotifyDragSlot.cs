namespace Ms2.Ui;

/// <summary>
/// The C++ side of a dialog's drag slots (DragSlot and its subclasses, gfxui/dragslot.cpp), as the controller that owns them
/// answers for them by binding name. The defaults are DragSlot's own.
/// </summary>
public interface IDragSlotOwner
{
    /// <summary>The name is one of this owner's drag slots (DynamicCast&lt;DragSlot&gt; of its binding is not null).</summary>
    bool IsDragSlot(string name);

    /// <summary>DragSlot::vfunc_10 (dragslot.cpp:33): PushBinding, the slot's value pushed to the movie again.</summary>
    void RefreshSlot(string name);

    /// <summary>DragSlot::vfunc_12 (dragslot.cpp:51): dropped back on itself; vfunc_10.</summary>
    void SlotReturned(string name) => RefreshSlot(name);

    /// <summary>The destination's vfunc_18 with the dragged slot (DragSlot::vfunc_18, dragslot.cpp:93: nothing).</summary>
    void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) { }

    /// <summary>
    /// A drag that ended on no drag slot. The client picks vfunc_12 or vfunc_17 by GFxManager FUN_1422aeed0(2), which is
    /// not recovered (STILL-OPEN), so the owner is told only that this branch was reached.
    /// </summary>
    void SlotDropUnresolved(string name);

    /// <summary>vfunc_20, the drag started (DragSlot::vfunc_20, dragslot.cpp:113: its dragging flag; not kept here).</summary>
    void SlotDragStarted(string name) { }

    /// <summary>vfunc_21, the drag over (DragSlot::vfunc_21, dragslot.cpp:121: its dragging flag; not kept here).</summary>
    void SlotDragDone(string name) { }
}

/// <summary>
/// NotifyContext's binding map (ms_mapBindings, notifycontext.cpp:175; one for every dialog) for drag slots: the owners
/// of the dialogs that are loaded, asked in the order they were added.
/// </summary>
public sealed class BindingRegistry
{
    readonly List<IDragSlotOwner> owners = new();

    public void Add(IDragSlotOwner owner) { if (owner != null && !owners.Contains(owner)) owners.Add(owner); }
    public void Remove(IDragSlotOwner owner) => owners.Remove(owner);

    /// <summary>FindBinding(name) then DynamicCast&lt;DragSlot&gt;: the owner of the drag slot of that name, or null.</summary>
    public IDragSlotOwner Find(string name) => name == null ? null : owners.FirstOrDefault(o => o.IsDragSlot(name));
}

/// <summary>GameDataModel's drag callbacks, NotifyDragSlot (gfxui/notifydragslot.cpp), over a <see cref="BindingRegistry"/>.</summary>
public static class NotifyDragSlot
{
    /// <summary>SlotDragStart (@0x1423024d0): no argument, or null / undefined, nothing; the slot's vfunc_20.</summary>
    public static void SlotDragStart(BindingRegistry slots, object[] args)
    {
        if (args.Length < 1 || args[0] is not string name) return;
        slots.Find(name)?.SlotDragStarted(name);
    }

    /// <summary>
    /// SlotDragEnd (@0x1423025d0): fewer than two arguments or no source name, nothing. The destination name is the second
    /// argument when it is a string. A source that is a drag slot: dropped on itself, vfunc_12; on another drag slot, the
    /// destination's vfunc_18 with the source; on none, the open FUN_1422aeed0(2) branch
    /// (<see cref="IDragSlotOwner.SlotDropUnresolved"/>); then always the source's vfunc_21.
    /// </summary>
    public static void SlotDragEnd(BindingRegistry slots, object[] args)
    {
        if (args.Length < 2 || args[0] is not string src) return;
        string dst = args[1] as string;
        var srcOwner = slots.Find(src);
        if (srcOwner == null) return;
        var dstOwner = slots.Find(dst);
        if (dstOwner != null)
        {
            if (dstOwner == srcOwner && dst == src) dstOwner.SlotReturned(dst);
            else dstOwner.SlotDroppedOn(dst, src, srcOwner);
        }
        else srcOwner.SlotDropUnresolved(src);
        srcOwner.SlotDragDone(src);
    }
}
