using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The state and timing of UIItemMultiOpenBoxDialog (client/ms2/uiitemmultiopenboxdialog.cpp, UI id 0x4f88, type name
/// "ItemMultiOpenBoxDialog"): the box being opened, how many are left, and the one RequestItemBox in flight. The dialog opens
/// the boxes in batches: 500 ms after it loads, then 100 ms after each "opened" answer, it sends min(left, multiOpenMax).
/// <para>STILL-OPEN: the movie itself (setItem, m_count text, OnCancel from the movie, the item info slot, the inventory
/// dialog flag AddFlags(2) on close); here only the packet logic is ported.</para>
/// </summary>
public sealed class ItemMultiOpenBox
{
    public int BoxId { get; private set; }          // m_180
    public short Slot { get; private set; }         // m_184
    public string Key { get; private set; } = "";   // m_188
    public int Total { get; private set; }          // m_190
    public int Left { get; private set; }           // m_194
    long dueAt;                                     // m_198 (0 = nothing due)
    readonly int multiOpenMax;

    public ItemMultiOpenBox(int multiOpenMax) { this.multiOpenMax = multiOpenMax; }

    public bool Active => BoxId != 0;

    /// <summary>The "m_count" text arguments: boxes opened so far and the total (FUN_140c3fca0).</summary>
    public (int Used, int Total) CountText => (Total - Left, Total);

    /// <summary>FUN_140c3f9d0: remember the box and the count, nothing due yet (the load schedules the first send).</summary>
    public void Open(int boxId, short slot, string key, int count)
    {
        BoxId = boxId; Slot = slot; Total = count; Key = key ?? ""; Left = count; dueAt = 0;
    }

    /// <summary>OnLoadComplete (@0x140c3f600): the first send is due 500 ms later, when the box is still in the bag.</summary>
    public void Loaded(long nowMs, bool itemInBag) { if (itemInBag) dueAt = nowMs + 500; }

    /// <summary>vfunc_15 (@0x140c3f8a0): the request that is due at this time, once (null when none).</summary>
    public ItemBoxRequest? Tick(long nowMs)
    {
        if (dueAt == 0 || nowMs <= dueAt) return null;
        dueAt = 0;
        return new ItemBoxRequest(BoxId, Slot, Math.Min(Left, multiOpenMax), Key);
    }

    /// <summary>What OnOpenResult asks the window to do.</summary>
    public enum Outcome { Ignored, Continue, Close, ShowInventoryFailBox, ShowInventoryBox }

    /// <summary>OnOpenResult (@0x140c3fa80): result 2 counts the opened boxes and goes on (or closes at 0 left); 3 and 4 show their box and close.</summary>
    public Outcome OnOpenResult(int opened, int result, long nowMs)
    {
        if (!Active) return Outcome.Ignored;   // vfunc_55() != 0: the dialog is not open
        Left -= opened;
        if (result == ItemBoxResult.ResultOpened)
        {
            if (Left > 0) { dueAt = nowMs + 100; return Outcome.Continue; }
            Close();
            return Outcome.Close;
        }
        var o = result switch { ItemBoxResult.ResultFailed => Outcome.ShowInventoryFailBox, ItemBoxResult.ResultInventoryFull => Outcome.ShowInventoryBox, _ => Outcome.Close };
        Close();
        return o;
    }

    /// <summary>vfunc_67 / OnCancel: everything cleared.</summary>
    public void Close() { BoxId = 0; Slot = 0; Total = 0; Left = 0; dueAt = 0; Key = ""; }
}
