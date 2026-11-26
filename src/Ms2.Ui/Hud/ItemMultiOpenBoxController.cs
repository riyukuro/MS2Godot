using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIItemMultiOpenBoxDialog (uiitemmultiopenboxdialog.cpp, UI id 0x4f88): the window that opens a stack of boxes by sending
/// RequestItemBox, up to ItemBoxMultiOpenMaxCount (not the 1000 limit) at a time, and counts them as the answers (ItemBox 0xAC) come back. The state is
/// <see cref="ItemMultiOpenBox"/>; the host calls <see cref="Tick"/> each frame (vfunc_15 @0x140c3f8a0) and
/// <see cref="OnItemBox"/> for each answer (OnOpenResult @0x140c3fa80).
/// STILL-OPEN: the item info slot (CreateItemInfoSlot / the movie's setItem, OnLoadComplete @0x140c3f600), the inventory
/// dialog's flag AddFlags(2) on close (vfunc_67), the movie's close tween.
/// </summary>
public sealed class ItemMultiOpenBoxController : ModalDialogController
{
    public override string Movie => "uiitemmultiopenboxdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemMultiOpenBoxDialog";   // @0x140c3f470
    public const int UiId = 0x4f88;
    public override int TypeId => UiId;

    public readonly ItemMultiOpenBox State;
    /// <summary>The server time in ms (GetTickCountMs2 is the client's own tick; both only order the due times).</summary>
    public Func<long> NowMs = () => Environment.TickCount64;
    /// <summary>Unk_Inventory::FUN_140a5e2e0(m_180): the box is still in the bag.</summary>
    public Func<int, bool> InBag = _ => false;
    /// <summary>FUN_140710590: the packet is sent.</summary>
    public Action<ItemBoxRequest> Send = _ => { };
    /// <summary>ShowMessageBox(common string, 0): the box shown for results 3 and 4.</summary>
    public Action<string> ShowNotice = _ => { };
    public event Action Closing;

    public ItemMultiOpenBoxController(int multiOpenMax) { State = new ItemMultiOpenBox(multiOpenMax); }

    object countText;

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        countText = Host.Child("m_count");
        ShowCount();
        State.Loaded(NowMs(), InBag(State.BoxId));
    }

    /// <summary>FUN_140c3fca0 (@0x140c3fca0): the "m_count" text, s_item_multi_open_box_count (used, total).</summary>
    void ShowCount()
    {
        if (countText == null) return;
        var (used, total) = State.CountText;
        Host.Set(countText, "text", ClientFormat.Format(Model.Tables.Common("s_item_multi_open_box_count") ?? "", used, total));
    }

    /// <summary>vfunc_28 (@0x140c3f810): OnCancel stops the pending send and closes.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "OnCancel", StringComparison.OrdinalIgnoreCase)) return false;
        Close();
        return true;
    }

    /// <summary>vfunc_15 (@0x140c3f8a0): the request that fell due, sent once.</summary>
    public void Tick()
    {
        if (State.Tick(NowMs()) is { } request) Send(request);
    }

    /// <summary>OnOpenResult (@0x140c3fa80): result 2 updates the count and goes on or closes; 3 and 4 show their box and close.</summary>
    public void OnItemBox(ItemBoxResult n)
    {
        switch (State.OnOpenResult(n.Opened, n.Result, NowMs()))
        {
            case ItemMultiOpenBox.Outcome.Continue: ShowCount(); break;
            case ItemMultiOpenBox.Outcome.Close: Close(); break;
            case ItemMultiOpenBox.Outcome.ShowInventoryFailBox:
                ShowNotice(Model.Tables.Common("s_err_cannot_open_multi_itembox_inventory_fail") ?? ""); Close(); break;
            case ItemMultiOpenBox.Outcome.ShowInventoryBox:
                ShowNotice(Model.Tables.Common("s_err_cannot_open_multi_itembox_inventory") ?? ""); Close(); break;
        }
    }

    /// <summary>vfunc_67 (@0x140c3f4c0): the box state cleared, the movie closed.</summary>
    public override void Close()
    {
        State.Close();
        countText = null;
        base.Close();
        Closing?.Invoke();
    }
}
