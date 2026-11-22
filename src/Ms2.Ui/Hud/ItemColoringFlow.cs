using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The host side of the item colour dialog (UIItemColoringDialog, <see cref="ItemColoringDialogController"/>): when it opens and how it
/// ends. It needs the own-avatar <see cref="IItemColoringCustomize"/> and stays shut without one. The NPC talk's "BeautyShopDialog"
/// action with kind "itemColor" opens it (dialog 0x4e23, type 3: <see cref="BeautyTalkCommand"/>).
/// <para>STILL-OPEN: the sub scene and field camera of the open (FUN_1419ae940's tail) are the host's <see cref="ItemColoringDialogController.Left"/>.</para>
/// </summary>
public sealed class ItemColoringFlow
{
    public Func<IItemColoringCustomize> CreateCustomize = () => null;
    public Func<ItemColoringDialogController> CreateController;
    public Action<ItemColoringDialogController> Show;
    public Action<string> Log = _ => { };

    public ItemColoringDialogController Current { get; private set; }
    public bool IsOpen => Current != null;

    /// <summary>Opens the dialog. False when it is already open or there is no Customize.</summary>
    public bool Open()
    {
        if (IsOpen || CreateController == null) return false;
        var customize = CreateCustomize?.Invoke();
        if (customize == null) { Log("item coloring: no own-avatar customize, the dialog stays shut"); return false; }
        var c = CreateController();
        c.Customize = customize;
        c.Closed += () => { if (Current == c) Current = null; };
        Current = c;
        Show?.Invoke(c);
        return true;
    }

    /// <summary>The scene goes away with the dialog open: the edit is put back (RestoreAll), then it closes.</summary>
    public void Shutdown()
    {
        var c = Current;
        if (c == null) return;
        c.Customize?.RestoreAll();
        c.Close();
        Current = null;
    }

    public void OnPacket(BeautyColoringPacket p)
    {
        if (IsOpen) Current.OnPacket(p);
        else Log($"item coloring: mode {p.Mode} arrived with the dialog closed");
    }
}
